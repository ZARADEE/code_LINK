import Foundation

var checks = 0
func check(_ condition: @autoclosure () -> Bool, _ message:String) {
    checks += 1; if !condition() { fputs("FAIL: \(message)\n", stderr); exit(1) }
}
func rejects(_ block: () throws -> Void, _ message:String) {
    do { try block(); check(false, message) } catch { check(true, message) }
}
let invite = PairingInvite(host:"192.168.1.10", port:47631, pin:String(repeating:"a", count:64), code:"123456")
let decodedInvite = try PairingInvite(text:invite.text)
check(decodedInvite == invite, "Pairing invite round trip")
rejects({ _ = try PairingInvite(text:"https://example.com") }, "Reject non-Code-Link invite")
rejects({ _ = try PairingInvite(text:PairingInvite(host:"host", port:0, pin:"abc", code:"123456").text) }, "Reject incomplete certificate pins and invalid ports")
let first = try FrameDecoder.encode(["type":"hello", "text":"中文 🐈"])
let second = try FrameDecoder.encode(["type":"state"])
for split in 0...first.count {
    var decoder = FrameDecoder()
    let a = try decoder.append(first.prefix(split))
    let b = try decoder.append(Data(first.dropFirst(split)) + second)
    check(a.count + b.count == 2, "Fragmented and coalesced frames at \(split)")
    check((a+b)[0]["text"] as? String == "中文 🐈", "UTF-8 survives framing")
}
rejects({ var d = FrameDecoder(); _ = try d.append(Data([0,16,0,1])) }, "Reject oversized frame")
rejects({ var d = FrameDecoder(); _ = try d.append(Data([0,0,0,0])) }, "Reject zero-length frame")
func item(_ id:String, _ group:String, _ status:String, _ turn:String="1", _ request:String="") -> ReminderItem { ReminderItem(id:id, group:group, status:status, turn:turn, requests:request) }
var a = ReminderState()
check(a.update([item("a","A","completed")], online:true).isEmpty, "Initial completed snapshot is not a new completion")
_ = a.update([item("a","A","running","2")], online:true)
check(a.update([item("a","A","completed","2")], online:true).count == 1, "A completes once")
check(a.update([item("a","A","completed","2")], online:true).isEmpty, "A deduplicates heartbeat")
check(a.notice(for:"a") != nil, "Unread persists")
a.acknowledge("a"); check(a.notice(for:"a") == nil, "Explicit read acknowledgement")
var b = ReminderState()
_ = b.update([item("b1","B","running"), item("b2","B","running")], online:true)
check(b.update([item("b1","B","completed"), item("b2","B","running")], online:true).isEmpty, "B waits for all tasks")
let batch = b.update([item("b1","B","completed"), item("b2","B","completed")], online:true)
check(batch.count == 1 && batch[0].kind == "batch", "B reports one batch completion")
check(b.batchUnread.count == 2, "B unread tracks all finished tasks")
b.acknowledgeBatch(); check(b.batchUnread.isEmpty, "B explicit batch read")
var failed = ReminderState()
_ = failed.update([item("b1","B","running"), item("b2","B","running")], online:true)
let failure = failed.update([item("b1","B","completed"), item("b2","B","failed")], online:true)
check(failure.count == 1 && failure[0].kind == "failed", "Failure does not become batch success")
check(failed.update([item("b1","B","completed")], online:true).isEmpty, "Removing failed task cannot synthesize success")
var approval = ReminderState()
check(approval.update([item("x","B","approval","1","req1")], online:true).first?.kind == "approval", "Approval is immediate in group B")
check(approval.update([item("x","B","approval","1","req1")], online:true).isEmpty, "Approval deduplicates")
check(approval.update([item("x","B","approval","1","req2")], online:true).count == 1, "New request is distinct")
check(approval.update([item("x","B","completed")], online:false).isEmpty, "Disconnected state does not infer completion")
let restored = try JSONDecoder().decode(ReminderState.self, from:JSONEncoder().encode(approval))
check(restored.notice(for:"x") == approval.notice(for:"x"), "Reminder persistence round trip")
var rendering = TerminalRenderBuffer()
rendering.desired = TerminalSnapshot(text:"first", id:"one", enabled:true)
check(rendering.next()?.text == "first", "Initial terminal output is rendered in full")
rendering.desired.text = "first second"
check(rendering.next() == nil, "Updates wait while a WebView render is pending")
rendering.complete(success:true)
check(rendering.next()?.text == " second", "A render acknowledgement does not consume a newer update")
rendering.complete(success:false)
check(rendering.next()?.text == " second", "Failed JavaScript preserves unrendered output")
rendering.complete(success:true)
check(rendering.next() == nil, "Acknowledged output is not duplicated")
rendering.reset()
check(rendering.next()?.text == "first second", "WebView process recovery replays the full terminal")
rendering.complete(success:true)
rendering.desired = TerminalSnapshot(text:"replacement", id:"two", enabled:true)
check(rendering.next()?.reset == true, "Changing sessions clears the prior terminal")
rendering.complete(success:true)
rendering.desired.text = "short"
check(rendering.next()?.reset == true, "Truncated output resets the terminal")
print("PASS: \(checks) framing, invitation, reminder and terminal render checks")
