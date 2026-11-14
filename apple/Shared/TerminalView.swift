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
    private var previousText = "", previousID = ""
    private var desired = (text:"", id:"", observer:false, enabled:false)
    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) { ready = true; render() }
    func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction, decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
        decisionHandler(navigationAction.request.url?.isFileURL == true ? .allow : .cancel)
    }
    func userContentController(_ userContentController: WKUserContentController, didReceive message: WKScriptMessage) {
        if desired.enabled, message.frameInfo.isMainFrame, let input = message.body as? String { onInput?(input) }
    }
    func update(text:String, id:String, observer:Bool, enabled:Bool) { desired = (text, id, observer, enabled); render() }
    private func render() {
        guard ready, let webView else { return }
        let reset = previousID != desired.id || !desired.text.hasPrefix(previousText)
        let delta = reset ? desired.text : String(desired.text.dropFirst(previousText.count))
        let values: [String:Any] = ["text":desired.observer ? desired.text : delta, "reset":reset, "observer":desired.observer, "enabled":desired.enabled]
        guard let data = try? JSONSerialization.data(withJSONObject:values, options:[.fragmentsAllowed]), let json = String(data:data, encoding:.utf8) else { return }
        webView.evaluateJavaScript("window.renderTerminal(\(json));", completionHandler:nil)
        previousText = desired.text; previousID = desired.id
    }
}
