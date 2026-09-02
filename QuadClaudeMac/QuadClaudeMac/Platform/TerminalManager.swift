import Cocoa

/// Manages Terminal.app windows: launching, detecting, positioning, and tracking.
enum TerminalManager {

    /// Launch 4 Terminal.app windows and position them in a 2x2 grid.
    /// Returns the CGWindowIDs for each quad.
    static func launchQuadGrid(config: QuadConfig) -> [CGWindowID] {
        let quadFrames = ScreenManager.quadFrames()

        // Snapshot current Terminal windows
        let terminalPID = ProcessHelper.terminalAppPID()
        let existingWindows = terminalPID.map { getTerminalWindowIds(pid: $0) } ?? []

        // Build launch commands for each quad
        let launchScript = launchScriptPath(config: config)
        let commands = (0..<4).map { quadIndex -> String in
            "export QUAD_INDEX=\(quadIndex); source '\(launchScript)'"
        }

        // Launch all 4 terminal windows
        _ = AppleScriptBridge.openTerminalWindows(commands: commands)

        // Wait for windows to appear
        Thread.sleep(forTimeInterval: 1.5)

        // Position windows using AppleScript (no Accessibility permission needed).
        // AppleScript bounds are {x1, y1, x2, y2} in screen coords (top-left origin).
        let screenHeight = NSScreen.main?.frame.height ?? 1080
        let bounds = quadFrames.map { qf -> (x1: Int, y1: Int, x2: Int, y2: Int) in
            // Convert NSRect (bottom-left origin) to screen coords (top-left origin)
            let x1 = Int(qf.frame.origin.x)
            let y1 = Int(screenHeight - qf.frame.origin.y - qf.frame.height)
            let x2 = Int(qf.frame.origin.x + qf.frame.width)
            let y2 = Int(screenHeight - qf.frame.origin.y)
            return (x1, y1, x2, y2)
        }
        _ = AppleScriptBridge.positionTerminalWindows(bounds: bounds)

        // Small delay for positions to settle, then identify windows by their new positions
        Thread.sleep(forTimeInterval: 0.5)

        // Find new terminal window IDs
        var newWindowIds: [CGWindowID] = []
        if let pid = ProcessHelper.terminalAppPID() {
            let currentWindows = getTerminalWindowIds(pid: pid)
            let newWindows = currentWindows.filter { !existingWindows.contains($0) }

            // Match each new window to a quad by comparing its position to expected quad positions
            for qf in quadFrames {
                let expectedX = qf.frame.origin.x
                let expectedY = screenHeight - qf.frame.origin.y - qf.frame.height
                var bestMatch: CGWindowID?
                var bestDist: CGFloat = .infinity

                for windowId in newWindows {
                    if newWindowIds.contains(windowId) { continue }
                    guard let wBounds = ProcessHelper.windowBounds(windowId: windowId) else { continue }
                    let dist = abs(wBounds.origin.x - expectedX) + abs(wBounds.origin.y - expectedY)
                    if dist < bestDist {
                        bestDist = dist
                        bestMatch = windowId
                    }
                }

                if let match = bestMatch {
                    newWindowIds.append(match)
                    saveWindowId(match, forQuad: qf.index)
                } else if let fallback = newWindows.first(where: { !newWindowIds.contains($0) }) {
                    newWindowIds.append(fallback)
                    saveWindowId(fallback, forQuad: qf.index)
                }
            }
        }

        // Set font size based on quad height
        let fontSize = ScreenManager.fontSizeForQuad(height: quadFrames[0].frame.height)
        let profile = config.terminalProfile
        _ = AppleScriptBridge.setTerminalFontSize(profile: profile, size: fontSize)

        return newWindowIds
    }

    /// Get all Terminal.app CGWindowIDs
    static func getTerminalWindowIds(pid: pid_t) -> [CGWindowID] {
        let windows = ProcessHelper.windowList(for: pid)
        return windows.compactMap { info -> CGWindowID? in
            guard let layer = info[kCGWindowLayer as String] as? Int, layer == 0 else { return nil }
            return info[kCGWindowNumber as String] as? CGWindowID
        }
    }

    /// Get the stored CGWindowID for a quad
    static func storedWindowId(forQuad index: Int) -> CGWindowID? {
        let url = QuadConfig.quadWindowIdURL(index: index)
        guard let data = try? Data(contentsOf: url),
              let str = String(data: data, encoding: .utf8)?.trimmingCharacters(in: .whitespacesAndNewlines),
              let id = UInt32(str) else { return nil }
        return CGWindowID(id)
    }

    /// Save a CGWindowID for a quad
    static func saveWindowId(_ windowId: CGWindowID, forQuad index: Int) {
        let url = QuadConfig.quadWindowIdURL(index: index)
        try? "\(windowId)".data(using: .utf8)?.write(to: url, options: .atomic)
    }

    /// Get the bounds of a quad's terminal window (in CGWindow top-left coordinates)
    static func terminalBounds(forQuad index: Int) -> CGRect? {
        guard let windowId = storedWindowId(forQuad: index) else { return nil }
        return ProcessHelper.windowBounds(windowId: windowId)
    }

    /// Check if a quad's terminal window still exists
    static func isTerminalAlive(forQuad index: Int) -> Bool {
        guard let windowId = storedWindowId(forQuad: index) else { return false }
        return ProcessHelper.windowExists(windowId: windowId)
    }

    // MARK: - Launch Script

    private static func launchScriptPath(config: QuadConfig) -> String {
        // First check for mac-specific script in the setup dir
        let setupDir = config.setupDir.isEmpty
            ? (PathHelper.homeDir as NSString).appendingPathComponent("quad-claude-standalone")
            : config.setupDir
        let macScript = (setupDir as NSString).appendingPathComponent("QuadClaudeMac/Scripts/claude-launch-mac.sh")
        if FileManager.default.fileExists(atPath: macScript) {
            return macScript
        }
        // Fall back to generic script
        return (setupDir as NSString).appendingPathComponent("claude-launch.sh")
    }
}
