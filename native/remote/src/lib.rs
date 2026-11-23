//! Byte-stream transport only. Code Link's TLS, pairing and authorization stay
//! above this layer. Never forward to a caller-selected remote TCP destination.
use anyhow::{Context, Result, bail};
use iroh::{Endpoint, EndpointAddr, EndpointId, RelayUrl, SecretKey, Watcher, endpoint::presets};
use serde::{Deserialize, Serialize};
use std::{collections::HashMap, ffi::{CStr, c_char}, net::{IpAddr, Ipv4Addr}, sync::{Arc, Mutex, OnceLock, atomic::{AtomicU64, Ordering}}, time::Duration};
use tokio::{io::{AsyncReadExt, AsyncWriteExt}, net::{TcpListener, TcpStream}, sync::Semaphore};
use tokio_util::sync::CancellationToken;

const ALPN: &[u8] = b"codelink/remote/1";
const MAX_CONFIG: usize = 16384;
static NEXT: AtomicU64 = AtomicU64::new(1);
static BRIDGES: OnceLock<Mutex<HashMap<u64, Arc<Bridge>>>> = OnceLock::new();
static RUNTIME: OnceLock<tokio::runtime::Runtime> = OnceLock::new();

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct Config {
    role: String,
    secret: String,
    #[serde(default)] peer: String,
    #[serde(default)] relays: Vec<String>,
    #[serde(default)] dns_servers: Vec<String>,
    #[serde(default)] target_port: u16,
    #[serde(default)] relay_only: bool,
}

