import Foundation

class SocketServer {
    static let socketPath: String = {
        let dir = QuadConfig.appSupportDir.path
        return (dir as NSString).appendingPathComponent("quadclaude.sock")
    }()

    private var serverSocket: Int32 = -1
    private var isRunning = false
    private let handler: (String) -> String
    private var acceptThread: Thread?

    init(handler: @escaping (String) -> String) {
        self.handler = handler
    }

    func start() throws {
        // Clean up stale socket file
        let fm = FileManager.default
        if fm.fileExists(atPath: Self.socketPath) {
            try fm.removeItem(atPath: Self.socketPath)
        }

        // Ensure directory exists
        let dir = (Self.socketPath as NSString).deletingLastPathComponent
        if !fm.fileExists(atPath: dir) {
            try fm.createDirectory(atPath: dir, withIntermediateDirectories: true)
        }

        // Create Unix domain socket
        serverSocket = socket(AF_UNIX, SOCK_STREAM, 0)
        guard serverSocket >= 0 else {
            throw SocketError.createFailed(errno)
        }

        // Bind to socket path
        var addr = sockaddr_un()
        addr.sun_family = sa_family_t(AF_UNIX)
        let pathBytes = Self.socketPath.utf8CString
        guard pathBytes.count <= MemoryLayout.size(ofValue: addr.sun_path) else {
            throw SocketError.pathTooLong
        }
        withUnsafeMutablePointer(to: &addr.sun_path) { ptr in
            ptr.withMemoryRebound(to: CChar.self, capacity: pathBytes.count) { dest in
                pathBytes.withUnsafeBufferPointer { src in
                    _ = memcpy(dest, src.baseAddress!, pathBytes.count)
                }
            }
        }

        let bindResult = withUnsafePointer(to: &addr) { ptr in
            ptr.withMemoryRebound(to: sockaddr.self, capacity: 1) { sockPtr in
                bind(serverSocket, sockPtr, socklen_t(MemoryLayout<sockaddr_un>.size))
            }
        }
        guard bindResult == 0 else {
            close(serverSocket)
            throw SocketError.bindFailed(errno)
        }

        // Listen
        guard listen(serverSocket, 5) == 0 else {
            close(serverSocket)
            throw SocketError.listenFailed(errno)
        }

        isRunning = true

        // Accept connections on a background thread
        acceptThread = Thread { [weak self] in
            self?.acceptLoop()
        }
        acceptThread?.name = "QuadClaudeMac-SocketServer"
        acceptThread?.start()
    }

    func stop() {
        isRunning = false
        if serverSocket >= 0 {
            close(serverSocket)
            serverSocket = -1
        }
        try? FileManager.default.removeItem(atPath: Self.socketPath)
    }

    private func acceptLoop() {
        while isRunning {
            var clientAddr = sockaddr_un()
            var clientLen = socklen_t(MemoryLayout<sockaddr_un>.size)

            let clientSocket = withUnsafeMutablePointer(to: &clientAddr) { ptr in
                ptr.withMemoryRebound(to: sockaddr.self, capacity: 1) { sockPtr in
                    accept(serverSocket, sockPtr, &clientLen)
                }
            }

            guard clientSocket >= 0 else {
                if isRunning { continue }
                break
            }

            // Handle each client on a dispatch queue
            DispatchQueue.global(qos: .userInitiated).async { [weak self] in
                self?.handleClient(clientSocket)
            }
        }
    }

    private func handleClient(_ clientSocket: Int32) {
        defer { close(clientSocket) }

        // Read message (up to 8KB)
        var buffer = [UInt8](repeating: 0, count: 8192)
        let bytesRead = read(clientSocket, &buffer, buffer.count)
        guard bytesRead > 0 else { return }

        let message = String(bytes: buffer[0..<bytesRead], encoding: .utf8) ?? ""
        let trimmed = message.trimmingCharacters(in: .whitespacesAndNewlines)

        // Process on main thread (AppKit requires it)
        var response = ""
        let semaphore = DispatchSemaphore(value: 0)
        DispatchQueue.main.async { [weak self] in
            response = self?.handler(trimmed) ?? IPCResponse.error("Server gone").toJSON()
            semaphore.signal()
        }
        semaphore.wait()

        // Send response
        let responseData = (response + "\n").data(using: .utf8) ?? Data()
        responseData.withUnsafeBytes { ptr in
            if let base = ptr.baseAddress {
                _ = write(clientSocket, base, responseData.count)
            }
        }
    }

    enum SocketError: Error, LocalizedError {
        case createFailed(Int32)
        case bindFailed(Int32)
        case listenFailed(Int32)
        case pathTooLong

        var errorDescription: String? {
            switch self {
            case .createFailed(let e): return "Failed to create socket: \(String(cString: strerror(e)))"
            case .bindFailed(let e): return "Failed to bind socket: \(String(cString: strerror(e)))"
            case .listenFailed(let e): return "Failed to listen on socket: \(String(cString: strerror(e)))"
            case .pathTooLong: return "Socket path exceeds maximum length"
            }
        }
    }
}
