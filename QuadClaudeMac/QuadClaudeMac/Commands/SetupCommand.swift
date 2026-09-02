import Foundation

/// Interactive setup wizard. Runs in the CLI shim (terminal I/O).
enum SetupCommand {
    static func execute() -> Int32 {
        print("")
        print("╔══════════════════════════════════════╗")
        print("║     QuadClaude Mac Setup Wizard      ║")
        print("╚══════════════════════════════════════╝")
        print("")

        var config = QuadConfig.loadOrDefault()

        // Step 1: Projects directory
        config.projectsDir = askProjectsDir()

        // Step 2: Layout mode
        config.layout = askLayout()

        // Step 3: Worktree / dedicated project details
        if config.layout == "worktrees" {
            print("Enter the base repo name (inside projects dir):")
            config.worktreeBase = readLine()?.trimmingCharacters(in: .whitespaces) ?? ""
            // Base branch: the up-to-date branch each worktree is cut from and
            // reset to every time its quad opens. Mirrors C# ConfigureWorktrees.
            print("Base branch each worktree resets to [\(config.worktreeBaseBranch)]:")
            let bb = readLine()?.trimmingCharacters(in: .whitespaces) ?? ""
            if !bb.isEmpty { config.worktreeBaseBranch = bb }
        } else if config.layout == "dedicated-roles" {
            print("Enter the project name or full path:")
            config.dedicatedProject = readLine()?.trimmingCharacters(in: .whitespaces) ?? ""
            print("Enter labels for each quad (comma-separated, e.g. 'Frontend,Backend,Tests,Docs'):")
            let labels = readLine()?.split(separator: ",").map { String($0).trimmingCharacters(in: .whitespaces) } ?? []
            if labels.count >= 4 { config.quadLabels = Array(labels.prefix(4)) }
        }

        // Step 4: Terminal.app profile
        config.terminalProfile = askTerminalProfile()

        // Step 5: Sounds
        config.soundsEnabled = askYesNo("Enable sound notifications?", defaultYes: true)

        // Step 6: Permission mode
        config.permissionMode = askPermissionMode()

        // Step 7: Setup dir
        let defaultSetupDir = (PathHelper.homeDir as NSString).appendingPathComponent("quad-claude-standalone")
        if FileManager.default.fileExists(atPath: defaultSetupDir) {
            config.setupDir = defaultSetupDir
        } else {
            print("Enter the path to your quad-claude repo:")
            config.setupDir = readLine()?.trimmingCharacters(in: .whitespaces) ?? defaultSetupDir
        }

        // Save config
        do {
            try config.save()
            print("\n  Config saved to: \(QuadConfig.configURL.path)")
        } catch {
            print("\n  Error saving config: \(error)")
            return 1
        }

        // Write Claude Code hooks
        writeClaudeHooks(config: config)

        // Opt-in: install the bundled helper slash-commands into ~/.claude/commands
        installHelperCommands(config: config)

        // Check Accessibility permission
        print("\n  ⚠ QuadClaude needs Accessibility permission to position terminal windows.")
        print("    Go to: System Settings > Privacy & Security > Accessibility")
        print("    Add QuadClaudeMac.app to the allowed list.")

        print("\n  Setup complete! Run 'quadclaude launch' to start.")
        print("")
        return 0
    }

    // MARK: - Step Implementations

