import Foundation

/// Bridge to Terminal.app via AppleScript for operations that
/// AXUIElement cannot perform (launching windows, setting profiles).
enum AppleScriptBridge {

    /// Open a new Terminal.app window running the given shell command
    static func openTerminalWindow(command: String) -> Bool {
        let script = """
        tell application "Terminal"
            do script "\(escapeForAppleScript(command))"
            activate
        end tell
        """
        return runAppleScript(script) != nil
    }

    /// Open multiple Terminal.app windows with different commands in a single tell block
    static func openTerminalWindows(commands: [String]) -> Bool {
        let doScripts = commands.map { cmd in
            "do script \"\(escapeForAppleScript(cmd))\""
        }.joined(separator: "\n            ")

        let script = """
        tell application "Terminal"
            \(doScripts)
            activate
        end tell
        """
        return runAppleScript(script) != nil
    }

    /// Set the font size for a Terminal.app settings profile
    static func setTerminalFontSize(profile: String, size: Int) -> Bool {
        let script = """
        tell application "Terminal"
            set font size of settings set "\(escapeForAppleScript(profile))" to \(size)
        end tell
        """
        return runAppleScript(script) != nil
    }

    /// Create a Terminal.app profile if it doesn't exist
    static func ensureTerminalProfile(name: String, fontSize: Int = 13) -> Bool {
        let script = """
        tell application "Terminal"
            set profileNames to name of every settings set
            if profileNames does not contain "\(escapeForAppleScript(name))" then
                set newProfile to make new settings set with properties {name:"\(escapeForAppleScript(name))"}
            end if
            set font size of settings set "\(escapeForAppleScript(name))" to \(fontSize)
            set font name of settings set "\(escapeForAppleScript(name))" to "MenloRegular"
        end tell
        """
        return runAppleScript(script) != nil
    }

    /// Get the number of Terminal.app windows
    static func terminalWindowCount() -> Int {
        let script = """
        tell application "Terminal"
            return count of windows
        end tell
        """
        guard let result = runAppleScript(script) else { return 0 }
        return Int(result) ?? 0
    }

    /// Close a specific Terminal.app window by index (1-based)
    static func closeTerminalWindow(index: Int) -> Bool {
        let script = """
        tell application "Terminal"
            close window \(index)
        end tell
        """
        return runAppleScript(script) != nil
    }

    /// Set bounds of a Terminal.app window by index (1-based).
    /// Bounds are {x1, y1, x2, y2} in screen coordinates (top-left origin).
    static func setTerminalWindowBounds(index: Int, x1: Int, y1: Int, x2: Int, y2: Int) -> Bool {
        let script = """
        tell application "Terminal"
            set bounds of window \(index) to {\(x1), \(y1), \(x2), \(y2)}
        end tell
        """
        return runAppleScript(script) != nil
    }

    /// Position all Terminal.app windows in a 2x2 grid.
    /// Takes an array of 4 bounds: [(x1,y1,x2,y2), ...]
    /// Windows are indexed newest-first in Terminal (window 1 = most recent).
    static func positionTerminalWindows(bounds: [(x1: Int, y1: Int, x2: Int, y2: Int)]) -> Bool {
        // Terminal.app indexes windows 1-based, newest first.
        // We opened 4 windows, so windows 1-4 are our new ones (in reverse order).
        // Window 1 = last opened (quad 3), window 4 = first opened (quad 0).
        var setBounds = ""
        for (i, b) in bounds.enumerated() {
            // Map quad index to Terminal window index (reverse order)
            let windowIndex = bounds.count - i
            setBounds += "set bounds of window \(windowIndex) to {\(b.x1), \(b.y1), \(b.x2), \(b.y2)}\n            "
        }

        let script = """
        tell application "Terminal"
            \(setBounds)
        end tell
        """
        return runAppleScript(script) != nil
    }

    // MARK: - Helpers

    private static func runAppleScript(_ source: String) -> String? {
        let script = NSAppleScript(source: source)
        var errorInfo: NSDictionary?
        let result = script?.executeAndReturnError(&errorInfo)
        if let error = errorInfo {
            print("AppleScript error: \(error)")
            return nil
        }
        return result?.stringValue
    }

    private static func escapeForAppleScript(_ str: String) -> String {
        return str.replacingOccurrences(of: "\\", with: "\\\\")
                  .replacingOccurrences(of: "\"", with: "\\\"")
    }
}
