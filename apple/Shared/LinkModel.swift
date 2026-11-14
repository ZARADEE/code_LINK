import SwiftUI
import UserNotifications
#if os(iOS)
import UIKit
#endif

@MainActor final class LinkModel: ObservableObject {
    @Published var connected = false
    @Published var connectionStatus = "Not connected"
    @Published var error = ""
    @Published var sessions = [LinkSession]()
    @Published var commands = [LinkSession]()
    @Published var devices = [LinkDevice]()
    @Published var selected = ""
    @Published var output = ""
    @Published var lan = false
    @Published var pairing = [String: Any]()
    @Published var invitationText = ""
    @Published var hostAddress = ""
    @Published var notificationStatus = ""
    @Published var groups = [String: String]()
    @Published var aliases = [String: String]()
    @Published var reminders = ReminderState()
    @Published var pendingCount = 0
    let connection = LinkConnection()
    private var caches = [String: String]()
    private var pendingWatch: String?
    private var pending = [String: (Date, (Bool) -> Void)]()
    private var invite: PairingInvite?
    private var desiredConnection = false
    private var timer: Timer?
    private var lastAttempt = Date.distantPast
    private let defaults = UserDefaults.standard
    #if os(macOS)
    let host = MacHost()
    var isHost: Bool { true }
    #else
    var isHost: Bool { false }
    #endif
    var identity: String {
        if isHost { return "pc" }
        if let id = defaults.string(forKey: "device-id") { return id }
        let id = UUID().uuidString; defaults.set(id, forKey: "device-id"); return id
    }
    var current: LinkSession? { sessions.first { $0.id == selected } }
    var canInput: Bool { connected && (isHost || current?.controller == identity) && current != nil }
    var pairInvite: String {
        guard let port = pairing["port"] as? Int, let code = pairing["code"] as? String,
              let pin = pairing["fingerprint"] as? String, !hostAddress.isEmpty else { return "" }
        return PairingInvite(host: hostAddress, port: UInt16(clamping: port), pin: pin, code: code).text
    }
    init() {
        groups = defaults.dictionary(forKey: "groups") as? [String: String] ?? [:]
        aliases = defaults.dictionary(forKey: "aliases") as? [String: String] ?? [:]
        if let data = defaults.data(forKey: "reminders"), let saved = try? JSONDecoder().decode(ReminderState.self, from: data) { reminders = saved }
        invitationText = defaults.string(forKey: "invitation") ?? ""
        invite = try? PairingInvite(text: invitationText)
        connection.onMessage = { [weak self] message in DispatchQueue.main.async { self?.receive(message) } }
        connection.onStatus = { [weak self] status, online in DispatchQueue.main.async {
            guard let self else { return }
            self.connected = online; self.connectionStatus = status
            if !online { self.failPending() }
        } }
        timer = Timer.scheduledTimer(withTimeInterval: 2, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.tick() }
        }
        #if os(macOS)
        hostAddress = MacHost.lanAddress()
        do { try host.start(); desiredConnection = true; reconnect() } catch { self.error = error.localizedDescription }
        #else
        let args = ProcessInfo.processInfo.arguments
        if let i = args.firstIndex(of: "--invite"), args.count > i + 1 { invitationText = args[i + 1]; connect() }
        #endif
    }
    private func tick() {
        for (id, value) in pending where Date().timeIntervalSince(value.0) > 30 {
            pending.removeValue(forKey: id); value.1(false)
            error = "No confirmation received. Check the terminal before sending again; the command may already have run."
        }
        pendingCount = pending.count
        if desiredConnection && !connected && Date().timeIntervalSince(lastAttempt) > 30 { reconnect() }
    }
    private func failPending() {
        guard !pending.isEmpty else { return }
        let callbacks = pending.values.map { $0.1 }; pending.removeAll(); pendingCount = 0
        callbacks.forEach { $0(false) }
        error = "Connection lost before confirmation. Check the terminal before sending again."
    }
    func connect() {
        do {
            let parsed = try PairingInvite(text: invitationText)
            if let prior = invite, prior.pin != parsed.pin {
                caches.removeAll(); sessions = []; commands = []; selected = ""; output = ""
                reminders = ReminderState(); persist()
            }
            invite = parsed; defaults.set(parsed.text, forKey: "invitation")
            desiredConnection = true; error = ""; reconnect()
        } catch { self.error = error.localizedDescription }
    }
    func reconnect() {
        desiredConnection = true; lastAttempt = Date()
        #if os(macOS)
        connection.connectLocal(path: host.socketPath)
        #else
        guard let invite else { return }
        connection.connect(invite: invite, deviceID: identity, name: UIDevice.current.name)
        #endif
    }
    func disconnect() { desiredConnection = false; connection.disconnect() }
    func sceneActive(_ active: Bool) {
        #if os(iOS)
        if active { if invite != nil { reconnect() } }
        else { desiredConnection = false; connection.disconnect() }
        #endif
    }
    func action(_ action: String, _ values: [String: Any] = [:], completion: @escaping (Bool) -> Void = { _ in }) {
        guard connected else { error = "Connect to your computer first."; completion(false); return }
        let id = UUID().uuidString
        var value = values; value["type"] = "action"; value["id"] = id; value["action"] = action
        pending[id] = (Date(), completion); pendingCount = pending.count; connection.send(value)
    }
    func select(_ id: String) {
        selected = id; pendingWatch = id; output = caches[id] ?? ""
        reminders.acknowledge(id); persist()
        connection.send(["type":"watch", "sessionId":id])
    }
    func open(_ item: LinkSession) {
        action("openCommand", ["key":item.catalogKey ?? item.id])
    }
    func send(_ text: String, raw: Bool = false, completion: @escaping (Bool) -> Void = { _ in }) {
        guard canInput else { error = "This session is read-only. Open it from the command list or grant control on the computer."; completion(false); return }
        action("input", ["sessionId":selected, "text":text, "raw":raw], completion: completion)
    }
    func group(_ id: String) -> String { groups[id] ?? "B" }
    func name(_ session: LinkSession) -> String { let value = aliases[session.id] ?? ""; return value.isEmpty ? session.name : value }
    func setGroup(_ id: String, _ group: String) { groups[id] = group; persist() }
    func rename(_ id: String, _ name: String) { aliases[id] = name; persist() }
    func acknowledgeBatch() { reminders.acknowledgeBatch(); persist() }
    private func persist() {
        defaults.set(groups, forKey: "groups"); defaults.set(aliases, forKey: "aliases")
        defaults.set(try? JSONEncoder().encode(reminders), forKey: "reminders")
    }
    private func receive(_ message: [String: Any]) {
        guard let type = message["type"] as? String else { return }
        if type == "result", let id = message["id"] as? String, let result = message["result"] as? [String: Any] {
            let ok = result["ok"] as? Bool ?? false
            if !ok { error = result["error"] as? String ?? "The operation failed." }
            if ok, let session = result["sessionId"] as? String { select(session) }
            pending.removeValue(forKey: id)?.1(ok); pendingCount = pending.count
        }
        guard type == "state" else { return }
        sessions = (message["sessions"] as? [[String: Any]] ?? []).map(LinkSession.init)
        commands = (message["commands"] as? [[String: Any]] ?? []).map(LinkSession.init)
        devices = (message["devices"] as? [[String: Any]] ?? []).map(LinkDevice.init)
        lan = message["lan"] as? Bool ?? false
        pairing = message["pairing"] as? [String: Any] ?? [:]
        let remoteSelection = message["selected"] as? String ?? ""
        let delta = message["output"] as? String ?? ""
        var text = (message["reset"] as? Bool ?? false) ? delta : (caches[remoteSelection] ?? "") + delta
        if text.count > 120000 { text = String(text.suffix(100000)) }
        caches[remoteSelection] = text
        caches = caches.filter { entry in sessions.contains { $0.id == entry.key } }
        if pendingWatch == remoteSelection { pendingWatch = nil }
        if let pendingWatch, !sessions.contains(where: { $0.id == pendingWatch }) { self.pendingWatch = nil }
        if pendingWatch == nil { selected = remoteSelection; output = text }
        let notices = reminders.update(sessions.map { s in
            ReminderItem(id:s.id, group:group(s.id), status:s.status, turn:s.turn,
                         requests:s.approvals.compactMap { $0["id"] as? String }.sorted().joined(separator:","))
        }, online:connected)
        persist()
        for notice in notices { notify(notice) }
    }
    func requestNotifications() {
        UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound, .badge]) { granted, error in
            DispatchQueue.main.async { self.notificationStatus = error?.localizedDescription ?? (granted ? "Notifications enabled" : "Enable notifications in system settings.") }
        }
    }
    private func notify(_ notice: ReminderNotice) {
        let content = UNMutableNotificationContent()
        content.title = notice.kind == "batch" ? "Code Link · Group B complete" : "Code Link · " + notice.kind
        content.body = notice.kind == "batch" ? "All tasks in this batch have completed." : sessions.first(where: { $0.id == notice.id }).map(name) ?? "Session update"
        content.sound = .default
        UNUserNotificationCenter.current().add(UNNotificationRequest(identifier:notice.key, content:content, trigger:nil))
    }
}
