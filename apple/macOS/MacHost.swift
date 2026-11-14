import Foundation
import AppKit
import Darwin

final class MacHost {
    private var process: Process?
    let dataDirectory: URL
    var socketPath: String { dataDirectory.appendingPathComponent("control.sock").path }
    init() {
        dataDirectory = ProcessInfo.processInfo.environment["CODELINK_DATA_DIR"].map { URL(fileURLWithPath:$0) }
            ?? FileManager.default.urls(for:.applicationSupportDirectory, in:.userDomainMask)[0].appendingPathComponent("Code Link", isDirectory:true)
    }
    func start() throws {
        if process?.isRunning == true { return }
        guard let resources = Bundle.main.resourceURL else { throw LinkError.message("App resources are missing.") }
        let binary = resources.appendingPathComponent("backend/CodeLink.Host")
        guard FileManager.default.isExecutableFile(atPath:binary.path) else { throw LinkError.message("The local host is missing. Download the complete macOS app or run scripts/build-apple.sh.") }
        try FileManager.default.createDirectory(at:dataDirectory, withIntermediateDirectories:true, attributes:[.posixPermissions:0o700])
        let log = dataDirectory.appendingPathComponent("host.log")
        if !FileManager.default.fileExists(atPath:log.path) { FileManager.default.createFile(atPath:log.path, contents:nil, attributes:[.posixPermissions:0o600]) }
        let handle = try FileHandle(forWritingTo:log); try handle.seekToEnd()
        let child = Process(); child.executableURL = binary; child.currentDirectoryURL = FileManager.default.homeDirectoryForCurrentUser
        var environment = ProcessInfo.processInfo.environment
        environment["CODELINK_DATA_DIR"] = dataDirectory.path
        environment["CODELINK_PARENT_PID"] = String(ProcessInfo.processInfo.processIdentifier)
        environment["PATH"] = "/opt/homebrew/bin:/usr/local/bin:" + (environment["PATH"] ?? "/usr/bin:/bin:/usr/sbin:/sbin")
        child.environment = environment; child.standardOutput = handle; child.standardError = handle
        try child.run(); process = child; try handle.close()
    }
    func stop() { if process?.isRunning == true { process?.terminate() }; process = nil }
    static func lanAddress() -> String {
        var addresses: UnsafeMutablePointer<ifaddrs>?
        guard getifaddrs(&addresses) == 0, let first = addresses else { return "" }
        defer { freeifaddrs(addresses) }
        var cursor: UnsafeMutablePointer<ifaddrs>? = first
        while let item = cursor {
            defer { cursor = item.pointee.ifa_next }
            guard let address = item.pointee.ifa_addr, Int32(address.pointee.sa_family) == AF_INET,
                  (item.pointee.ifa_flags & UInt32(IFF_LOOPBACK)) == 0,
                  (item.pointee.ifa_flags & UInt32(IFF_UP)) != 0 else { continue }
            var name = [CChar](repeating:0, count:Int(NI_MAXHOST))
            if getnameinfo(address, socklen_t(address.pointee.sa_len), &name, socklen_t(name.count), nil, 0, NI_NUMERICHOST) == 0 { return String(cString:name) }
        }
        return ""
    }
}

final class MacAppDelegate: NSObject, NSApplicationDelegate {
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }
}
