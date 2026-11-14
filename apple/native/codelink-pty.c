#include <util.h>
#include <sys/ioctl.h>
#include <sys/wait.h>
#include <poll.h>
#include <unistd.h>
#include <signal.h>
#include <stdlib.h>
#include <stdio.h>
#include <string.h>
#include <errno.h>
#include <stdint.h>

static volatile sig_atomic_t child = -1;
static void stop(int sig) { if (child > 0) kill(-child, sig); }
static int write_all(int fd, const void *data, size_t size) {
    const char *p=data;
    while(size) { ssize_t n=write(fd,p,size); if(n<0&&errno==EINTR)continue; if(n<=0)return -1; p+=n;size-=n; }
    return 0;
}
int main(int argc,char **argv) {
    if(argc!=2) { fprintf(stderr,"Usage: codelink-pty COMMAND\n");return 64; }
    int master;struct winsize size={.ws_row=32,.ws_col=120};
    child=forkpty(&master,NULL,NULL,&size);
    if(child<0){perror("forkpty");return 1;}
    if(child==0){execl("/bin/zsh","zsh","-l","-c",argv[1],(char*)NULL);_exit(127);}
    signal(SIGTERM,stop);signal(SIGHUP,stop);signal(SIGINT,stop);signal(SIGPIPE,SIG_IGN);
    unsigned char input[1048581];size_t used=0;int status=0,ended=0;
    while(!ended) {
        struct pollfd fds[2]={{master,POLLIN,0},{STDIN_FILENO,POLLIN,0}};
        int result=poll(fds,2,100);
        if(result<0&&errno!=EINTR)break;
        if(fds[0].revents&(POLLIN|POLLHUP)) {
            char bytes[8192];ssize_t n=read(master,bytes,sizeof(bytes));
            if(n>0){if(write_all(STDOUT_FILENO,bytes,(size_t)n)<0)break;}
            else if(n==0||(n<0&&errno!=EINTR)){ended=1;}
        }
        if(fds[1].revents&POLLIN) {
            ssize_t n=read(STDIN_FILENO,input+used,sizeof(input)-used);
            if(n<=0)break;used+=(size_t)n;
            while(used>=5) {
                uint32_t length=((uint32_t)input[1]<<24)|((uint32_t)input[2]<<16)|((uint32_t)input[3]<<8)|input[4];
                if(length>1048576){ended=1;break;}
                if(used<length+5)break;
                if(input[0]==1){if(write_all(master,input+5,length)<0){ended=1;break;}}
                else if(input[0]==2&&length==4){size.ws_col=(input[5]<<8)|input[6];size.ws_row=(input[7]<<8)|input[8];ioctl(master,TIOCSWINSZ,&size);}
                else if(input[0]==3){ended=1;break;}
                else {ended=1;break;}
                used-=length+5;memmove(input,input+length+5,used);
            }
        }
        if(fds[1].revents&(POLLHUP|POLLERR))break;
    }
    close(master);kill(-child,SIGHUP);
    for(int i=0;i<100;i++){pid_t r=waitpid(child,&status,WNOHANG);if(r==child){child=-1;return WIFEXITED(status)?WEXITSTATUS(status):128+WTERMSIG(status);}if(r<0&&errno!=EINTR)return 1;usleep(10000);}
    kill(-child,SIGKILL);while(waitpid(child,&status,0)<0&&errno==EINTR){}
    return WIFEXITED(status)?WEXITSTATUS(status):128+WTERMSIG(status);
}
