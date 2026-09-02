import Cocoa

struct QuadFrame {
    let index: Int
    let frame: NSRect  // Position and size for this quad
}

enum ScreenManager {
    /// Find the largest connected screen (by pixel area)
    static func largestScreen() -> NSScreen {
        let screens = NSScreen.screens
        guard !screens.isEmpty else {
            return NSScreen.main ?? NSScreen.screens[0]
        }
        return screens.max(by: { a, b in
            let areaA = a.frame.width * a.frame.height
            let areaB = b.frame.width * b.frame.height
            return areaA < areaB
        }) ?? screens[0]
    }

    /// Calculate 2x2 grid positions on the given screen.
    /// Uses visibleFrame which excludes the menu bar and dock.
    static func quadFrames(on screen: NSScreen? = nil) -> [QuadFrame] {
        let target = screen ?? largestScreen()
        let area = target.visibleFrame  // Excludes menu bar and dock

        let halfW = area.width / 2
        let halfH = area.height / 2

        // macOS coordinate system: origin is bottom-left
        // Quad layout:
        // ┌───────┬───────┐
        // │ Q0    │ Q1    │  (top row)
        // ├───────┼───────┤
        // │ Q2    │ Q3    │  (bottom row)
        // └───────┴───────┘

        return [
            QuadFrame(index: 0, frame: NSRect(x: area.minX,         y: area.minY + halfH, width: halfW, height: halfH)),  // Top-left
            QuadFrame(index: 1, frame: NSRect(x: area.minX + halfW, y: area.minY + halfH, width: halfW, height: halfH)),  // Top-right
            QuadFrame(index: 2, frame: NSRect(x: area.minX,         y: area.minY,          width: halfW, height: halfH)),  // Bottom-left
            QuadFrame(index: 3, frame: NSRect(x: area.minX + halfW, y: area.minY,          width: halfW, height: halfH)),  // Bottom-right
        ]
    }

    /// Calculate an appropriate font size for the given quad height
    /// Baseline: 13pt at 540px quad height, scaled proportionally, clamped 10-18
    static func fontSizeForQuad(height: CGFloat) -> Int {
        let baseline: CGFloat = 13
        let baselineHeight: CGFloat = 540
        let scaled = baseline * (height / baselineHeight)
        return Int(min(max(scaled, 10), 18))
    }

    /// Get the backing scale factor (Retina = 2.0, standard = 1.0)
    static func scaleFactor(for screen: NSScreen? = nil) -> CGFloat {
        let target = screen ?? largestScreen()
        return target.backingScaleFactor
    }
}
