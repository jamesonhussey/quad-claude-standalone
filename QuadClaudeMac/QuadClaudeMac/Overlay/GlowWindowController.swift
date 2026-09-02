import Cocoa
import QuartzCore

/// Displays a pulsing colored border overlay around a terminal window.
/// Sits BEHIND the terminal so the glow peeks out around the edges.
class GlowWindowController {
    private let trackedWindowId: CGWindowID
    private let quadIndex: Int
    private var window: NSWindow?
    private var trackingTimer: Timer?
    var isPaused = false

    // Zero margin: glow window matches terminal exactly, border draws inward
    private let margin: CGFloat = 0

    init(trackedWindowId: CGWindowID, quadIndex: Int, color: String) {
        self.trackedWindowId = trackedWindowId
        self.quadIndex = quadIndex

        let nsColor = Self.colorFromName(color)

        let initialFrame = terminalFrameExpanded() ?? NSRect(x: 100, y: 100, width: 400, height: 300)
        let win = NSWindow(contentRect: initialFrame, styleMask: .borderless, backing: .buffered, defer: false)
        win.backgroundColor = .clear
        win.isOpaque = false
        win.hasShadow = false
        // Just above normal so borders stay visible even when other apps are focused
        win.level = AppDelegate.overlayLevel
        win.ignoresMouseEvents = true
        win.collectionBehavior = [.transient, .ignoresCycle, .stationary]
        win.isReleasedWhenClosed = false

        let glowView = GlowBorderView(frame: NSRect(origin: .zero, size: initialFrame.size))
        glowView.glowColor = nsColor
        glowView.borderMargin = margin
        glowView.autoresizingMask = [.width, .height]
        win.contentView = glowView

        self.window = win
    }

    func show() {
        guard let window = window else { return }
        positionOverTerminal()
        window.orderFront(nil)

        trackingTimer = Timer.scheduledTimer(withTimeInterval: 0.5, repeats: true) { [weak self] _ in
            self?.onTimerTick()
        }
    }

    func close() {
        trackingTimer?.invalidate()
        trackingTimer = nil
        window?.orderOut(nil)
        window?.close()
        window = nil
    }

    func showWindow() {
        isPaused = false
        window?.orderFront(nil)
    }

    func hideWindow() {
        isPaused = true
        window?.orderOut(nil)
    }

    func setLevel(_ level: NSWindow.Level) {
        window?.level = level
    }

    private func positionOverTerminal() {
        guard let frame = terminalFrameExpanded(), let window = window else { return }
        window.setFrame(frame, display: true)
    }

    private func terminalFrameExpanded() -> NSRect? {
        guard let bounds = ProcessHelper.windowBounds(windowId: trackedWindowId) else { return nil }
        guard let screen = NSScreen.main else { return nil }
        let screenHeight = screen.frame.height
        let m = margin

        return NSRect(
            x: bounds.origin.x - m,
            y: screenHeight - bounds.maxY - m,
            width: bounds.width + m * 2,
            height: bounds.height + m * 2
        )
    }

    private func onTimerTick() {
        if isPaused { return }

        if !ProcessHelper.windowIsOnScreen(windowId: trackedWindowId) {
            window?.orderOut(nil)
            return
        }

        if let win = window, !win.isVisible {
            win.orderFront(nil)
        }

        positionOverTerminal()
    }

    private static func colorFromName(_ name: String) -> NSColor {
        switch name {
        case "red":    return NSColor(red: 1.0, green: 0.27, blue: 0.27, alpha: 1.0)
        case "yellow": return NSColor(red: 1.0, green: 0.88, blue: 0.4, alpha: 1.0)
        default:       return NSColor(red: 0.0, green: 1.0, blue: 0.53, alpha: 1.0)
        }
    }
}

// MARK: - Glow Border View

class GlowBorderView: NSView {
    var glowColor: NSColor = .green {
        didSet { needsDisplay = true }
    }
    var borderMargin: CGFloat = 4

    private var glowRadius: CGFloat = 15
    private var animationTimer: Timer?
    private var animationPhase: CGFloat = 0

    override init(frame: NSRect) {
        super.init(frame: frame)
        wantsLayer = false
        setupAnimation()
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) not supported")
    }

    private func setupAnimation() {
        animationTimer?.invalidate()
        animationTimer = Timer.scheduledTimer(withTimeInterval: 1.0 / 30.0, repeats: true) { [weak self] _ in
            guard let self = self else { return }
            self.animationPhase += 0.05
            self.glowRadius = 16.5 + 8.5 * sin(self.animationPhase)
            self.needsDisplay = true
        }
    }

    override func draw(_ dirtyRect: NSRect) {
        guard let ctx = NSGraphicsContext.current?.cgContext else { return }
        ctx.clear(bounds)

        // Border draws inward from the window edges so adjacent quads don't overlap
        let inset: CGFloat = 3
        let borderRect = NSRect(
            x: inset,
            y: inset,
            width: bounds.width - inset * 2,
            height: bounds.height - inset * 2
        )
        let path = CGPath(roundedRect: borderRect, cornerWidth: 6, cornerHeight: 6, transform: nil)

        // Clip to bounds so glow doesn't bleed into adjacent quads
        ctx.clip(to: bounds)

        // Draw outer glow (shadow effect)
        ctx.saveGState()
        ctx.setShadow(offset: .zero, blur: glowRadius, color: glowColor.withAlphaComponent(0.7).cgColor)
        ctx.setStrokeColor(glowColor.withAlphaComponent(0.6).cgColor)
        ctx.setLineWidth(6)
        ctx.addPath(path)
        ctx.strokePath()
        ctx.restoreGState()

        // Draw inner glow
        ctx.saveGState()
        ctx.setShadow(offset: .zero, blur: 4, color: glowColor.withAlphaComponent(0.9).cgColor)
        ctx.setStrokeColor(glowColor.withAlphaComponent(0.8).cgColor)
        ctx.setLineWidth(4)
        ctx.addPath(path)
        ctx.strokePath()
        ctx.restoreGState()

        // Draw crisp border line
        ctx.setStrokeColor(glowColor.cgColor)
        ctx.setLineWidth(2)
        ctx.addPath(path)
        ctx.strokePath()
    }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        if window == nil {
            animationTimer?.invalidate()
            animationTimer = nil
        }
    }
}
