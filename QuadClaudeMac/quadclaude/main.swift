import Foundation

/// QuadClaude CLI shim — sends commands to the running QuadClaudeMac.app via Unix socket.
/// For commands that don't need GUI (track, setup), executes directly.

let args = CommandLine.arguments

guard args.count >= 2 else {
    printUsage()
    exit(1)
}

let command = args[1].lowercased()

switch command {
case "track":
    // Track runs locally (no GUI needed)
    exit(TrackCommandCLI.execute())

case "setup":
    // Setup is interactive CLI
    exit(SetupCommandCLI.execute())

case "launch", "glow", "kill-glow", "status", "quit":
    // These need the GUI app — send via socket
    let request = buildRequest(command: command, args: Array(args.dropFirst(2)))
    let response = sendToApp(request)
    if let msg = response["message"] as? String, !msg.isEmpty {
        print(msg)
    }
    exit(response["status"] as? String == "ok" ? 0 : 1)

default:
    printUsage()
    exit(1)
}

// MARK: - Socket Communication

func sendToApp(_ request: [String: Any]) -> [String: Any] {
    let socketPath = appSupportDir() + "/quadclaude.sock"

    // Check if socket exists, if not launch the app
    if !FileManager.default.fileExists(atPath: socketPath) {
        launchApp()
        // Wait for socket to appear
        for _ in 0..<20 {
            Thread.sleep(forTimeInterval: 0.5)
            if FileManager.default.fileExists(atPath: socketPath) { break }
        }
    }

    // Connect to socket
    let sock = socket(AF_UNIX, SOCK_STREAM, 0)
    guard sock >= 0 else {
        print("Error: Failed to create socket")
        return ["status": "error", "message": "Socket creation failed"]
    }
    defer { close(sock) }

    var addr = sockaddr_un()
    addr.sun_family = sa_family_t(AF_UNIX)
    let pathBytes = socketPath.utf8CString
    withUnsafeMutablePointer(to: &addr.sun_path) { ptr in
        ptr.withMemoryRebound(to: CChar.self, capacity: pathBytes.count) { dest in
            pathBytes.withUnsafeBufferPointer { src in
                _ = memcpy(dest, src.baseAddress!, pathBytes.count)
            }
        }
    }

    let connectResult = withUnsafePointer(to: &addr) { ptr in
        ptr.withMemoryRebound(to: sockaddr.self, capacity: 1) { sockPtr in
            connect(sock, sockPtr, socklen_t(MemoryLayout<sockaddr_un>.size))
        }
    }

    if connectResult != 0 {
        // Socket exists but can't connect — app may have died. Relaunch.
        launchApp()
        Thread.sleep(forTimeInterval: 2)

        let retryResult = withUnsafePointer(to: &addr) { ptr in
            ptr.withMemoryRebound(to: sockaddr.self, capacity: 1) { sockPtr in
                connect(sock, sockPtr, socklen_t(MemoryLayout<sockaddr_un>.size))
            }
        }
        if retryResult != 0 {
            print("Error: Cannot connect to QuadClaudeMac")
            return ["status": "error", "message": "Connection failed"]
        }
    }

    // Send request
    guard let jsonData = try? JSONSerialization.data(withJSONObject: request),
          let jsonStr = String(data: jsonData, encoding: .utf8) else {
        return ["status": "error", "message": "Failed to encode request"]
    }

    let message = jsonStr + "\n"
    message.withCString { ptr in
        _ = write(sock, ptr, message.utf8.count)
    }

    // Read response
    var buffer = [UInt8](repeating: 0, count: 4096)
    let bytesRead = read(sock, &buffer, buffer.count)
    guard bytesRead > 0,
          let responseStr = String(bytes: buffer[0..<bytesRead], encoding: .utf8),
          let responseData = responseStr.data(using: .utf8),
          let response = try? JSONSerialization.jsonObject(with: responseData) as? [String: Any] else {
        return ["status": "ok"]
    }

    return response
}

func launchApp() {
    // Try to find the app
    let possiblePaths = [
        "/Applications/QuadClaudeMac.app",
        NSString(string: "~/Applications/QuadClaudeMac.app").expandingTildeInPath,
        // During development, check build directory
        NSString(string: "~/quad-claude-standalone/QuadClaudeMac/build/Release/QuadClaudeMac.app").expandingTildeInPath,
        NSString(string: "~/quad-claude-standalone/QuadClaudeMac/build/Debug/QuadClaudeMac.app").expandingTildeInPath,
    ]

    for path in possiblePaths {
        if FileManager.default.fileExists(atPath: path) {
            let process = Process()
            process.executableURL = URL(fileURLWithPath: "/usr/bin/open")
            process.arguments = ["-a", path]
            try? process.run()
            return
        }
    }

    // Fallback: open by bundle name
    let process = Process()
    process.executableURL = URL(fileURLWithPath: "/usr/bin/open")
    process.arguments = ["-a", "QuadClaudeMac"]
    try? process.run()
}

