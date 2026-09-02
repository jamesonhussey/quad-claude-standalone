import Cocoa

enum GlowCommand {
    static func execute(appDelegate: AppDelegate, quadIndex: Int, color: String) {
        // Kill any existing glow for this quad
        KillGlowCommand.execute(appDelegate: appDelegate, quadIndex: quadIndex)

        // Look up the terminal window for this quad
        guard let windowId = TerminalManager.storedWindowId(forQuad: quadIndex),
              ProcessHelper.windowExists(windowId: windowId) else {
            print("GlowCommand: No terminal window found for quad \(quadIndex)")
            return
        }

        let controller = GlowWindowController(trackedWindowId: windowId, quadIndex: quadIndex, color: color)
        appDelegate.glowControllers[quadIndex] = controller
        controller.show()
    }
}
