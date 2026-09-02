import Cocoa

class AppDelegate: NSObject, NSApplicationDelegate {

    private var socketServer: SocketServer?
    private var commandRouter: CommandRouter?
    private var focusTimer: Timer?
    private var quadsFocused = false
    private var focusedQuad: Int = -1

    // Active overlay controllers, keyed by quad index
    var glowControllers: [Int: GlowWindowController] = [:]
    var statusControllers: [Int: StatusWidgetController] = [:]
    var fileExplorerControllers: [Int: FileExplorerController] = [:]

    static let aboveNormal = NSWindow.Level(rawValue: NSWindow.Level.normal.rawValue + 1)
    static let overlayLevel = aboveNormal  // Public access for initial window creation

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)

        commandRouter = CommandRouter(appDelegate: self)

        socketServer = SocketServer(handler: { [weak self] message in
            guard let self = self, let router = self.commandRouter else {
                return "{\"status\":\"error\",\"message\":\"App not ready\"}"
            }
            return router.handle(message: message)
        })

        do {
            try socketServer?.start()
            print("QuadClaudeMac: Socket server started at \(SocketServer.socketPath)")
        } catch {
            print("QuadClaudeMac: Failed to start socket server: \(error)")
        }

        // Poll focus state to manage overlay z-order
        focusTimer = Timer.scheduledTimer(withTimeInterval: 0.3, repeats: true) { [weak self] _ in
            self?.updateOverlayVisibility()
        }
    }

    func applicationWillTerminate(_ notification: Notification) {
        socketServer?.stop()
        focusTimer?.invalidate()

        for (_, controller) in glowControllers { controller.close() }
        for (_, controller) in statusControllers { controller.close() }
        for (_, controller) in fileExplorerControllers { controller.close() }
    }

    // MARK: - Focus-based Z-Order

    private func updateOverlayVisibility() {
        let currentFocusedQuad = findFocusedQuad()
        let anyQuadFocused = currentFocusedQuad >= 0

        if anyQuadFocused != quadsFocused {
            quadsFocused = anyQuadFocused
            let level = anyQuadFocused ? Self.aboveNormal : NSWindow.Level.normal
            setAllOverlayLevels(level)
        }

        // When focused quad changes, bring its overlays to the very top
        if currentFocusedQuad != focusedQuad {
            focusedQuad = currentFocusedQuad
            if currentFocusedQuad >= 0 {
                bringQuadOverlaysToTop(currentFocusedQuad)
            }
        }
    }

    /// Find which quad terminal is frontmost, or -1 if none
    private func findFocusedQuad() -> Int {
        guard let frontApp = NSWorkspace.shared.frontmostApplication,
              frontApp.bundleIdentifier == "com.apple.Terminal" else {
            return -1
        }

        let options: CGWindowListOption = [.optionOnScreenOnly, .excludeDesktopElements]
        guard let windowList = CGWindowListCopyWindowInfo(options, kCGNullWindowID) as? [[String: Any]] else {
            return -1
        }

        let terminalPID = frontApp.processIdentifier

        for info in windowList {
            guard let ownerPID = info[kCGWindowOwnerPID as String] as? pid_t,
                  ownerPID == terminalPID,
                  let layer = info[kCGWindowLayer as String] as? Int, layer == 0,
                  let windowId = info[kCGWindowNumber as String] as? CGWindowID else {
                continue
            }

            for i in 0..<100 {
                guard let storedId = TerminalManager.storedWindowId(forQuad: i) else {
                    if i >= 4 { break }
                    continue
                }
                if storedId == windowId { return i }
            }
            break
        }
        return -1
    }

    /// Bring a specific quad's overlays to the very top (above other quads' overlays)
    private func bringQuadOverlaysToTop(_ quadIndex: Int) {
        glowControllers[quadIndex]?.showWindow()
        statusControllers[quadIndex]?.showWindow()
        fileExplorerControllers[quadIndex]?.showWindow()
    }

    private func setAllOverlayLevels(_ level: NSWindow.Level) {
        for (_, c) in glowControllers { c.setLevel(level) }
        for (_, c) in statusControllers { c.setLevel(level) }
        for (_, c) in fileExplorerControllers { c.setLevel(level) }
    }
}
