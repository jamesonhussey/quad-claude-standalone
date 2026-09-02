import Cocoa

enum StatusCommand {
    static func execute(appDelegate: AppDelegate, quadIndex: Int, windowId: CGWindowID) {
        // Close existing status widget for this quad
        if let existing = appDelegate.statusControllers[quadIndex] {
            existing.close()
        }

        let controller = StatusWidgetController(trackedWindowId: windowId, quadIndex: quadIndex, appDelegate: appDelegate)
        appDelegate.statusControllers[quadIndex] = controller
        controller.show()
    }
}
