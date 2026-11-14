import SwiftUI

private let linkGreen = Color(red:0.14, green:0.38, blue:0.30)

struct RootView: View {
    @EnvironmentObject private var model: LinkModel
    @State private var settings = false
    @State private var create = false
    @State private var draft = ""
    @State private var sending = false
    @State private var showSessions = false
    @State private var renameID = ""
    @State private var renameText = ""
    @State private var renaming = false
    @Environment(\.horizontalSizeClass) private var sizeClass
    var body: some View {
        VStack(spacing:0) {
            HStack(spacing:12) {
                Image(systemName:"terminal.fill").font(.title2).foregroundStyle(linkGreen)
                VStack(alignment:.leading, spacing:3) {
                    Text("CODE LINK").font(.headline).tracking(2)
                    Label(model.connectionStatus, systemImage:model.connected ? "circle.fill" : "circle")
                        .font(.caption).foregroundStyle(model.connected ? linkGreen : .secondary).lineLimit(1)
                }
                Spacer()
                #if os(iOS)
                Button { showSessions = true } label: { Image(systemName:"list.bullet.rectangle") }.accessibilityLabel("Sessions")
                #else
                Button { create = true } label: { Label("New session", systemImage:"plus") }.disabled(!model.connected)
                #endif
                Button { settings = true } label: { Label(model.isHost ? "Pair devices" : "Connect", systemImage:"qrcode") }
            }.padding(16).background(Color.primary.opacity(0.035))
            Divider()
            if !model.error.isEmpty {
                HStack {
                    Image(systemName:"exclamationmark.triangle.fill")
                    Text(model.error).font(.callout).textSelection(.enabled)
                    Spacer(); Button { model.error = "" } label: { Image(systemName:"xmark") }.accessibilityLabel("Dismiss error")
                }.foregroundStyle(.red).padding(12).background(Color.red.opacity(0.08))
            }
            HStack(spacing:0) {
                #if os(macOS)
                sessionList.frame(width:270)
                Divider()
                #else
                if sizeClass == .regular { sessionList.frame(width:270); Divider() }
                #endif
                workspace
            }
        }
        .tint(linkGreen)
        .sheet(isPresented:$settings) { ConnectionView().environmentObject(model) }
        .sheet(isPresented:$create) { CreateSessionView().environmentObject(model) }
        .sheet(isPresented:$showSessions) {
            NavigationStack { sessionList.navigationTitle("Sessions").toolbar { Button("Done") { showSessions = false } } }
                .presentationDetents([.medium, .large])
        }
        .alert("Rename session", isPresented:$renaming) {
            TextField("Name on this device", text:$renameText)
            Button("Save") { model.rename(renameID, renameText.trimmingCharacters(in:.whitespacesAndNewlines)) }
            Button("Cancel", role:.cancel) {}
        }
    }
    private var sessionList: some View {
        ScrollView {
            VStack(alignment:.leading, spacing:14) {
                HStack { Text("YOUR SESSIONS").font(.caption.bold()).tracking(1); Spacer(); Text("\(model.sessions.count)").font(.caption.monospacedDigit()) }
                if model.sessions.isEmpty {
                    Text(model.isHost ? "Start a terminal or Codex session to share it with your phone." : "Pair with your computer, then open a command below.")
                        .font(.callout).foregroundStyle(.secondary).padding(.vertical, 12)
                }
                ForEach(["A", "B"], id:\.self) { group in
                    let items = model.sessions.filter { model.group($0.id) == group }
                    if !items.isEmpty {
                        HStack {
                            Text("\(group) · " + (group == "A" ? "Each completion" : "Batch completion")).font(.caption.bold())
                            Spacer()
                            if group == "B" && !model.reminders.batchUnread.isEmpty { Button("Read") { model.acknowledgeBatch() }.font(.caption) }
                        }.foregroundStyle(.secondary)
                        ForEach(items) { session in
                            Button { model.select(session.id); showSessions = false } label: {
                                HStack(alignment:.top, spacing:10) {
                                    Image(systemName:session.kind == "codex" ? "sparkles" : "terminal").padding(.top, 2)
                                    VStack(alignment:.leading, spacing:5) {
                                        Text(model.name(session)).font(.callout.bold()).lineLimit(2)
                                        Text(session.status).font(.caption).foregroundStyle(.secondary)
                                    }
                                    Spacer(minLength:0)
                                    if model.reminders.notice(for:session.id) != nil { Circle().fill(.orange).frame(width:8, height:8) }
                                }.padding(12).frame(maxWidth:.infinity, alignment:.leading)
                                    .background(session.id == model.selected ? linkGreen.opacity(0.12) : Color.primary.opacity(0.035))
                                    .clipShape(RoundedRectangle(cornerRadius:8))
                            }.buttonStyle(.plain)
                            .contextMenu {
                                Button("Rename") { renameID = session.id; renameText = model.name(session); renaming = true }
                                Button("Move to group " + (group == "A" ? "B" : "A")) { model.setGroup(session.id, group == "A" ? "B" : "A") }
                                Button("Mark read") { model.select(session.id) }
                                if model.isHost {
                                    Button("Take control") { model.action("takeControl", ["sessionId":session.id]) }
                                    Button("Remove session", role:.destructive) { model.action("delete", ["sessionId":session.id]) }
                                }
                            }
                        }
                    }
                }
                if !model.isHost && !model.commands.isEmpty {
                    Divider(); Text("ON YOUR COMPUTER").font(.caption.bold()).tracking(1)
                    ForEach(model.commands) { command in
                        Button { model.open(command); showSessions = false } label: {
                            HStack { VStack(alignment:.leading) { Text(command.name).lineLimit(2); Text(command.status).font(.caption).foregroundStyle(.secondary) }; Spacer(); Image(systemName:"arrow.up.right") }
                        }.buttonStyle(.plain).padding(.vertical, 6).disabled(!model.connected)
                    }
                }
                Spacer(minLength:20)
                Text("A alerts on each completed task. B alerts when all tasks in the current batch finish. Approvals and failures always appear separately.")
                    .font(.caption).foregroundStyle(.secondary)
            }.padding(16)
        }
    }
    private var workspace: some View {
        VStack(spacing:0) {
            if let session = model.current {
                HStack(spacing:10) {
                    VStack(alignment:.leading, spacing:4) {
                        Text(model.name(session)).font(.headline)
                        Text(session.cwd).font(.caption.monospaced()).lineLimit(1).foregroundStyle(.secondary)
                    }
                    Spacer()
                    Menu {
                        Button("Rename") { renameID = session.id; renameText = model.name(session); renaming = true }
                        Button("Group A · individual") { model.setGroup(session.id, "A") }
                        Button("Group B · batch") { model.setGroup(session.id, "B") }
                        if model.isHost {
                            Button("Take control") { model.action("takeControl", ["sessionId":session.id]) }
                            ForEach(model.devices) { device in
                                Button("Share with " + device.name) { model.action("map", ["sessionId":session.id, "deviceId":device.id, "enabled":true, "control":true]) }
                            }
                            Button("Remove session", role:.destructive) { model.action("delete", ["sessionId":session.id]) }
                        }
                    } label: { Label("Group " + model.group(session.id), systemImage:"slider.horizontal.3") }.font(.caption).fixedSize()
                }.padding(14)
                TerminalView(text:model.output, sessionID:session.id, observer:session.kind == "observer", enabled:model.canInput && session.kind == "terminal") { model.send($0, raw:true) }
                    .frame(maxWidth:.infinity, maxHeight:.infinity)
                if !session.approvals.isEmpty {
                    ScrollView { VStack { ForEach(session.approvals.indices, id:\.self) { index in ApprovalView(session:session.id, approval:session.approvals[index]) } }.padding(12) }
                        .frame(maxHeight:260).background(Color.orange.opacity(0.06))
                }
                VStack(spacing:10) {
                    ScrollView(.horizontal, showsIndicators:false) { HStack(spacing:12) {
                        Label(model.canInput ? "Input enabled" : "Read only", systemImage:model.canInput ? "keyboard" : "lock").font(.caption).foregroundStyle(.secondary)
                        if session.kind != "codex" {
                            ForEach([("Enter", "\r"), ("Tab", "\t"), ("Esc", "\u{1b}"), ("↑", "\u{1b}[A"), ("↓", "\u{1b}[B"), ("←", "\u{1b}[D"), ("→", "\u{1b}[C")], id:\.0) { item in Button(item.0) { model.send(item.1, raw:true) }.disabled(!model.canInput) }
                        }
                        Button("Interrupt", role:.destructive) { model.action("interrupt", ["sessionId":session.id]) }.disabled(!model.canInput)
                    }.font(.caption) }
                    HStack(alignment:.bottom) {
                        TextField(session.kind == "codex" ? "Send a prompt…" : "Send a command…", text:$draft, axis:.vertical)
                            .lineLimit(1...5).textFieldStyle(.roundedBorder).disabled(!model.canInput)
                        Button(sending ? "Sending…" : "Send") {
                            let text = draft; sending = true
                            model.send(text) { ok in sending = false; if ok && draft == text { draft = "" } }
                        }.buttonStyle(.borderedProminent).disabled(!model.canInput || draft.isEmpty || sending)
                    }
                }.padding(14)
            } else {
                VStack(spacing:18) {
                    Image(systemName:"terminal").font(.system(size:52, weight:.light)).foregroundStyle(linkGreen)
                    Text("Your terminal, within reach.").font(.title2.bold())
                    Text(model.isHost ? "Create a session here. Pair a phone or tablet to follow its output, send input and handle approvals." : "Keep your computer working. Follow its terminals and respond from your iPhone or iPad.")
                        .multilineTextAlignment(.center).foregroundStyle(.secondary).frame(maxWidth:380)
                    if model.isHost { Button("Create a session") { create = true }.buttonStyle(.borderedProminent).disabled(!model.connected) }
                    else { Button("Connect to computer") { settings = true }.buttonStyle(.borderedProminent) }
                }.padding(32).frame(maxWidth:.infinity, maxHeight:.infinity)
            }
        }
    }
}

