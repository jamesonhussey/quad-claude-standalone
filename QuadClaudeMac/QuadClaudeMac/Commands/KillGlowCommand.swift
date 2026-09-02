import Cocoa

enum KillGlowCommand {
    static func execute(appDelegate: AppDelegate, quadIndex: Int) {
        if let controller = appDelegate.glowControllers[quadIndex] {
            controller.close()
            appDelegate.glowControllers.removeValue(forKey: quadIndex)
        }
    }
}