func buildRequest(command: String, args: [String]) -> [String: Any] {
    var request: [String: Any] = ["command": command]

    // Parse --color flag
    if let colorIdx = args.firstIndex(of: "--color"), colorIdx + 1 < args.count {
        request["color"] = args[colorIdx + 1]
    }

    // Parse --quad flag
    if let quadIdx = args.firstIndex(of: "--quad"), quadIdx + 1 < args.count {
        request["quadIndex"] = Int(args[quadIdx + 1]) ?? 0
    }

    // Use QUAD_INDEX env var as default
    if request["quadIndex"] == nil,
       let envQuad = ProcessInfo.processInfo.environment["QUAD_INDEX"],
       let idx = Int(envQuad) {
        request["quadIndex"] = idx
    }

    return request
}

func appSupportDir() -> String {
    let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first!
    return base.appendingPathComponent("QuadClaude").path
}

func printUsage() {
    print("""
    QuadClaude for Mac - Claude Code Terminal Overlay

    Usage:
      quadclaude launch              Open 4 terminals in a 2x2 grid
      quadclaude glow --color green  Show pulsing glow border (green/red/yellow)
      quadclaude kill-glow           Remove glow border
      quadclaude track               Update branch/directory state
      quadclaude setup               Run interactive setup wizard
      quadclaude quit                Stop the background app
    """)
}

// MARK: - Direct command wrappers (no GUI needed)

enum TrackCommandCLI {
    static func execute() -> Int32 {
        // Inline implementation since we can't import from the app target
        let quadIndex = ProcessInfo.processInfo.environment["QUAD_INDEX"] ?? "0"
        let supportDir = appSupportDir()
        try? FileManager.default.createDirectory(atPath: supportDir, withIntermediateDirectories: true)

        let cwd = FileManager.default.currentDirectoryPath
        let project = (cwd as NSString).lastPathComponent
        let branch = getGitBranch(dir: cwd)

        let state: [String: String] = ["cwd": cwd, "project": project, "branch": branch]
        guard let jsonData = try? JSONSerialization.data(withJSONObject: state),
              let json = String(data: jsonData, encoding: .utf8) else { return 1 }

        let statePath = (supportDir as NSString).appendingPathComponent("quad-\(quadIndex).cwd.json")
        let tmpPath = statePath + ".tmp"

        do {
            try json.write(toFile: tmpPath, atomically: false, encoding: .utf8)
            if FileManager.default.fileExists(atPath: statePath) {
                try FileManager.default.removeItem(atPath: statePath)
            }
            try FileManager.default.moveItem(atPath: tmpPath, toPath: statePath)
        } catch {
            try? json.write(toFile: statePath, atomically: true, encoding: .utf8)
        }
        return 0
    }

    private static func getGitBranch(dir: String) -> String {
        if let gitDir = findGitDir(from: dir) {
            let headPath = (gitDir as NSString).appendingPathComponent("HEAD")
            if let head = try? String(contentsOfFile: headPath, encoding: .utf8).trimmingCharacters(in: .whitespacesAndNewlines) {
                if head.hasPrefix("ref: refs/heads/") {
                    return String(head.dropFirst("ref: refs/heads/".count))
                }
                if head.count >= 7 { return String(head.prefix(7)) }
            }
        }

        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/git")
        process.arguments = ["symbolic-ref", "--short", "HEAD"]
        process.currentDirectoryURL = URL(fileURLWithPath: dir)
        let pipe = Pipe()
        process.standardOutput = pipe
        process.standardError = Pipe()
        do {
            try process.run()
            process.waitUntilExit()
            if process.terminationStatus == 0 {
                let data = pipe.fileHandleForReading.readDataToEndOfFile()
                if let output = String(data: data, encoding: .utf8)?.trimmingCharacters(in: .whitespacesAndNewlines), !output.isEmpty {
                    return output
                }
            }
        } catch {}
        return ""
    }

    private static func findGitDir(from dir: String) -> String? {
        var current = dir
        while current != "/" {
            let gitPath = (current as NSString).appendingPathComponent(".git")
            var isDirectory: ObjCBool = false
            if FileManager.default.fileExists(atPath: gitPath, isDirectory: &isDirectory) {
                if isDirectory.boolValue { return gitPath }
                if let content = try? String(contentsOfFile: gitPath, encoding: .utf8).trimmingCharacters(in: .whitespacesAndNewlines),
                   content.hasPrefix("gitdir: ") {
                    return String(content.dropFirst("gitdir: ".count)).trimmingCharacters(in: .whitespacesAndNewlines)
                }
            }
            current = (current as NSString).deletingLastPathComponent
        }
        return nil
    }
}

