import Foundation

struct TerminalSnapshot: Equatable {
    var text = "", id = ""
    var observer = false, enabled = false
}

struct TerminalRenderBuffer {
    struct Request {
        let snapshot: TerminalSnapshot
        let text: String
        let reset: Bool
    }
    var desired = TerminalSnapshot()
    private var applied: TerminalSnapshot?
    private var pending: Request?

    mutating func next() -> Request? {
        guard pending == nil, applied != desired else { return nil }
        let reset = applied == nil || applied!.id != desired.id
            || applied!.observer != desired.observer || !desired.text.hasPrefix(applied!.text)
        let text = reset || desired.observer ? desired.text
            : String(desired.text.dropFirst(applied!.text.count))
        let request = Request(snapshot: desired, text: text, reset: reset)
        pending = request
        return request
    }
    mutating func complete(success: Bool) {
        if success { applied = pending?.snapshot }
        pending = nil
    }
    mutating func reset() { applied = nil; pending = nil }
}
