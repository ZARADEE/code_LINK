using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
namespace CodeLink;
public static class WindowsSecret
{
    [StructLayout(LayoutKind.Sequential)]struct Blob{public int Length;public IntPtr Data;}
    public static string Protect(string text)=>OperatingSystem.IsMacOS()?MacSecret.Protect(text):"dpapi:"+System.Convert.ToBase64String(Convert(Encoding.UTF8.GetBytes(text),true));
    public static string Unprotect(string text)=>text.StartsWith("keychain:")?MacSecret.Unprotect(text):text.StartsWith("dpapi:")?Encoding.UTF8.GetString(Convert(System.Convert.FromBase64String(text[6..]),false)):text;
    private static byte[] Convert(byte[] bytes,bool protect)
    {
        var input=new Blob{Length=bytes.Length,Data=Marshal.AllocHGlobal(bytes.Length)};Blob output=default;
        try
        {
            Marshal.Copy(bytes,0,input.Data,bytes.Length);
            var ok=protect?CryptProtectData(ref input,null,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output):CryptUnprotectData(ref input,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output);
            if(!ok)throw new Win32Exception();var result=new byte[output.Length];Marshal.Copy(output.Data,result,0,result.Length);return result;
        }
        finally{Marshal.FreeHGlobal(input.Data);if(output.Data!=IntPtr.Zero)LocalFree(output.Data);}
    }
    [DllImport("crypt32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool CryptProtectData(ref Blob input,string? desc,IntPtr entropy,IntPtr reserved,IntPtr prompt,uint flags,out Blob output);
    [DllImport("crypt32.dll",SetLastError=true)]static extern bool CryptUnprotectData(ref Blob input,IntPtr desc,IntPtr entropy,IntPtr reserved,IntPtr prompt,uint flags,out Blob output);
    [DllImport("kernel32.dll")]static extern IntPtr LocalFree(IntPtr memory);
}
