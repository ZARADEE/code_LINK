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
    func webView(_ webView: WKWebView, didStartProvisionalNavigation navigation: WKNavigation!) {
        generation += 1; ready = false; buffer.reset()
    }
    func webViewWebContentProcessDidTerminate(_ webView: WKWebView) {
        generation += 1; ready = false; buffer.reset(); webView.reload()
    }
    func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction, decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
        decisionHandler(navigationAction.request.url?.isFileURL == true ? .allow : .cancel)
    }
    func userContentController(_ userContentController: WKUserContentController, didReceive message: WKScriptMessage) {
        guard message.frameInfo.isMainFrame else { return }
        if let value = message.body as? [String: String], value["event"] == "ready" {
            ready = true; render()
        } else if buffer.desired.enabled, let input = message.body as? String { onInput?(input) }
    }
    func update(text:String, id:String, observer:Bool, enabled:Bool) {
        buffer.desired = TerminalSnapshot(text:text, id:id, observer:observer, enabled:enabled)
        render()
    }
    private func render() {
        guard ready, let webView, let request = buffer.next() else { return }
        let revision = generation
        let values: [String:Any] = ["text":request.text, "reset":request.reset,
                                   "observer":request.snapshot.observer, "enabled":request.snapshot.enabled]
        webView.callAsyncJavaScript("return await window.renderTerminal(value);", arguments:["value":values],
                                    in:nil, contentWorld:.page) { [weak self] result in
            guard let self, self.generation == revision else { return }
            switch result {
            case .success(let value):
                self.buffer.complete(success:true); self.retries = 0
                self.recordValidation(screen:value as? String ?? "", session:request.snapshot.id)
                self.render()
            case .failure(let error):
                self.buffer.complete(success:false)
                NSLog("Code Link terminal render failed: %@", error.localizedDescription)
                // Retain the unacknowledged output and replay it after reloading.
                if self.retries < 2 { self.retries += 1; webView.reload() }
            }
        }
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