    private static func askProjectsDir() -> String {
        let candidates = [
            (PathHelper.homeDir as NSString).appendingPathComponent("Developer"),
            (PathHelper.homeDir as NSString).appendingPathComponent("Projects"),
            (PathHelper.homeDir as NSString).appendingPathComponent("Code"),
            (PathHelper.homeDir as NSString).appendingPathComponent("repos"),
        ]

        let existing = candidates.filter { FileManager.default.fileExists(atPath: $0) }

        print("Step 1: Projects Directory")
        if !existing.isEmpty {
            print("  Found these directories:")
            for (i, dir) in existing.enumerated() {
                print("    \(i + 1)) \(dir)")
            }
            print("    0) Enter a custom path")
            print("  Choice: ", terminator: "")
            if let choice = readLine()?.trimmingCharacters(in: .whitespaces),
               let num = Int(choice), num >= 1, num <= existing.count {
                return existing[num - 1]
            }
        }

        print("  Enter your projects directory path:")
        print("  > ", terminator: "")
        let path = readLine()?.trimmingCharacters(in: .whitespaces) ?? ""
        return PathHelper.expandTilde(path)
    }

    private static func askLayout() -> String {
        print("\nStep 2: Layout Mode")
        print("  1) multi-project  — Each quad picks any project")
        print("  2) worktrees      — One repo + 3 git worktrees")
        print("  3) hybrid         — Mix of repos and worktrees")
        print("  4) dedicated-roles — All quads same project, custom roles")
        print("  Choice [1]: ", terminator: "")

        let choice = readLine()?.trimmingCharacters(in: .whitespaces) ?? "1"
        switch choice {
        case "2": return "worktrees"
        case "3": return "hybrid"
        case "4": return "dedicated-roles"
        default: return "multi-project"
        }
    }

    private static func askTerminalProfile() -> String {
        print("\nStep 3: Terminal.app Profile")
        print("  Which Terminal.app profile to use?")
        print("  Common options: Basic, Pro, Homebrew, Novel, QuadClaude")
        print("  Profile name [Basic]: ", terminator: "")
        let input = readLine()?.trimmingCharacters(in: .whitespaces) ?? ""
        return input.isEmpty ? "Basic" : input
    }

    private static func askPermissionMode() -> String {
        print("\nStep 4: Claude Permission Mode")
        print("  1) bypassPermissions — Trust Claude fully (fastest)")
        print("  2) auto              — Auto-approve safe commands")
        print("  3) manual            — Ask for every action (safest)")
        print("  Choice [1]: ", terminator: "")

        let choice = readLine()?.trimmingCharacters(in: .whitespaces) ?? "1"
        switch choice {
        case "2": return "auto"
        case "3": return "manual"
        default: return "bypassPermissions"
        }
    }

    private static func askYesNo(_ prompt: String, defaultYes: Bool) -> Bool {
        let suffix = defaultYes ? "[Y/n]" : "[y/N]"
        print("\n  \(prompt) \(suffix): ", terminator: "")
        let input = readLine()?.trimmingCharacters(in: .whitespaces).lowercased() ?? ""
        if input.isEmpty { return defaultYes }
        return input.hasPrefix("y")
    }

    // MARK: - Helper Commands

    // Copy the repo's bundled slash-commands (.claude/commands/*.md) into the
    // user's ~/.claude/commands so they work in EVERY Claude Code session on this
    // machine. Opt-in, copy-if-missing (never clobbers a same-named command),
    // and trivially reversible (plain files). Mirrors C# InstallHelperCommands.
    private static func installHelperCommands(config: QuadConfig) {
        let fm = FileManager.default
        let srcDir = (config.setupDir as NSString).appendingPathComponent(".claude/commands")
        guard let files = try? fm.contentsOfDirectory(atPath: srcDir) else { return }
        let mdFiles = files.filter { $0.hasSuffix(".md") }
        if mdFiles.isEmpty { return }

        let destDir = (PathHelper.claudeDir as NSString).appendingPathComponent("commands")

        print("\n  Helper commands")
        print("  QuadClaude bundles a few optional slash-commands.")
        print("  Installing them copies plain markdown files into:")
        print("    \(destDir)")
        print("  That makes them available in EVERY Claude Code session on this machine")
        print("  (including your work worktrees). This is opt-in — say no to skip, and you")
        print("  can remove any later by deleting its .md file from that folder.")

        if !askYesNo("  Install these helper commands now?", defaultYes: true) {
            print("  [SKIP] Helper commands not installed.")
            return
        }

        try? fm.createDirectory(atPath: destDir, withIntermediateDirectories: true)
        for name in mdFiles {
            let dest = (destDir as NSString).appendingPathComponent(name)
            if fm.fileExists(atPath: dest) {
                print("  [SKIP] \(name) — you already have a command by this name (left it alone).")
                continue
            }
            let src = (srcDir as NSString).appendingPathComponent(name)
            do {
                try fm.copyItem(atPath: src, toPath: dest)
                print("  [OK] \(name)")
            } catch {
                print("  [FAIL] \(name) — \(error.localizedDescription)")
            }
        }
    }

