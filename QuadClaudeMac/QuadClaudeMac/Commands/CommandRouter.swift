import Cocoa

class CommandRouter {
    private weak var appDelegate: AppDelegate?

    init(appDelegate: AppDelegate) {
        self.appDelegate = appDelegate
    }

    func handle(message: String) -> String {
        guard let data = message.data(using: .utf8) else {
            return IPCResponse.error("Invalid message encoding").toJSON()
        }

        let request: IPCRequest
        do {
            request = try JSONDecoder().decode(IPCRequest.self, from: data)
        } catch {
            return IPCResponse.error("Invalid JSON: \(error.localizedDescription)").toJSON()
        }

        switch request.command {
        case "launch":
            return handleLaunch()
        case "glow":
            return handleGlow(request)
        case "kill-glow":
            return handleKillGlow(request)
        case "status":
            return handleStatus(request)
        case "quit":
            DispatchQueue.main.async { NSApp.terminate(nil) }
            return IPCResponse.ok("Shutting down").toJSON()
        default:
            return IPCResponse.error("Unknown command: \(request.command)").toJSON()
        }
    }

    // MARK: - Command Handlers

    private func handleLaunch() -> String {
        guard let delegate = appDelegate else {
            return IPCResponse.error("App not ready").toJSON()
        }
        LaunchCommand.execute(appDelegate: delegate)
        return IPCResponse.ok("Launch started").toJSON()
    }

    private func handleGlow(_ request: IPCRequest) -> String {
        guard let delegate = appDelegate else {
            return IPCResponse.error("App not ready").toJSON()
        }
        let quad = request.quadIndex ?? 0
        let color = request.color ?? "green"
        GlowCommand.execute(appDelegate: delegate, quadIndex: quad, color: color)
        return IPCResponse.ok().toJSON()
    }

    private func handleKillGlow(_ request: IPCRequest) -> String {
        guard let delegate = appDelegate else {
            return IPCResponse.error("App not ready").toJSON()
        }
        let quad = request.quadIndex ?? 0
        KillGlowCommand.execute(appDelegate: delegate, quadIndex: quad)
        return IPCResponse.ok().toJSON()
    }

    private func handleStatus(_ request: IPCRequest) -> String {
        guard let delegate = appDelegate else {
            return IPCResponse.error("App not ready").toJSON()
        }
        let quad = request.quadIndex ?? 0
        let windowId = request.windowId ?? 0
        StatusCommand.execute(appDelegate: delegate, quadIndex: quad, windowId: CGWindowID(windowId))
        return IPCResponse.ok().toJSON()
    }
}