enum SetupCommandCLI {
    static func execute() -> Int32 {
        print("")
        print("╔══════════════════════════════════════╗")
        print("║     QuadClaude Mac Setup Wizard      ║")
        print("╚══════════════════════════════════════╝")
        print("")

        let homeDir = FileManager.default.homeDirectoryForCurrentUser.path
        let supportDir = appSupportDir()
        try? FileManager.default.createDirectory(atPath: supportDir, withIntermediateDirectories: true)

        // Minimal inline setup - reads from stdin, writes config
        print("Step 1: Projects directory")
        let defaultDir = (homeDir as NSString).appendingPathComponent("Projects")
        if FileManager.default.fileExists(atPath: defaultDir) {
            print("  Found: \(defaultDir)")
            print("  Use this? [Y/n]: ", terminator: "")
        } else {
            print("  Enter path: ", terminator: "")
        }
        let projectsInput = readLine()?.trimmingCharacters(in: .whitespaces) ?? ""
        let projectsDir = projectsInput.isEmpty || projectsInput.lowercased() == "y"
            ? defaultDir
            : (projectsInput as NSString).expandingTildeInPath

        // Step 2: Layout (minimal — multi-project or worktrees). Worktrees needs a
        // base repo + base branch so LaunchCommand can emit the shared-contract
        // launch-env vars and the launcher can open each quad's own worktree.
        print("\nStep 2: Layout")
        print("  1) multi-project  — each quad picks any project")
        print("  2) worktrees      — one repo + a git worktree per quad")
        print("  Choice [1]: ", terminator: "")
        let layoutInput = readLine()?.trimmingCharacters(in: .whitespaces) ?? "1"
        var layout = "multi-project"
        var worktreeBase = ""
        var worktreeBaseBranch = "main"
        if layoutInput == "2" {
            layout = "worktrees"
            print("  Base repo name (inside projects dir): ", terminator: "")
            worktreeBase = readLine()?.trimmingCharacters(in: .whitespaces) ?? ""
            print("  Base branch each worktree resets to [main]: ", terminator: "")
            let bb = readLine()?.trimmingCharacters(in: .whitespaces) ?? ""
            if !bb.isEmpty { worktreeBaseBranch = bb }
        }

        print("\nStep 3: Permission mode")
        print("  1) bypassPermissions  2) auto  3) manual")
        print("  Choice [1]: ", terminator: "")
        let permInput = readLine()?.trimmingCharacters(in: .whitespaces) ?? "1"
        let permMode = permInput == "2" ? "auto" : permInput == "3" ? "manual" : "bypassPermissions"

        print("\nStep 4: Enable sounds? [Y/n]: ", terminator: "")
        let soundsInput = readLine()?.trimmingCharacters(in: .whitespaces).lowercased() ?? ""
        let sounds = soundsInput != "n"

        // Write config (snake_case keys — QuadConfig decodes convertFromSnakeCase)
        var config: [String: Any] = [
            "projects_dir": projectsDir,
            "setup_dir": (homeDir as NSString).appendingPathComponent("quad-claude-standalone"),
            "terminal_profile": "Basic",
            "layout": layout,
            "sounds_enabled": sounds,
            "quad_labels": ["Quad 1", "Quad 2", "Quad 3", "Quad 4"],
            "permission_mode": permMode,
            "allow_list": [
                "Bash(git clone:*)", "Bash(npm install:*)", "Bash(npm run:*)",
                "Bash(npx prisma:*)", "Bash(npx eslint:*)",
                "Skill(update-config)", "Skill(update-config:*)"
            ]
        ]
        if layout == "worktrees" && !worktreeBase.isEmpty {
            config["worktree_base"] = worktreeBase
            config["worktree_pattern"] = "{base} - Quad-{n}"
            config["worktree_base_branch"] = worktreeBaseBranch
        }

        let configPath = (supportDir as NSString).appendingPathComponent("config.json")
        if let data = try? JSONSerialization.data(withJSONObject: config, options: .prettyPrinted) {
            try? data.write(to: URL(fileURLWithPath: configPath))
            print("\n  Config saved: \(configPath)")
        }

        print("\n  Setup complete! Run 'quadclaude launch' to start.")
        print("  Note: Grant Accessibility permission in System Settings for window positioning.")
        print("")
        return 0
    }
}
