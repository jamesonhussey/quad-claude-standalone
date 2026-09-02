import Cocoa
import ApplicationServices

/// Wrapper around AXUIElement APIs for controlling external application windows.
/// Requires Accessibility permission (System Settings > Privacy > Accessibility).
enum AccessibilityBridge {

    /// Check if we have Accessibility permission
    static var hasPermission: Bool {
        AXIsProcessTrustedWithOptions(
            [kAXTrustedCheckOptionPrompt.takeUnretainedValue(): false] as CFDictionary
        )
    }

    /// Prompt the user to grant Accessibility permission
    static func requestPermission() {
        AXIsProcessTrustedWithOptions(
            [kAXTrustedCheckOptionPrompt.takeUnretainedValue(): true] as CFDictionary
        )
    }

    /// Get the AXUIElement for an application by PID
    static func appElement(pid: pid_t) -> AXUIElement {
        AXUIElementCreateApplication(pid)
    }

    /// Get all windows for an application
    static func windows(for pid: pid_t) -> [AXUIElement] {
        let app = appElement(pid: pid)
        var value: AnyObject?
        let result = AXUIElementCopyAttributeValue(app, kAXWindowsAttribute as CFString, &value)
        guard result == .success, let windows = value as? [AXUIElement] else { return [] }
        return windows
    }

    /// Get the position of a window
    static func windowPosition(_ window: AXUIElement) -> CGPoint? {
        var value: AnyObject?
        guard AXUIElementCopyAttributeValue(window, kAXPositionAttribute as CFString, &value) == .success,
              let axValue = value else { return nil }
        var point = CGPoint.zero
        AXValueGetValue(axValue as! AXValue, .cgPoint, &point)
        return point
    }

    /// Get the size of a window
    static func windowSize(_ window: AXUIElement) -> CGSize? {
        var value: AnyObject?
        guard AXUIElementCopyAttributeValue(window, kAXSizeAttribute as CFString, &value) == .success,
              let axValue = value else { return nil }
        var size = CGSize.zero
        AXValueGetValue(axValue as! AXValue, .cgSize, &size)
        return size
    }

    /// Move a window to a specific position
    static func setWindowPosition(_ window: AXUIElement, to point: CGPoint) -> Bool {
        var mutablePoint = point
        guard let axValue = AXValueCreate(.cgPoint, &mutablePoint) else { return false }
        return AXUIElementSetAttributeValue(window, kAXPositionAttribute as CFString, axValue) == .success
    }

    /// Resize a window
    static func setWindowSize(_ window: AXUIElement, to size: CGSize) -> Bool {
        var mutableSize = size
        guard let axValue = AXValueCreate(.cgSize, &mutableSize) else { return false }
        return AXUIElementSetAttributeValue(window, kAXSizeAttribute as CFString, axValue) == .success
    }

    /// Move and resize a window to match a frame
    static func setWindowFrame(_ window: AXUIElement, to frame: NSRect) -> Bool {
        // CGWindow coords: origin top-left. AXUIElement uses the same coordinate system.
        let posOk = setWindowPosition(window, to: frame.origin)
        let sizeOk = setWindowSize(window, to: frame.size)
        return posOk && sizeOk
    }

    /// Get the title of a window
    static func windowTitle(_ window: AXUIElement) -> String? {
        var value: AnyObject?
        guard AXUIElementCopyAttributeValue(window, kAXTitleAttribute as CFString, &value) == .success else {
            return nil
        }
        return value as? String
    }

    /// Find a Terminal.app window by matching its CGWindowID.
    /// This uses position/size matching between CGWindowList and AXUIElement windows.
    static func findTerminalWindow(matchingWindowId: CGWindowID, terminalPID: pid_t) -> AXUIElement? {
        guard let targetBounds = ProcessHelper.windowBounds(windowId: matchingWindowId) else { return nil }

        let axWindows = windows(for: terminalPID)
        for axWindow in axWindows {
            guard let pos = windowPosition(axWindow), let size = windowSize(axWindow) else { continue }
            // Match by position (within 2px tolerance for rounding)
            if abs(pos.x - targetBounds.origin.x) < 2 &&
               abs(pos.y - targetBounds.origin.y) < 2 &&
               abs(size.width - targetBounds.size.width) < 2 &&
               abs(size.height - targetBounds.size.height) < 2 {
                return axWindow
            }
        }
        return nil
    }
}
