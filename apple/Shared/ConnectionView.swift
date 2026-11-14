import SwiftUI
import CoreImage.CIFilterBuiltins

struct ConnectionView: View {
    @EnvironmentObject private var model: LinkModel
    @Environment(\.dismiss) private var dismiss
    @State private var scanner = false
    @State private var confirmRevoke: LinkDevice?
    @State private var codexPath = ""
    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment:.leading, spacing:20) {
                    #if os(macOS)
                    Text("Connect your phone or tablet").font(.title2.bold())
                    Text("Use the same local network on both devices. Keep Code Link open on this Mac while working from your phone.").foregroundStyle(.secondary)
                    Toggle("Allow connections on the local network", isOn:Binding(get:{ model.lan }, set:{ model.action("settings", ["lan":$0]) })).disabled(!model.connected)
                    TextField("Mac IP address", text:$model.hostAddress).textFieldStyle(.roundedBorder)
                    if !model.pairInvite.isEmpty {
                        HStack(alignment:.top, spacing:20) {
                            QRImage(value:model.pairInvite).frame(width:180, height:180).background(.white)
                            VStack(alignment:.leading, spacing:12) {
                                Text(model.pairing["code"] as? String ?? "").font(.system(size:30, weight:.bold, design:.monospaced))
                                Text("Pairing codes expire after 10 minutes.").font(.caption).foregroundStyle(.secondary)
                                Button("Copy invitation") { NSPasteboard.general.clearContents(); NSPasteboard.general.setString(model.pairInvite, forType:.string) }
                                Button("New pairing code") { model.action("rotatePairing") }
                            }
                        }
                        Text("Certificate SHA-256").font(.caption.bold())
                        Text(model.pairing["fingerprint"] as? String ?? "").font(.system(.caption2, design:.monospaced)).textSelection(.enabled)
                    }
                    if !model.lan { Text("Enable local network connections before pairing a phone.").font(.callout).foregroundStyle(.orange) }
                    Divider()
                    Text("Paired devices").font(.headline)
                    if model.devices.isEmpty { Text("Scan this QR code in the iOS or Android app to pair.").foregroundStyle(.secondary) }
                    ForEach(model.devices) { device in
                        HStack {
                            Image(systemName:device.online ? "iphone.radiowaves.left.and.right" : "iphone")
                            VStack(alignment:.leading) { Text(device.name); Text(device.online ? "Connected" : "Offline").font(.caption).foregroundStyle(.secondary) }
                            Spacer(); Button("Revoke", role:.destructive) { confirmRevoke = device }
                        }
                    }
                    Text("Select a session, then use its menu to share it with a device. A paired device can also open sessions from its command list.").font(.caption).foregroundStyle(.secondary)
                    Divider()
                    Text("Codex executable (optional)").font(.headline)
                    TextField("/opt/homebrew/bin/codex", text:$codexPath).textFieldStyle(.roundedBorder)
                    HStack {
                        Button("Save path") { model.action("settings", ["lan":model.lan, "codexPath":codexPath.trimmingCharacters(in:.whitespacesAndNewlines)]) }
                        Spacer()
                        Button("Open host logs") { NSWorkspace.shared.open(model.host.dataDirectory) }
                    }
                    Text("Leave the path empty to detect Homebrew or PATH installations. Keep any required Node runtime available on PATH.").font(.caption).foregroundStyle(.secondary)
                    #else
                    Text("Connect to your computer").font(.title2.bold())
                    Text("Enable LAN in Code Link on your Mac or Windows PC. Scan its pairing QR code or paste the invitation here.").foregroundStyle(.secondary)
                    TextEditor(text:$model.invitationText).font(.system(.caption, design:.monospaced)).frame(height:110).padding(6).overlay(RoundedRectangle(cornerRadius:8).stroke(.secondary.opacity(0.4)))
                        .autocorrectionDisabled().textInputAutocapitalization(.never).accessibilityLabel("Pairing invitation")
                    HStack {
                        Button { scanner = true } label: { Label("Scan QR", systemImage:"qrcode.viewfinder") }
                        Spacer()
                        Button("Connect") { model.connect() }.buttonStyle(.borderedProminent).disabled(model.invitationText.trimmingCharacters(in:.whitespacesAndNewlines).isEmpty)
                    }
                    if let invite = try? PairingInvite(text:model.invitationText) {
                        Text("\(invite.host):\(invite.port)").font(.callout.monospaced())
                        Text("Certificate SHA-256").font(.caption.bold())
                        Text(invite.pin).font(.system(.caption2, design:.monospaced)).textSelection(.enabled)
                    }
                    Text(model.connectionStatus).font(.callout)
                    if model.connected { Button("Disconnect") { model.disconnect() } }
                    Divider()
                    Label("Keep the app open for live updates", systemImage:"info.circle").font(.headline)
                    Text("iOS suspends the connection in the background. Live output and A/B notifications resume when you return. Tasks continue running on your computer; missed intermediate events cannot be recovered.").font(.callout).foregroundStyle(.secondary)
                    #endif
                    Divider()
                    Button("Enable completion notifications") { model.requestNotifications() }
                    if !model.notificationStatus.isEmpty { Text(model.notificationStatus).font(.caption) }
                    if !model.error.isEmpty { Text(model.error).foregroundStyle(.red).textSelection(.enabled) }
                    Text("Code Link · local first · TLS with certificate pinning").font(.caption).foregroundStyle(.secondary)
                }.padding(24)
            }.navigationTitle(model.isHost ? "Pair devices" : "Connection")
                .toolbar { Button("Done") { dismiss() } }
                #if os(macOS)
                .frame(width:600, height:660)
                #endif
        }
        #if os(iOS)
        .sheet(isPresented:$scanner) { QRScanner { value in model.invitationText = value; scanner = false; model.connect() } }
        #endif
        .confirmationDialog("Revoke \(confirmRevoke?.name ?? "device")?", isPresented:Binding(get:{ confirmRevoke != nil }, set:{ if !$0 { confirmRevoke = nil } }), titleVisibility:.visible) {
            if let device = confirmRevoke { Button("Revoke access", role:.destructive) { model.action("revoke", ["deviceId":device.id]); confirmRevoke = nil } }
        } message: { Text("This disconnects the device and removes its access to shared sessions.") }
    }
}

struct QRImage: View {
    let value: String
    var body: some View {
        if let image = makeImage() { Image(decorative:image, scale:1).interpolation(.none).resizable().scaledToFit().padding(10).background(.white).accessibilityLabel("Pairing QR code") }
    }
    private func makeImage() -> CGImage? {
        let filter = CIFilter.qrCodeGenerator(); filter.message = Data(value.utf8)
        guard let output = filter.outputImage?.transformed(by:CGAffineTransform(scaleX:4, y:4)) else { return nil }
        return CIContext().createCGImage(output, from:output.extent)
    }
}
