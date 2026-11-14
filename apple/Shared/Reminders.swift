import Foundation

struct ReminderItem {
    var id: String
    var group: String
    var status: String
    var turn: String
    var requests: String = ""
}
struct ReminderNotice: Codable, Equatable {
    var id: String
    var kind: String
    var key: String
}
struct ReminderState: Codable {
    private struct Seen: Codable {
        var status = "", turn = "", request = "", completedToken = ""
        var sequence = 0
    }
    private var seen = [String: Seen]()
    private(set) var unread = [String: ReminderNotice]()
    private var batch = Set<String>()
    private(set) var batchUnread = Set<String>()
    private var batchNumber = 0
    private(set) var batchDone = 0, batchTotal = 0
    static func active(_ status: String) -> Bool { ["running", "approval", "input"].contains(status) }
    mutating func update(_ items: [ReminderItem], online: Bool) -> [ReminderNotice] {
        guard online else { return [] }
        var events = [ReminderNotice](), finishedNow = false
        let current = Dictionary(items.map { ($0.id, $0) }, uniquingKeysWith: { _, new in new })
        for item in items {
            let first = seen[item.id] == nil
            var prior = seen[item.id] ?? Seen()
            let active = Self.active(item.status)
            let started = active && ((!Self.active(prior.status) && !["unknown", "disconnected"].contains(prior.status)) || (!item.turn.isEmpty && item.turn != prior.turn))
            if started || (first && active) { prior.sequence += 1 }
            if item.group == "B" && active { batch.insert(item.id); batchUnread.removeAll() }
            if item.group == "B" && unread[item.id]?.kind == "completed" { unread.removeValue(forKey: item.id) }
            let request = item.requests.isEmpty ? (["approval", "input"].contains(item.status) ? item.status + ":" + item.turn : "") : item.requests
            if !request.isEmpty && request != prior.request {
                prior.sequence += 1
                let note = ReminderNotice(id: item.id, kind: item.status == "input" ? "input" : "approval", key: "\(item.id):\(request):\(prior.sequence)")
                unread[item.id] = note; events.append(note)
            }
            let token = item.turn.isEmpty ? "run:\(prior.sequence)" : item.turn
            let complete = item.status == "completed"
            let completion = complete && !first && token != prior.completedToken
            if complete { prior.completedToken = token }
            if completion {
                finishedNow = true
                if item.group == "A" {
                    let note = ReminderNotice(id: item.id, kind: "completed", key: "\(item.id):done:\(token):\(prior.sequence)")
                    unread[item.id] = note; events.append(note)
                } else { batch.insert(item.id) }
            }
            if ["failed", "interrupted"].contains(item.status) && item.status != prior.status {
                let note = ReminderNotice(id: item.id, kind: item.status, key: "\(item.id):\(item.status):\(token):\(prior.sequence)")
                unread[item.id] = note; events.append(note)
            }
            prior.status = item.status; prior.turn = item.turn; prior.request = request; seen[item.id] = prior
        }
        batch = batch.filter { current[$0]?.group == "B" }
        batchUnread = batchUnread.filter { current[$0]?.group == "B" }
        unread = unread.filter { current[$0.key] != nil }; seen = seen.filter { current[$0.key] != nil }
        batchTotal = batch.count; batchDone = batch.filter { current[$0]?.status == "completed" }.count
        if finishedNow && batchTotal > 0 && batchDone == batchTotal {
            batchNumber += 1; batchUnread.formUnion(batch)
            events.append(ReminderNotice(id: "@B", kind: "batch", key: "batch:\(batchNumber)")); batch.removeAll()
        }
        return events
    }
    func notice(for id: String) -> ReminderNotice? {
        unread[id] ?? (batchUnread.contains(id) ? ReminderNotice(id: id, kind: "completed", key: "batch:\(batchNumber):\(id)") : nil)
    }
    mutating func acknowledge(_ id: String) { unread.removeValue(forKey: id); batchUnread.remove(id) }
    mutating func acknowledgeBatch() {
        for id in batchUnread where unread[id]?.kind == "completed" { unread.removeValue(forKey: id) }
        batchUnread.removeAll()
    }
}
