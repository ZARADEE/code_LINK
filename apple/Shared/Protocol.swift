import Foundation

enum LinkError: LocalizedError {
    case message(String)
    var errorDescription: String? { if case .message(let message) = self { return message }; return nil }
}

struct PairingInvite: Codable, Equatable {
    let host: String
    let port: UInt16
    let pin: String
    let code: String
    init(host: String, port: UInt16, pin: String, code: String) {
        self.host = host; self.port = port; self.pin = pin; self.code = code
    }
    init(text: String) throws {
        let value = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard value.hasPrefix("codelink:"), value.count < 8192 else { throw LinkError.message("Paste a Code Link pairing invitation from your computer.") }
        var encoded = String(value.dropFirst(9)).replacingOccurrences(of: "-", with: "+").replacingOccurrences(of: "_", with: "/")
        encoded += String(repeating: "=", count: (4 - encoded.count % 4) % 4)
        guard let data = Data(base64Encoded: encoded) else { throw LinkError.message("Invalid pairing invitation.") }
        self = try JSONDecoder().decode(Self.self, from: data)
        guard !host.isEmpty, !host.contains("/"), port > 0,
              pin.count == 64, pin.allSatisfy({ $0.isHexDigit }),
              code.count == 6, code.allSatisfy({ $0.isNumber }) else { throw LinkError.message("Invalid host, certificate or pairing code.") }
    }
    var text: String {
        guard let data = try? JSONEncoder().encode(self) else { return "" }
        return "codelink:" + data.base64EncodedString().replacingOccurrences(of: "+", with: "-").replacingOccurrences(of: "/", with: "_").replacingOccurrences(of: "=", with: "")
    }
}

struct FrameDecoder {
    static let maximum = 1_048_576
    private var buffer = Data()
    mutating func append(_ data: Data) throws -> [[String: Any]] {
        buffer.append(data)
        var messages = [[String: Any]]()
        while buffer.count >= 4 {
            let count = buffer.prefix(4).reduce(0) { ($0 << 8) | Int($1) }
            guard count >= 2, count <= Self.maximum else { throw LinkError.message("Invalid message length.") }
            guard buffer.count >= count + 4 else { break }
            let payload = Data(buffer.dropFirst(4).prefix(count))
            guard let object = try JSONSerialization.jsonObject(with: payload) as? [String: Any] else { throw LinkError.message("Invalid message.") }
            messages.append(object)
            buffer = Data(buffer.dropFirst(count + 4))
        }
        return messages
    }
    static func encode(_ value: [String: Any]) throws -> Data {
        let payload = try JSONSerialization.data(withJSONObject: value)
        guard payload.count <= maximum else { throw LinkError.message("Message is too large.") }
        let n = UInt32(payload.count)
        var result = Data([UInt8((n >> 24) & 255), UInt8((n >> 16) & 255), UInt8((n >> 8) & 255), UInt8(n & 255)])
        result.append(payload); return result
    }
}

struct LinkSession: Identifiable {
    var id: String
    var name: String
    var kind: String
    var status: String
    var cwd: String
    var controller: String
    var turn: String
    var catalogKey: String?
    var approvals: [[String: Any]]
    init(_ data: [String: Any]) {
        id = data["id"] as? String ?? data["sessionId"] as? String ?? data["key"] as? String ?? ""
        name = data["name"] as? String ?? "Terminal"
        kind = data["kind"] as? String ?? "terminal"
        status = data["status"] as? String ?? "unknown"
        cwd = data["cwd"] as? String ?? ""
        controller = data["controller"] as? String ?? ""
        let turnID = data["turnId"] as? String ?? ""
        turn = turnID.isEmpty ? String(describing: data["activityCycle"] ?? "") : turnID
        catalogKey = data["key"] as? String
        if id.isEmpty { id = catalogKey ?? "" }
        approvals = data["approvals"] as? [[String: Any]] ?? []
    }
}

struct LinkDevice: Identifiable {
    var id: String
    var name: String
    var online: Bool
    init(_ data: [String: Any]) { id = data["id"] as? String ?? ""; name = data["name"] as? String ?? "Device"; online = data["online"] as? Bool ?? false }
}