    // MARK: - Claude Hooks

    private static func writeClaudeHooks(config: QuadConfig) {
        let settingsPath = PathHelper.claudeSettingsPath
        let claudeDir = PathHelper.claudeDir

        // Ensure .claude directory exists
        try? FileManager.default.createDirectory(atPath: claudeDir, withIntermediateDirectories: true)

        // Read existing settings
        var settings: [String: Any] = [:]
        if let data = try? Data(contentsOf: URL(fileURLWithPath: settingsPath)),
           let existing = try? JSONSerialization.jsonObject(with: data) as? [String: Any] {
            settings = existing
        }

        // Find quadclaude CLI path
        let cliPath = "/usr/local/bin/quadclaude"

        // Build hooks
        var hooks: [String: Any] = (settings["hooks"] as? [String: Any]) ?? [:]

        // Stop hooks: green glow + track + optional sound
        var stopHooks: [[String: Any]] = [
            ["type": "command", "command": "\(cliPath) glow --color green", "async": true],
            ["type": "command", "command": "\(cliPath) track", "async": true]
        ]
        if config.soundsEnabled {
            let soundPath = (config.setupDir as NSString).appendingPathComponent("notification-pack/inbox.wav")
            stopHooks.append(["type": "command", "command": "afplay \(soundPath) &", "async": true])
        }
        hooks["Stop"] = stopHooks

        // UserPromptSubmit hook: kill glow
        hooks["UserPromptSubmit"] = [
            ["type": "command", "command": "\(cliPath) kill-glow", "async": true]
        ]

        // Notification hooks
        var notificationHooks: [[String: Any]] = [
            ["type": "command", "command": "\(cliPath) glow --color red", "event": "permission_prompt", "async": true],
            ["type": "command", "command": "\(cliPath) glow --color yellow", "event": "idle_prompt", "async": true]
        ]
        if config.soundsEnabled {
            let alertPath = (config.setupDir as NSString).appendingPathComponent("notification-pack/alert.wav")
            notificationHooks.append(["type": "command", "command": "afplay \(alertPath) &", "event": "permission_prompt", "async": true])
            notificationHooks.append(["type": "command", "command": "afplay \(alertPath) &", "event": "idle_prompt", "async": true])
        }
        hooks["Notification"] = notificationHooks

        settings["hooks"] = hooks

        // Permission settings
        if config.permissionMode == "bypassPermissions" {
            var permissions = (settings["permissions"] as? [String: Any]) ?? [:]
            permissions["defaultMode"] = "bypassPermissions"
            permissions["allow"] = config.allowList
            settings["permissions"] = permissions
            settings["skipDangerousModePermissionPrompt"] = true
        } else if config.permissionMode == "auto" {
            var permissions = (settings["permissions"] as? [String: Any]) ?? [:]
            permissions["defaultMode"] = "auto"
            permissions["allow"] = config.allowList
            settings["permissions"] = permissions
        }

        // Write settings
        if let jsonData = try? JSONSerialization.data(withJSONObject: settings, options: [.prettyPrinted, .sortedKeys]) {
            try? jsonData.write(to: URL(fileURLWithPath: settingsPath), options: .atomic)
            print("  Claude hooks written to: \(settingsPath)")
        }
    }
}
