import Foundation

enum ProcessHelper {
    /// Get the parent PID of a given process using sysctl
    static func parentPID(of pid: pid_t) -> pid_t? {
        var info = kinfo_proc()
        var size = MemoryLayout<kinfo_proc>.size
        var mib: [Int32] = [CTL_KERN, KERN_PROC, KERN_PROC_PID, pid]
        guard sysctl(&mib, 4, &info, &size, nil, 0) == 0 else { return nil }
        let ppid = info.kp_eproc.e_ppid
        return ppid > 0 ? ppid : nil
    }

    /// Walk up the process tree from the given PID looking for a process with the given name
    static func findAncestor(from pid: pid_t, named targetName: String) -> pid_t? {
        var current = pid
        var visited = Set<pid_t>()

        while current > 1 {
            if visited.contains(current) { break }
            visited.insert(current)

            if let name = processName(pid: current), name == targetName {
                return current
            }

            guard let parent = parentPID(of: current) else { break }
            current = parent
        }
        return nil
    }

    /// Get the name of a process by PID
    static func processName(pid: pid_t) -> String? {
        var info = kinfo_proc()
        var size = MemoryLayout<kinfo_proc>.size
        var mib: [Int32] = [CTL_KERN, KERN_PROC, KERN_PROC_PID, pid]
        guard sysctl(&mib, 4, &info, &size, nil, 0) == 0 else { return nil }
        return withUnsafePointer(to: info.kp_proc.p_comm) { ptr in
            ptr.withMemoryRebound(to: CChar.self, capacity: Int(MAXCOMLEN)) { cStr in
                String(cString: cStr)
            }
        }
    }

    /// Find the PID of Terminal.app (if running)
    static func terminalAppPID() -> pid_t? {
        let workspace = NSWorkspace.shared
        let runningApps = workspace.runningApplications
        return runningApps.first(where: { $0.bundleIdentifier == "com.apple.Terminal" })?.processIdentifier
    }

    /// List all windows for a given process using CGWindowList
    static func windowList(for pid: pid_t? = nil) -> [[String: Any]] {
        let options: CGWindowListOption = [.optionOnScreenOnly, .excludeDesktopElements]
        guard let windowInfoList = CGWindowListCopyWindowInfo(options, kCGNullWindowID) as? [[String: Any]] else {
            return []
        }

        if let pid = pid {
            return windowInfoList.filter { info in
                guard let ownerPID = info[kCGWindowOwnerPID as String] as? pid_t else { return false }
                return ownerPID == pid
            }
        }
        return windowInfoList
    }

    /// Get the bounds (CGRect) of a window by its CGWindowID
    static func windowBounds(windowId: CGWindowID) -> CGRect? {
        guard let windowInfoList = CGWindowListCopyWindowInfo([.optionIncludingWindow], windowId) as? [[String: Any]],
              let info = windowInfoList.first,
              let boundsDict = info[kCGWindowBounds as String] as? [String: Any] else {
            return nil
        }
        let rect = CGRect(
            x: boundsDict["X"] as? CGFloat ?? 0,
            y: boundsDict["Y"] as? CGFloat ?? 0,
            width: boundsDict["Width"] as? CGFloat ?? 0,
            height: boundsDict["Height"] as? CGFloat ?? 0
        )
        return rect
    }

    /// Check if a window still exists
    static func windowExists(windowId: CGWindowID) -> Bool {
        guard let list = CGWindowListCopyWindowInfo([.optionIncludingWindow], windowId) as? [[String: Any]] else {
            return false
        }
        return !list.isEmpty
    }

    /// Check if a window is currently visible on screen (not minimized)
    static func windowIsOnScreen(windowId: CGWindowID) -> Bool {
        guard let list = CGWindowListCopyWindowInfo([.optionIncludingWindow], windowId) as? [[String: Any]],
              let info = list.first else {
            return false
        }
        // kCGWindowIsOnscreen is true when the window is visible and not minimized
        return info[kCGWindowIsOnscreen as String] as? Bool ?? false
    }
}

// NSWorkspace import
import Cocoa
