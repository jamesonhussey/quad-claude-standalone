import Foundation

// JSON messages exchanged between CLI shim and app over Unix socket.
// Newline-delimited JSON, one message per line.

struct IPCRequest: Codable {
    let command: String
    var quadIndex: Int?
    var color: String?
    var windowId: Int?
}

struct IPCResponse: Codable {
    let status: String  // "ok" or "error"
    var message: String?

    static func ok(_ message: String? = nil) -> IPCResponse {
        IPCResponse(status: "ok", message: message)
    }

    static func error(_ message: String) -> IPCResponse {
        IPCResponse(status: "error", message: message)
    }

    func toJSON() -> String {
        let encoder = JSONEncoder()
        guard let data = try? encoder.encode(self),
              let str = String(data: data, encoding: .utf8) else {
            return "{\"status\":\"error\",\"message\":\"Failed to encode response\"}"
        }
        return str
    }
}