#[derive(Default, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
struct Status {
    state: String,
    endpoint: String,
    relays: Vec<String>,
    relay_online: bool,
    relay_error: String,
    port: u16,
    path: String,
    active: u32,
    sent: u64,
    received: u64,
    error: String,
}
struct Bridge {
    status: Mutex<Status>,
    cancel: CancellationToken,
    endpoint: Mutex<Option<Endpoint>>,
}
fn bridges() -> &'static Mutex<HashMap<u64, Arc<Bridge>>> { BRIDGES.get_or_init(Mutex::default) }
fn runtime() -> &'static tokio::runtime::Runtime {
    RUNTIME.get_or_init(|| tokio::runtime::Builder::new_multi_thread().worker_threads(2).enable_all().build().expect("network runtime"))
}
fn parse_secret(value: &str) -> Result<SecretKey> {
    if value.len() != 64 || !value.is_ascii() { bail!("invalid identity length"); }
    let mut bytes = [0u8; 32];
    for (i, byte) in bytes.iter_mut().enumerate() { *byte = u8::from_str_radix(&value[i*2..i*2+2], 16).context("invalid identity")?; }
    Ok(SecretKey::from_bytes(&bytes))
}
fn start(json: &str) -> Result<u64> {
    if json.len() > MAX_CONFIG { bail!("configuration too large"); }
    let config: Config = serde_json::from_str(json)?;
    parse_secret(&config.secret)?;
    if config.role != "server" && config.role != "client" { bail!("invalid role"); }
    if config.role == "server" && config.target_port == 0 { bail!("invalid service port"); }
    if config.role == "client" { config.peer.parse::<EndpointId>().context("invalid peer")?; }
    for relay in &config.relays {
        let url: RelayUrl = relay.parse()?;
        if url.scheme() != "https" { bail!("relay requires HTTPS"); }
    }
    let state = Arc::new(Bridge { status: Mutex::new(Status { state: "starting".into(), ..Status::default() }), cancel: CancellationToken::new(), endpoint: Mutex::new(None) });
    let id = NEXT.fetch_add(1, Ordering::Relaxed);
    bridges().lock().unwrap().insert(id, state.clone());
    runtime().spawn(async move {
        if let Err(error) = run(config, state.clone()).await {
            let mut status = state.status.lock().unwrap();
            status.state = "error".into();
            status.error = format!("{error:#}");
        }
        // Also stop monitors and in-flight streams when an accept loop exits
        // with an error, not only when the caller explicitly closes the handle.
        state.cancel.cancel();
        let ep = state.endpoint.lock().unwrap().take();
        if let Some(ep) = ep { ep.close().await; }
    });
    Ok(id)
}
async fn run(config: Config, state: Arc<Bridge>) -> Result<()> {
    let mut builder = Endpoint::builder(presets::N0).secret_key(parse_secret(&config.secret)?).alpns(vec![ALPN.to_vec()]);
    // Android has no resolv.conf. The Java side supplies DNS from the active
    // network instead of hard-coding a resolver that campus networks may block.
    if !config.dns_servers.is_empty() {
        use iroh::dns::{DnsResolver, NameserverConfig};
        let servers: Vec<_> = config.dns_servers.iter().filter_map(|s| s.parse::<IpAddr>().ok()).flat_map(|ip| {
            [NameserverConfig::udp(ip), NameserverConfig::tcp(ip)]
        }).collect();
        if !servers.is_empty() { builder = builder.dns_resolver(DnsResolver::builder().add_nameserver_configs(servers).build()); }
    }
    if config.relay_only { builder = builder.clear_ip_transports(); }
    let ep = builder.bind().await?;
    *state.endpoint.lock().unwrap() = Some(ep.clone());
    state.status.lock().unwrap().endpoint = ep.id().to_string();
    let listener = if config.role == "client" {
        let listener = TcpListener::bind((Ipv4Addr::LOCALHOST, 0)).await?;
        state.status.lock().unwrap().port = listener.local_addr()?.port();
        Some(listener)
    } else { None };
    state.status.lock().unwrap().state = "ready".into();
    let monitor_ep = ep.clone(); let monitor_state = state.clone();
    let monitor = tokio::spawn(async move {
        loop {
            let relays = monitor_ep.addr().relay_urls().map(ToString::to_string).collect();
            let relay_status = monitor_ep.home_relay_status().get();
            {
                let mut s = monitor_state.status.lock().unwrap();
                s.relays = relays;
                s.relay_online = relay_status.iter().any(|r| r.is_connected());
                s.relay_error = relay_status.iter().filter_map(|r| r.last_error()).map(|e| format!("{e:#}")).collect::<Vec<_>>().join("; ");
            }
            tokio::select! { _ = monitor_state.cancel.cancelled() => break, _ = tokio::time::sleep(Duration::from_secs(1)) => {} }
        }
    });
    let permits = Arc::new(Semaphore::new(8));
    if let Some(listener) = listener {
        let peer: EndpointId = config.peer.parse()?;
        loop {
            let accepted = tokio::select! { _ = state.cancel.cancelled() => break, a = listener.accept() => a? };
            let Ok(permit) = permits.clone().try_acquire_owned() else { continue };
            let ep = ep.clone(); let state = state.clone(); let relays = config.relays.clone();
            tokio::spawn(async move {
                let _permit = permit;
                let result = async {
                    let mut addr = EndpointAddr::new(peer);
                    for url in relays { addr = addr.with_relay_url(url.parse()?); }
                    let conn = tokio::time::timeout(Duration::from_secs(30), ep.connect(addr, ALPN)).await.context("remote connection timed out")??;
                    let (send, recv) = conn.open_bi().await?;
                    forward(accepted.0, send, recv, conn, state.clone()).await
                };
                let result: Result<()> = tokio::select! { _ = state.cancel.cancelled() => Ok(()), result = result => result };
                if let Err(error) = result { state.status.lock().unwrap().error = format!("{error:#}"); }
            });
        }
    } else {
        loop {
            let incoming = tokio::select! { _ = state.cancel.cancelled() => break, a = ep.accept() => match a { Some(a) => a, None => break } };
            let Ok(permit) = permits.clone().try_acquire_owned() else { incoming.refuse(); continue };
            let state = state.clone(); let port = config.target_port;
            tokio::spawn(async move {
                let _permit = permit;
                let result = async {
                    let conn = tokio::time::timeout(Duration::from_secs(15), incoming).await??;
                    let (send, recv) = tokio::time::timeout(Duration::from_secs(15), conn.accept_bi()).await??;
                    let tcp = TcpStream::connect((Ipv4Addr::LOCALHOST, port)).await?;
                    forward(tcp, send, recv, conn, state.clone()).await
                };
                let result: Result<()> = tokio::select! { _ = state.cancel.cancelled() => Ok(()), result = result => result };
                if let Err(error) = result { state.status.lock().unwrap().error = format!("{error:#}"); }
            });
        }
    }
    monitor.abort();
    ep.close().await;
    Ok(())
}
struct Active(Arc<Bridge>);
impl Drop for Active { fn drop(&mut self) { let mut s = self.0.status.lock().unwrap(); s.active = s.active.saturating_sub(1); } }
async fn forward(tcp: TcpStream, mut send: iroh::endpoint::SendStream, mut recv: iroh::endpoint::RecvStream, conn: iroh::endpoint::Connection, state: Arc<Bridge>) -> Result<()> {
    tcp.set_nodelay(true)?;
    { let mut s = state.status.lock().unwrap(); s.active += 1; s.error.clear(); }
    let _active = Active(state.clone());
    let (mut reader, mut writer) = tcp.into_split();
    let sending = async {
        let mut buf = [0u8; 16384];
        loop { let n = reader.read(&mut buf).await?; if n == 0 { send.finish()?; break; } send.write_all(&buf[..n]).await?; state.status.lock().unwrap().sent += n as u64; }
        Ok::<_, anyhow::Error>(())
    };
    let receiving = async {
        let mut buf = [0u8; 16384];
        while let Some(n) = recv.read(&mut buf).await? { writer.write_all(&buf[..n]).await?; state.status.lock().unwrap().received += n as u64; }
        writer.shutdown().await?;
        Ok::<_, anyhow::Error>(())
    };
    let watching = async {
        loop {
            if let Some(path) = conn.paths().iter().find(|p| p.is_selected()) {
                state.status.lock().unwrap().path = if path.remote_addr().is_ip() { "direct" } else { "relay" }.into();
            }
            tokio::time::sleep(Duration::from_millis(250)).await;
        }
    };
    let result = tokio::select! {
        result = async { tokio::try_join!(sending, receiving)?; Ok(()) } => result,
        _ = state.cancel.cancelled() => Ok(()),
        error = conn.closed() => Err(anyhow::anyhow!("{error}")),
        _ = watching => Ok(()),
    };
    conn.close(0u32.into(), b"stream closed");
    result
}
fn status(id: u64) -> String {
    let state = bridges().lock().unwrap().get(&id).cloned();
    state.map(|s| serde_json::to_string(&*s.status.lock().unwrap()).unwrap()).unwrap_or_else(|| r#"{"state":"stopped"}"#.into())
}
fn stop(id: u64) { if let Some(state) = bridges().lock().unwrap().remove(&id) { state.cancel.cancel(); } }
fn network_changed(id: u64) {
    let state = bridges().lock().unwrap().get(&id).cloned();
    if let Some(state) = state {
        let ep = state.endpoint.lock().unwrap().clone();
        if let Some(ep) = ep { runtime().spawn(async move { ep.network_change().await; }); }
    }
}

/// The caller owns a valid NUL-terminated UTF-8 configuration for this call.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn cl_remote_start(config: *const c_char) -> u64 {
    if config.is_null() { return 0; }
    std::panic::catch_unwind(|| unsafe { CStr::from_ptr(config) }.to_str().ok().and_then(|s| start(s).ok()).unwrap_or(0)).unwrap_or(0)
}
/// Writes a NUL-terminated JSON snapshot, returning bytes needed including NUL.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn cl_remote_status(id: u64, output: *mut u8, capacity: usize) -> usize {
    let value = status(id); let needed = value.len()+1;
    if !output.is_null() && capacity >= needed { unsafe { std::ptr::copy_nonoverlapping(value.as_ptr(), output, value.len()); *output.add(value.len()) = 0; } }
    needed
}
#[unsafe(no_mangle)] pub extern "C" fn cl_remote_stop(id: u64) { stop(id); }
#[unsafe(no_mangle)] pub extern "C" fn cl_remote_network_changed(id: u64) { network_changed(id); }

