import Foundation

struct QuadConfig: Codable {
    var projectsDir: String = ""
    var setupDir: String = ""
    var terminalProfile: String = "Basic"
    var layout: String = "multi-project"  // multi-project, worktrees, hybrid, dedicated-roles
    var soundsEnabled: Bool = true
    var quadLabels: [String] = ["Quad 1", "Quad 2", "Quad 3", "Quad 4"]
    var worktreeBase: String?
    // Pattern for each quad's worktree folder name. {base}→base repo, {n}→quad number.
    var worktreePattern: String = "{base} - Quad-{n}"
    // Branch each worktree is reset/detached to when its quad opens (e.g. main, develop, staging).
    var worktreeBaseBranch: String = "main"
    // Optional command run once inside a freshly-cut worktree after env-copy + npm install
    // (e.g. "npx prisma generate"). Runs from the worktree root. nil → skip.
    var worktreeProvisionCommand: String?
    // Optional subdir appended to the resolved cwd (e.g. "apps/main-app" for monorepos).
    // Also used as the extra .env* source dir when provisioning worktrees.
    var devServerSubdir: String?
    var dedicatedProject: String?
    var permissionMode: String = "bypassPermissions"  // bypassPermissions, auto, manual
    var allowList: [String] = [
        "Bash(git clone:*)",
        "Bash(npm install:*)",
        "Bash(npm run:*)",
        "Bash(npx prisma:*)",
        "Bash(npx eslint:*)",
        "Bash(node_modules/.bin/prisma generate:*)",
        "Bash(node_modules/.bin/tsc --noEmit)",
        "Bash(npx dotenv-cli:*)",
        "Bash(npx dotenv:*)",
        "Bash(node -e \":*)",
        "Skill(update-config)",
        "Skill(update-config:*)"
    ]

    // ── Monday.com integration (optional, off by default) ────────
    // Master switch for any Monday surface. Off by default so the Mac port ships
    // with no Monday integration at all. (The Mac app currently has no Monday
    // panel/overlay UI; this field exists for config parity with Windows and to
    // gate such UI if/when it is ported.)
    var mondayEnabled: Bool = false

    // MARK: - Storage Paths

    static let appSupportDir: URL = {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first!
        return base.appendingPathComponent("QuadClaude")
    }()

    static let configURL: URL = appSupportDir.appendingPathComponent("config.json")

    static var exists: Bool {
        FileManager.default.fileExists(atPath: configURL.path)
    }

    // MARK: - Load / Save

    static func load() -> QuadConfig? {
        guard FileManager.default.fileExists(atPath: configURL.path) else { return nil }
        do {
            let data = try Data(contentsOf: configURL)
            let decoder = JSONDecoder()
            decoder.keyDecodingStrategy = .convertFromSnakeCase
            return try decoder.decode(QuadConfig.self, from: data)
        } catch {
            print("QuadConfig: Failed to load config: \(error)")
            return nil
        }
    }

    static func loadOrDefault() -> QuadConfig {
        return load() ?? QuadConfig()
    }

    func save() throws {
        let fm = FileManager.default
        if !fm.fileExists(atPath: QuadConfig.appSupportDir.path) {
            try fm.createDirectory(at: QuadConfig.appSupportDir, withIntermediateDirectories: true)
        }

        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        encoder.keyEncodingStrategy = .convertToSnakeCase
        let data = try encoder.encode(self)

        // Atomic write: write to tmp then move
        let tmpURL = QuadConfig.configURL.appendingPathExtension("tmp")
        try data.write(to: tmpURL, options: .atomic)
        if fm.fileExists(atPath: QuadConfig.configURL.path) {
            try fm.removeItem(at: QuadConfig.configURL)
        }
        try fm.moveItem(at: tmpURL, to: QuadConfig.configURL)
    }

    // MARK: - Quad State Files

    static func quadStateURL(index: Int) -> URL {
        return appSupportDir.appendingPathComponent("quad-\(index).cwd.json")
    }

    static func quadWindowIdURL(index: Int) -> URL {
        return appSupportDir.appendingPathComponent("quad-\(index).windowid")
    }

    static func statusStateURL(index: Int) -> URL {
        return appSupportDir.appendingPathComponent("status-quad-\(index).json")
    }
}
