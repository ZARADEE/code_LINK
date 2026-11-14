import Foundation
import Network
import Security
import CryptoKit

final class LinkConnection {
    private let queue = DispatchQueue(label: "com.codelink.connection")
    private var connection: NWConnection?
    private var generation = UUID()
    private var decoder = FrameDecoder()
    private var timer: DispatchSourceTimer?
    private var lastMessage = Date()
    var onMessage: (([String: Any]) -> Void)?
    var onStatus: ((String, Bool) -> Void)?

    func connect(invite: PairingInvite, deviceID: String, name: String) {
        queue.async {
            self.reset()
            let epoch = self.generation
            do {
                let key = try DeviceKeychain.load("device:" + invite.pin.lowercased())
                let tls = NWProtocolTLS.Options()
                sec_protocol_options_set_min_tls_protocol_version(tls.securityProtocolOptions, .TLSv12)
                sec_protocol_options_set_verify_block(tls.securityProtocolOptions, { _, trust, done in
                    let trustRef = sec_trust_copy_ref(trust).takeRetainedValue()
                    guard let chain = SecTrustCopyCertificateChain(trustRef) as? [SecCertificate], let leaf = chain.first else { done(false); return }
                    let digest = SHA256.hash(data: SecCertificateCopyData(leaf) as Data).map { String(format: "%02x", $0) }.joined()
                    done(digest == invite.pin.lowercased())
                }, self.queue)
                let parameters = NWParameters(tls: tls, tcp: NWProtocolTCP.Options())
                let c = NWConnection(host: NWEndpoint.Host(invite.host), port: NWEndpoint.Port(rawValue: invite.port)!, using: parameters)
                self.start(c, epoch: epoch, hello: ["type":"hello", "deviceId":deviceID, "name":name, "key":key, "code":invite.code], pin: invite.pin.lowercased())
            } catch { self.onStatus?(error.localizedDescription, false) }
        }
    }
    #if os(macOS)
    func connectLocal(path: String) {
        queue.async {
            self.reset(); let epoch = self.generation
            let c = NWConnection(to: .unix(path: path), using: .tcp)
            self.start(c, epoch: epoch, hello: ["type":"hello"], pin: nil)
        }
    }
    #endif
    private func reset() {
        generation = UUID(); timer?.cancel(); timer = nil; connection?.stateUpdateHandler = nil; connection?.cancel(); connection = nil; decoder = FrameDecoder()
    }
    func disconnect() { queue.async { self.reset(); self.onStatus?("Disconnected", false) } }
    private func start(_ c: NWConnection, epoch: UUID, hello: [String: Any], pin: String?) {
        connection = c; onStatus?("Connecting…", false); lastMessage = Date()
        let watchdog = DispatchSource.makeTimerSource(queue: queue)
        watchdog.schedule(deadline: .now() + 5, repeating: 5)
        watchdog.setEventHandler { [weak self, weak c] in
            guard let self, let c, self.generation == epoch else { return }
            if Date().timeIntervalSince(self.lastMessage) > 25 { self.fail(c, epoch: epoch, message: "Connection timed out. Reconnect to your computer.") }
        }
        timer = watchdog; watchdog.resume()
        c.stateUpdateHandler = { [weak self, weak c] state in
            guard let self, let c, self.generation == epoch else { return }
            switch state {
            case .ready: self.sendNow(hello, through: c, epoch: epoch); self.receive(c, epoch: epoch, pin: pin)
            case .failed(let error): self.fail(c, epoch: epoch, message: error.localizedDescription)
            case .waiting(let error): self.onStatus?(error.localizedDescription, false)
            default: break
            }
        }
        c.start(queue: queue)
    }
    private func fail(_ c: NWConnection, epoch: UUID, message: String) {
        guard generation == epoch else { return }
        reset(); onStatus?(message, false)
    }
    private func receive(_ c: NWConnection, epoch: UUID, pin: String?) {
        c.receive(minimumIncompleteLength: 1, maximumLength: 65536) { [weak self, weak c] data, _, complete, error in
            guard let self, let c, self.generation == epoch else { return }
            do {
                if let data, !data.isEmpty {
                    for message in try self.decoder.append(data) {
                        self.lastMessage = Date()
                        if message["type"] as? String == "welcome" {
                            guard message["protocol"] as? Int == 1 else { throw LinkError.message("Unsupported host protocol.") }
                            if let pin, let key = message["key"] as? String, !key.isEmpty { try DeviceKeychain.save(key, account: "device:" + pin) }
                            self.onStatus?("Connected · " + (message["host"] as? String ?? "computer"), true)
                        }
                        if message["type"] as? String == "error" { throw LinkError.message(message["error"] as? String ?? "Host rejected the request.") }
                        self.onMessage?(message)
                    }
                }
                if let error { throw error }
                if complete { throw LinkError.message("Computer disconnected. Your tasks continue on the computer.") }
                self.receive(c, epoch: epoch, pin: pin)
            } catch { self.fail(c, epoch: epoch, message: error.localizedDescription) }
        }
    }
    func send(_ value: [String: Any]) {
        queue.async {
            guard let c = self.connection else { self.onStatus?("Connect to your computer first.", false); return }
            self.sendNow(value, through: c, epoch: self.generation)
        }
    }
    private func sendNow(_ value: [String: Any], through c: NWConnection, epoch: UUID) {
        do {
            c.send(content: try FrameDecoder.encode(value), completion: .contentProcessed { [weak self, weak c] error in
                if let error, let self, let c { self.fail(c, epoch: epoch, message: error.localizedDescription) }
            })
        } catch { fail(c, epoch: epoch, message: error.localizedDescription) }
    }
}
