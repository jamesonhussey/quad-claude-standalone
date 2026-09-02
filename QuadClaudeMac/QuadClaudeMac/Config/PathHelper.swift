import Foundation

enum PathHelper {
    static var homeDir: String {
        FileManager.default.homeDirectoryForCurrentUser.path
    }

    static var claudeDir: String {
        (homeDir as NSString).appendingPathComponent(".claude")
    }

    static var claudeSettingsPath: String {
        (claudeDir as NSString).appendingPathComponent("settings.json")
    }

    static var appSupportDir: String {
        QuadConfig.appSupportDir.path
    }

    /// Resolve ~ to home directory
    static func expandTilde(_ path: String) -> String {
        return (path as NSString).expandingTildeInPath
    }
}