struct CreateSessionView: View {
    @EnvironmentObject private var model: LinkModel
    @Environment(\.dismiss) private var dismiss
    @State private var name = "Terminal"
    @State private var kind = "terminal"
    @State private var cwd = NSHomeDirectory()
    @State private var command = "/bin/zsh -l"
    @State private var busy = false
    var body: some View {
        VStack(alignment:.leading, spacing:18) {
            Text("New session").font(.title2.bold())
            TextField("Name", text:$name).textFieldStyle(.roundedBorder)
            Picker("Type", selection:$kind) { Text("Terminal").tag("terminal"); Text("Codex").tag("codex") }.pickerStyle(.segmented)
            TextField("Working directory", text:$cwd).textFieldStyle(.roundedBorder)
            if kind == "terminal" { TextField("Command", text:$command).textFieldStyle(.roundedBorder) }
            else { Text("Install Codex CLI and sign in on this Mac first. Code Link uses your local installation.").font(.callout).foregroundStyle(.secondary) }
            HStack { Button("Cancel") { dismiss() }; Spacer(); Button(busy ? "Creating…" : "Create") {
                busy = true
                model.action("create", ["kind":kind, "name":name, "cwd":cwd, "command":command]) { ok in busy = false; if ok { dismiss() } }
            }.buttonStyle(.borderedProminent).disabled(busy || name.isEmpty || cwd.isEmpty) }
        }.padding(24).frame(idealWidth:480)
    }
}
