import Cocoa

// QuadClaudeMac - Main App Entry Point
// This is a background app (LSUIElement=true) that hosts overlay windows
// and listens for commands from the `quadclaude` CLI shim via Unix socket.

class QuadClaudeApp: NSApplication {
    let strongDelegate = AppDelegate()

    override init() {
        super.init()
        self.delegate = strongDelegate
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) not supported")
    }
}

// Use @main attribute for proper app lifecycle
@main
enum AppMain {
    static func main() {
        let app = QuadClaudeApp.shared
        app.run()
    }
}
