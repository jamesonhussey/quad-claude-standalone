import Foundation

/// Writes current working directory and git branch to the quad's state file.
/// This runs in the CLI shim (no GUI needed).
enum TrackCommand {
    static func execute() -> Int32 {
        let quadIndex = ProcessInfo.processInfo.environment["QUAD_INDEX"] ?? "0"

        let dir = QuadConfig.appSupportDir
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)

        let cwd = FileManager.default.currentDirectoryPath
        let project = (cwd as NSString).lastPathComponent
        let branch = getGitBranch(dir: cwd)

        let state: [String: String] = [
            "cwd": cwd,
            "project": project,
            "branch": branch
        ]

        guard let jsonData = try? JSONSerialization.data(withJSONObject: state),
              let json = String(data: jsonData, encoding: .utf8) else {
            return 1
        }

        let stateURL = dir.appendingPathComponent("quad-\(quadIndex).cwd.json")
        let tmpURL = stateURL.appendingPathExtension("tmp")

        do {
            try json.write(to: tmpURL, atomically: false, encoding: .utf8)
            if FileManager.default.fileExists(atPath: stateURL.path) {
                try FileManager.default.removeItem(at: stateURL)
            }
            try FileManager.default.moveItem(at: tmpURL, to: stateURL)
        } catch {
            // Fallback: write directly
            try? json.write(to: stateURL, atomically: true, encoding: .utf8)
        }

        return 0
    }

    // MARK: - Git Branch Detection

    private static func getGitBranch(dir: String) -> String {
        // Fast path: read .git/HEAD directly
        if let gitDir = findGitDir(from: dir) {
            let headPath = (gitDir as NSString).appendingPathComponent("HEAD")
            if let head = try? String(contentsOfFile: headPath, encoding: .utf8).trimmingCharacters(in: .whitespacesAndNewlines) {
                if head.hasPrefix("ref: refs/heads/") {
                    return String(head.dropFirst("ref: refs/heads/".count))
                }
                if head.count >= 7 {
                    return String(head.prefix(7))
                }
            }
        }

        // Fallback: run git
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
                if isDirectory.boolValue {
                    return gitPath
                } else {
                    // Worktree: .git file contains "gitdir: /path/to/worktree"
                    if let content = try? String(contentsOfFile: gitPath, encoding: .utf8).trimmingCharacters(in: .whitespacesAndNewlines),
                       content.hasPrefix("gitdir: ") {
                        return String(content.dropFirst("gitdir: ".count)).trimmingCharacters(in: .whitespacesAndNewlines)
                    }
                }
            }
            current = (current as NSString).deletingLastPathComponent
        }
        return nil
    }
}
