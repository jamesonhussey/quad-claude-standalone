import Cocoa

/// File explorer side panel that docks to the right of a terminal window.
/// Shows a tree view of the project directory with search, context menus, and drag-drop.
class FileExplorerController: NSObject, NSOutlineViewDataSource, NSOutlineViewDelegate {
    private let trackedWindowId: CGWindowID
    private let quadIndex: Int
    weak var statusWidget: StatusWidgetController?
    private var rootPath: String
    private let projectRoot: String  // ceiling for "go up"
    private var window: NSWindow?
    private var outlineView: NSOutlineView!
    private var searchField: NSTextField!
    private var headerLabel: NSTextField!
    private var trackingTimer: Timer?
    private var rootNodes: [FileTreeNode] = []
    private var cwdPollCounter = 0
    private var lastCwdJson = ""
    private var searchDebounceTimer: Timer?

    init(trackedWindowId: CGWindowID, quadIndex: Int, rootPath: String, statusWidget: StatusWidgetController?) {
        self.trackedWindowId = trackedWindowId
        self.quadIndex = quadIndex
        self.rootPath = rootPath
        self.statusWidget = statusWidget
        self.projectRoot = (rootPath as NSString).deletingLastPathComponent
        super.init()
    }

    func show() {
        let win = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 250, height: 400),
            styleMask: .borderless,
            backing: .buffered,
            defer: false
        )
        win.backgroundColor = NSColor(red: 0.1, green: 0.1, blue: 0.15, alpha: 0.95)
        win.isOpaque = false
        win.hasShadow = true
        // Just above normal so file explorer stays visible
        win.level = AppDelegate.overlayLevel
        win.collectionBehavior = [.transient, .ignoresCycle]
        win.isReleasedWhenClosed = false

        buildUI(in: win)
        self.window = win

        loadTree(rootPath)
        positionPanel()

        win.orderFront(nil)

        trackingTimer = Timer.scheduledTimer(withTimeInterval: 0.5, repeats: true) { [weak self] _ in
            self?.onTimerTick()
        }
    }

    func close() {
        trackingTimer?.invalidate()
        trackingTimer = nil
        searchDebounceTimer?.invalidate()
        window?.close()
        window = nil
    }

    var isPaused = false

    func showWindow() {
        isPaused = false
        window?.orderFront(nil)
    }

    func hideWindow() {
        isPaused = true
        window?.orderOut(nil)
    }

    func setLevel(_ level: NSWindow.Level) {
        window?.level = level
    }

    // MARK: - UI

    private func buildUI(in win: NSWindow) {
        let contentView = win.contentView!
        contentView.wantsLayer = true
        contentView.layer?.cornerRadius = 6

        let mainStack = NSStackView()
        mainStack.orientation = .vertical
        mainStack.spacing = 4
        mainStack.edgeInsets = NSEdgeInsets(top: 8, left: 8, bottom: 8, right: 8)
        mainStack.translatesAutoresizingMaskIntoConstraints = false

        // Header row
        let headerRow = NSStackView()
        headerRow.orientation = .horizontal
        headerRow.spacing = 6

        let upButton = NSButton(title: "\u{2191}", target: self, action: #selector(onGoUp))
        upButton.bezelStyle = .inline
        upButton.font = NSFont.systemFont(ofSize: 12)
        headerRow.addArrangedSubview(upButton)

        headerLabel = NSTextField(labelWithString: (rootPath as NSString).lastPathComponent)
        headerLabel.font = NSFont.monospacedSystemFont(ofSize: 11, weight: .medium)
        headerLabel.textColor = NSColor(white: 0.85, alpha: 1)
        headerLabel.lineBreakMode = .byTruncatingMiddle
        headerRow.addArrangedSubview(headerLabel)

        let spacer = NSView()
        spacer.setContentHuggingPriority(.defaultLow, for: .horizontal)
        headerRow.addArrangedSubview(spacer)

        let closeButton = NSButton(title: "\u{2715}", target: self, action: #selector(onClose))
        closeButton.bezelStyle = .inline
        closeButton.font = NSFont.systemFont(ofSize: 11)
        headerRow.addArrangedSubview(closeButton)

        mainStack.addArrangedSubview(headerRow)

        // Search field
        searchField = NSTextField()
        searchField.placeholderString = "Search files..."
        searchField.font = NSFont.monospacedSystemFont(ofSize: 11, weight: .regular)
        searchField.textColor = NSColor(white: 0.9, alpha: 1)
        searchField.backgroundColor = NSColor(white: 0.15, alpha: 1)
        searchField.isBordered = true
        searchField.focusRingType = .none
        searchField.target = self
        searchField.action = #selector(onSearchChanged)
        mainStack.addArrangedSubview(searchField)

        // Outline view (tree)
        let scrollView = NSScrollView()
        scrollView.hasVerticalScroller = true
        scrollView.autohidesScrollers = true
        scrollView.borderType = .noBorder
        scrollView.drawsBackground = false
        scrollView.translatesAutoresizingMaskIntoConstraints = false

        outlineView = NSOutlineView()
        outlineView.headerView = nil
        outlineView.backgroundColor = .clear
        outlineView.selectionHighlightStyle = .sourceList
        outlineView.rowHeight = 20
        outlineView.dataSource = self
        outlineView.delegate = self
        outlineView.doubleAction = #selector(onDoubleClick)
        outlineView.target = self

        let column = NSTableColumn(identifier: NSUserInterfaceItemIdentifier("FileColumn"))
        column.resizingMask = .autoresizingMask
        outlineView.addTableColumn(column)
        outlineView.outlineTableColumn = column

        scrollView.documentView = outlineView
        mainStack.addArrangedSubview(scrollView)

        contentView.addSubview(mainStack)
        NSLayoutConstraint.activate([
            mainStack.topAnchor.constraint(equalTo: contentView.topAnchor),
            mainStack.bottomAnchor.constraint(equalTo: contentView.bottomAnchor),
            mainStack.leadingAnchor.constraint(equalTo: contentView.leadingAnchor),
            mainStack.trailingAnchor.constraint(equalTo: contentView.trailingAnchor),
        ])
    }

    // MARK: - Tree Loading

    private func loadTree(_ path: String) {
        rootPath = path
        rootNodes.removeAll()

        guard FileManager.default.fileExists(atPath: path) else { return }

        let rootNode = FileTreeNode(fullPath: path, isDirectory: true)
        rootNode.expand()
        rootNodes = [rootNode]

        outlineView?.reloadData()
        if !rootNodes.isEmpty {
            outlineView?.expandItem(rootNodes[0])
        }
    }

    // MARK: - NSOutlineViewDataSource

    func outlineView(_ outlineView: NSOutlineView, numberOfChildrenOfItem item: Any?) -> Int {
        if let node = item as? FileTreeNode {
            return node.children.count
        }
        return rootNodes.count
    }

    func outlineView(_ outlineView: NSOutlineView, child index: Int, ofItem item: Any?) -> Any {
        if let node = item as? FileTreeNode {
            return node.children[index]
        }
        return rootNodes[index]
    }

    func outlineView(_ outlineView: NSOutlineView, isItemExpandable item: Any) -> Bool {
        return (item as? FileTreeNode)?.isDirectory ?? false
    }

    // MARK: - NSOutlineViewDelegate

    func outlineView(_ outlineView: NSOutlineView, viewFor tableColumn: NSTableColumn?, item: Any) -> NSView? {
        guard let node = item as? FileTreeNode else { return nil }

        let cellId = NSUserInterfaceItemIdentifier("FileCell")
        let cell: NSTableCellView

        if let existing = outlineView.makeView(withIdentifier: cellId, owner: self) as? NSTableCellView {
            cell = existing
        } else {
            cell = NSTableCellView()
            cell.identifier = cellId

            let textField = NSTextField(labelWithString: "")
            textField.font = NSFont.monospacedSystemFont(ofSize: 11, weight: .regular)
            textField.translatesAutoresizingMaskIntoConstraints = false
            cell.addSubview(textField)
            cell.textField = textField

            NSLayoutConstraint.activate([
                textField.leadingAnchor.constraint(equalTo: cell.leadingAnchor, constant: 2),
                textField.trailingAnchor.constraint(equalTo: cell.trailingAnchor, constant: -2),
                textField.centerYAnchor.constraint(equalTo: cell.centerYAnchor),
            ])
        }

        cell.textField?.stringValue = "\(node.icon) \(node.name)"
        cell.textField?.textColor = node.isDirectory
            ? NSColor(red: 0.87, green: 0.67, blue: 0.27, alpha: 1)  // warm folder color
            : NSColor(red: 0.6, green: 0.6, blue: 0.67, alpha: 1)    // muted file color
        cell.textField?.toolTip = node.fullPath

        return cell
    }

    func outlineViewItemWillExpand(_ notification: Notification) {
        if let node = notification.userInfo?["NSObject"] as? FileTreeNode {
            node.expand()
            outlineView.reloadItem(node, reloadChildren: true)
        }
    }

    // MARK: - Actions

    @objc private func onGoUp() {
        let parent = (rootPath as NSString).deletingLastPathComponent
        guard parent.count >= projectRoot.count else { return }
        headerLabel.stringValue = (parent as NSString).lastPathComponent
        headerLabel.toolTip = parent
        loadTree(parent)
    }

    @objc private func onClose() {
        close()
    }

    @objc private func onDoubleClick() {
        guard let node = outlineView.item(atRow: outlineView.clickedRow) as? FileTreeNode,
              !node.isDirectory else { return }
        NSWorkspace.shared.open(URL(fileURLWithPath: node.fullPath))
    }

    @objc private func onSearchChanged() {
        searchDebounceTimer?.invalidate()
        searchDebounceTimer = Timer.scheduledTimer(withTimeInterval: 0.3, repeats: false) { [weak self] _ in
            self?.applySearch()
        }
    }

    private func applySearch() {
        let query = searchField.stringValue.trimmingCharacters(in: .whitespaces)
        if query.isEmpty {
            loadTree(rootPath)
            return
        }

        rootNodes = FileTreeNode.search(in: rootPath, query: query)
        outlineView.reloadData()
    }

    // MARK: - Timer / Positioning

    private func onTimerTick() {
        if !ProcessHelper.windowExists(windowId: trackedWindowId) {
            close()
            return
        }
        if isPaused { return }
        positionPanel()

        cwdPollCounter += 1
        if cwdPollCounter >= 4 {
            cwdPollCounter = 0
            checkCwdChange()
        }
    }

    private func positionPanel() {
        guard let bounds = ProcessHelper.windowBounds(windowId: trackedWindowId),
              let screen = NSScreen.main,
              let window = window else { return }

        let screenHeight = screen.frame.height

        // CGWindow coords: top-left origin. NSWindow: bottom-left origin.
        let termTopNS = screenHeight - bounds.origin.y
        let termBottomNS = screenHeight - bounds.maxY
        let termRightNS = bounds.maxX

        // Reserve space at bottom for the status widget toolbar
        let toolbarReserve: CGFloat = (statusWidget?.widgetHeight ?? 34) + 16

        let panelWidth: CGFloat = 250
        let panelHeight = (termTopNS - termBottomNS) - toolbarReserve
        let panelY = termBottomNS + toolbarReserve  // Starts above the toolbar

        window.setFrame(NSRect(
            x: termRightNS - panelWidth,
            y: panelY,
            width: panelWidth,
            height: max(100, panelHeight)
        ), display: true)
    }

    private func checkCwdChange() {
        let url = QuadConfig.quadStateURL(index: quadIndex)
        guard let data = try? Data(contentsOf: url),
              let json = String(data: data, encoding: .utf8),
              json != lastCwdJson else { return }

        lastCwdJson = json

        guard let state = try? JSONSerialization.jsonObject(with: data) as? [String: String],
              let newCwd = state["cwd"], !newCwd.isEmpty else { return }

        // Only auto-switch if it's a completely different project
        if !newCwd.hasPrefix(rootPath), !rootPath.hasPrefix(newCwd),
           FileManager.default.fileExists(atPath: newCwd) {
            headerLabel.stringValue = (newCwd as NSString).lastPathComponent
            headerLabel.toolTip = newCwd
            loadTree(newCwd)
        }
    }
}
