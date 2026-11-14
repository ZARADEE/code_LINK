import Foundation

@main enum NetworkProbe {
    static func main() throws {
        let args = CommandLine.arguments
        guard args.count >= 4 else { throw LinkError.message("Expected invitation, device ID and session ID") }
        let invite = try PairingInvite(text:args[1])
        let reject = args.contains("--reject")
        let client = LinkConnection()
        var opened = false
        let openID = UUID().uuidString, inputID = UUID().uuidString
        client.onStatus = { status, connected in
            if connected && reject { fputs("FAIL: mismatched certificate accepted\n", stderr); exit(1) }
            if reject && !status.hasPrefix("Connecting") {
                print("PASS: Swift client rejected mismatched certificate"); exit(0)
            }
        }
        client.onMessage = { message in
            if message["type"] as? String == "welcome" {
                client.send(["type":"action", "id":openID, "action":"openCommand", "key":args[3]])
            }
            if message["type"] as? String == "result", let result = message["result"] as? [String:Any] {
                guard result["ok"] as? Bool == true else { fputs("FAIL: Swift client action rejected\n", stderr); exit(1) }
                if message["id"] as? String == openID {
                    opened = true
                    client.send(["type":"action", "id":inputID, "action":"input", "sessionId":args[3], "text":"printf 'APPLE_%s\\n' 'CLIENT_OK'"])
                }
            }
            if opened, message["type"] as? String == "state", (message["output"] as? String ?? "").contains("APPLE_CLIENT_OK") {
                print("PASS: Swift client pairs, pins TLS, stores key, opens a session and sends input"); exit(0)
            }
        }
        client.connect(invite:invite, deviceID:args[2], name:"Apple protocol check")
        DispatchQueue.global().asyncAfter(deadline:.now() + 25) { fputs("FAIL: Swift client timed out\n", stderr); exit(1) }
        dispatchMain()
    }
}