#[cfg(target_os = "android")]
mod android {
    use super::*;
    use jni::{JNIEnv, objects::{JClass, JString}, sys::{jlong, jstring}};
    #[unsafe(no_mangle)]
    pub extern "system" fn Java_com_codelink_app_RemoteNative_start(mut env: JNIEnv, _: JClass, config: JString) -> jlong {
        match env.get_string(&config) { Ok(value) => start(&String::from(value)).unwrap_or(0) as jlong, Err(_) => 0 }
    }
    #[unsafe(no_mangle)]
    pub extern "system" fn Java_com_codelink_app_RemoteNative_status(env: JNIEnv, _: JClass, id: jlong) -> jstring {
        env.new_string(status(id as u64)).map(|s| s.into_raw()).unwrap_or(std::ptr::null_mut())
    }
    #[unsafe(no_mangle)] pub extern "system" fn Java_com_codelink_app_RemoteNative_stop(_: JNIEnv, _: JClass, id: jlong) { stop(id as u64); }
    #[unsafe(no_mangle)] pub extern "system" fn Java_com_codelink_app_RemoteNative_networkChanged(_: JNIEnv, _: JClass, id: jlong) { network_changed(id as u64); }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test] fn identity_validation_does_not_panic_on_unicode_or_short_keys() {
        assert!(parse_secret("秘密").is_err()); assert!(parse_secret(&"g".repeat(64)).is_err()); assert!(parse_secret(&"0".repeat(64)).is_ok());
    }
    #[test] fn reject_arbitrary_roles_and_plaintext_relays() {
        assert!(start(r#"{"role":"proxy","secret":""}"#).is_err());
        let json = serde_json::json!({"role":"server", "secret":"1".repeat(64), "targetPort":1, "relays":["http://example.com"]});
        assert!(start(&json.to_string()).is_err());
    }
    #[test] fn stopping_unknown_handle_is_idempotent() { stop(0); stop(0); assert!(status(0).contains("stopped")); }
}
