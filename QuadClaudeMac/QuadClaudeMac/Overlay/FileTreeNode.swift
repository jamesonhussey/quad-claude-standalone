import Foundation

/// Model for a node in the file explorer tree.
class FileTreeNode {
    static let excludedDirs: Set<String> = [
        ".git", "node_modules", "bin", "obj", ".vs", "__pycache__",
        ".next", "dist", "coverage", ".cache", ".nuget", "packages",
        ".terraform", "vendor", "target", "build", ".idea"
    ]

    static func isExcludedDir(_ name: String) -> Bool {
        return excludedDirs.contains(name) || name.hasPrefix(".")
    }

    let name: String
    let fullPath: String
    let isDirectory: Bool
    var children: [FileTreeNode] = []
    var isExpanded = false
    private var isLoaded = false

    var icon: String {
        if isDirectory {
            return isExpanded ? "\u{1F4C2}" : "\u{1F4C1}"
        }
        return Self.fileIcon(for: name)
    }

    init(fullPath: String, isDirectory: Bool) {
        self.fullPath = fullPath
        self.name = (fullPath as NSString).lastPathComponent
        self.isDirectory = isDirectory
    }

    func expand() {
        guard isDirectory, !isLoaded else { return }
        isExpanded = true
        loadChildren()
    }

    func collapse() {
        isExpanded = false
    }

    func loadChildren() {
        guard !isLoaded else { return }
        isLoaded = true
        children.removeAll()

        let fm = FileManager.default
        guard let contents = try? fm.contentsOfDirectory(atPath: fullPath) else { return }

        var dirs: [FileTreeNode] = []
        var files: [FileTreeNode] = []

        for item in contents {
            let itemPath = (fullPath as NSString).appendingPathComponent(item)
            var isDir: ObjCBool = false
            guard fm.fileExists(atPath: itemPath, isDirectory: &isDir) else { continue }

            if isDir.boolValue {
                if Self.isExcludedDir(item) { continue }
                dirs.append(FileTreeNode(fullPath: itemPath, isDirectory: true))
            } else {
                files.append(FileTreeNode(fullPath: itemPath, isDirectory: false))
            }
        }

        // Sort: folders first, then alphabetical
        dirs.sort { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }
        files.sort { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }
        children = dirs + files
    }

    func refresh() {
        guard isDirectory else { return }
        isLoaded = false
        children.removeAll()
        if isExpanded { loadChildren() }
    }

    // MARK: - File Icons

    private static func fileIcon(for name: String) -> String {
        let ext = (name as NSString).pathExtension.lowercased()
        switch ext {
        case "swift":           return "\u{1F426}"  // bird for Swift
        case "cs":              return "\u{2660}"    // spade
        case "ts", "tsx":       return "\u{25C6}"    // diamond
        case "js", "jsx":       return "\u{25CB}"    // circle
        case "json":            return "{ }"
        case "md":              return "\u{00B6}"    // pilcrow
        case "sh", "bash", "zsh": return ">_"
        case "xml", "html":     return "</>"
        case "css", "scss":     return "#"
        case "png", "jpg", "gif", "svg", "ico": return "\u{25A3}"
        case "xcodeproj", "xcworkspace": return "\u{2726}"
        default:                return "\u{2022}"    // bullet
        }
    }

    /// Search for files matching a query within this node's directory
    static func search(in rootPath: String, query: String, maxResults: Int = 50) -> [FileTreeNode] {
        let fm = FileManager.default
        var results: [FileTreeNode] = []

        guard let enumerator = fm.enumerator(
            at: URL(fileURLWithPath: rootPath),
            includingPropertiesForKeys: [.isDirectoryKey],
            options: [.skipsHiddenFiles]
        ) else { return [] }

        while let url = enumerator.nextObject() as? URL {
            guard results.count < maxResults else { break }

            let name = url.lastPathComponent

            // Skip excluded directories
            if let isDir = try? url.resourceValues(forKeys: [.isDirectoryKey]).isDirectory, isDir == true {
                if isExcludedDir(name) {
                    enumerator.skipDescendants()
                    continue
                }
                continue
            }

            // Match file name
            if name.localizedCaseInsensitiveContains(query) {
                results.append(FileTreeNode(fullPath: url.path, isDirectory: false))
            }
        }

        return results
    }
}
