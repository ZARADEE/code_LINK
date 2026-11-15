import SwiftUI
import WebKit

struct TerminalView {
    var text: String
    var sessionID: String
    var observer: Bool
    var enabled: Bool
    var onInput: (String) -> Void
    func makeCoordinator() -> TerminalCoordinator { TerminalCoordinator() }
    func makeWebView(_ coordinator: TerminalCoordinator) -> WKWebView {
        let config = WKWebViewConfiguration()
        config.userContentController.add(coordinator, name:"terminal")
        let view = WKWebView(frame:.zero, configuration:config)
        view.navigationDelegate = coordinator; coordinator.webView = view
        if let url = Bundle.main.url(forResource:"terminal", withExtension:"html", subdirectory:"Terminal") {
            view.loadFileURL(url, allowingReadAccessTo:url.deletingLastPathComponent())
        }
        #if os(iOS)
        view.isOpaque = false; view.backgroundColor = UIColor(red:0.098, green:0.176, blue:0.153, alpha:1)
        #endif
        return view
    }
    func update(_ coordinator: TerminalCoordinator) { coordinator.onInput = onInput; coordinator.update(text:text, id:sessionID, observer:observer, enabled:enabled) }
}
#if os(macOS)
extension TerminalView: NSViewRepresentable {
    func makeNSView(context:Context) -> WKWebView { makeWebView(context.coordinator) }
    func updateNSView(_ view:WKWebView, context:Context) { update(context.coordinator) }
    static func dismantleNSView(_ view:WKWebView, coordinator:TerminalCoordinator) { view.configuration.userContentController.removeScriptMessageHandler(forName:"terminal") }
}
#else
extension TerminalView: UIViewRepresentable {
    func makeUIView(context:Context) -> WKWebView { makeWebView(context.coordinator) }
    func updateUIView(_ view:WKWebView, context:Context) { update(context.coordinator) }
    static func dismantleUIView(_ view:WKWebView, coordinator:TerminalCoordinator) { view.configuration.userContentController.removeScriptMessageHandler(forName:"terminal") }
}
#endif
final class TerminalCoordinator: NSObject, WKNavigationDelegate, WKScriptMessageHandler {
    weak var webView: WKWebView?
    var onInput: ((String) -> Void)?
    private var ready = false
    private var buffer = TerminalRenderBuffer()
    private var generation = 0
    private var retries = 0
    private var pendingToken: String?
    private var pendingSession = ""
    func webView(_ webView: WKWebView, didStartProvisionalNavigation navigation: WKNavigation!) {
        generation += 1; ready = false; pendingToken = nil; buffer.reset()
    }
    func webViewWebContentProcessDidTerminate(_ webView: WKWebView) {
        generation += 1; ready = false; pendingToken = nil; buffer.reset(); webView.reload()
    }
    func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction, decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
        decisionHandler(navigationAction.request.url?.isFileURL == true ? .allow : .cancel)
    }
    func userContentController(_ userContentController: WKUserContentController, didReceive message: WKScriptMessage) {
        guard message.frameInfo.isMainFrame else { return }
        if let value = message.body as? [String: String] {
            if value["event"] == "ready" { ready = true; render() }
            else if let token = value["token"], token == pendingToken {
                if value["event"] == "rendered" {
                    pendingToken = nil; buffer.complete(success:true); retries = 0
                    recordValidation(screen:value["screen"] ?? "", session:pendingSession)
                    render()
                } else if value["event"] == "render-failed" {
                    renderFailed(value["error"] ?? "Unknown terminal error")
                }
            }
        } else if buffer.desired.enabled, let input = message.body as? String { onInput?(input) }
    }
    func update(text:String, id:String, observer:Bool, enabled:Bool) {
        buffer.desired = TerminalSnapshot(text:text, id:id, observer:observer, enabled:enabled)
        render()
    }
    private func render() {
        guard ready, let webView, let request = buffer.next() else { return }
        let revision = generation
        let token = UUID().uuidString
        pendingToken = token; pendingSession = request.snapshot.id
        let values: [String:Any] = ["text":request.text, "reset":request.reset,
                                   "observer":request.snapshot.observer, "enabled":request.snapshot.enabled, "token":token]
        guard let data = try? JSONSerialization.data(withJSONObject:values),
              let json = String(data:data, encoding:.utf8) else {
            pendingToken = nil; buffer.complete(success:false); return
        }
        // WKScriptMessage acknowledges the xterm write callback. Avoid the Swift
        // callAsyncJavaScript overlay, which adds a missing dylib on iOS 18.5.
        webView.evaluateJavaScript("window.applyTerminal(\(json));") { [weak self] _, error in
            guard let self, self.generation == revision, self.pendingToken == token else { return }
            if let error { self.renderFailed(error.localizedDescription) }
        }
    }
    private func renderFailed(_ message:String) {
        pendingToken = nil; buffer.complete(success:false)
        NSLog("Code Link terminal render failed: %@", message)
        // Retain the unacknowledged output and replay it after reloading.
        if retries < 2 { retries += 1; webView?.reload() }
    }
    private func recordValidation(screen:String, session:String) {
        #if targetEnvironment(simulator)
        guard ProcessInfo.processInfo.arguments.contains("--validate-terminal"),
              let directory = FileManager.default.urls(for:.documentDirectory, in:.userDomainMask).first,
              let data = try? JSONSerialization.data(withJSONObject:["sessionId":session, "screenText":screen]) else { return }
        try? data.write(to:directory.appendingPathComponent("codelink-render.json"), options:.atomic)
        #endif
    }
}
