import Cocoa

/// Status widget toolbar that floats at the bottom-right of a terminal window.
/// Shows branch, project, phase, and quad management controls.
class StatusWidgetController {
    private let trackedWindowId: CGWindowID
    private let quadIndex: Int
    private weak var appDelegate: AppDelegate?
    private var window: NSWindow?
    private var trackingTimer: Timer?
    private var cwdPollCounter = 0
    private var lastCwdJson = ""
    private var phaseIndex = 0
    private var sizeMode = "M"
    private var optionsExpanded = false
    private var fileExplorerController: FileExplorerController?
    var isPaused = false

    // UI elements
    private var labelField: NSTextField!
    private var branchLabel: NSTextField!
    private var projectLabel: NSTextField!
    private var phaseDot: NSView!
    private var phaseLabel: NSTextField!
    private var rootView: NSView!
    private var optionsPanel: NSStackView!
    private var quadButtons: [NSButton] = []
    private var sizeBtns: [String: NSButton] = [:]
    private var fifthButton: NSButton!

    private static let phases: [(name: String, color: NSColor)] = [
        ("Active",  NSColor(red: 0, green: 1, blue: 0.53, alpha: 1)),       // #00FF88
        ("Paused",  NSColor(red: 1, green: 0.72, blue: 0.2, alpha: 1)),     // #FFB833
        ("Blocked", NSColor(red: 1, green: 0.27, blue: 0.27, alpha: 1)),    // #FF4444
        ("Idle",    NSColor(red: 0.4, green: 0.4, blue: 0.47, alpha: 1)),   // #666677
    ]

    private static let sizeScales: [String: CGFloat] = ["S": 0.8, "M": 1.0, "L": 1.25]

    init(trackedWindowId: CGWindowID, quadIndex: Int, appDelegate: AppDelegate) {
        self.trackedWindowId = trackedWindowId
        self.quadIndex = quadIndex
        self.appDelegate = appDelegate
    }

    /// The current widget height (compact vs expanded)
    var widgetHeight: CGFloat {
        let scale = Self.sizeScales[sizeMode] ?? 1.0
        let base: CGFloat = optionsExpanded ? 80 : 34
        return base * scale
    }

    /// The current widget width
    var widgetWidth: CGFloat {
        let scale = Self.sizeScales[sizeMode] ?? 1.0
        return 320 * scale
    }

    func show() {
        let win = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 320, height: 34),
            styleMask: .borderless,
            backing: .buffered,
            defer: false
        )
        win.backgroundColor = NSColor(red: 0.12, green: 0.12, blue: 0.18, alpha: 0.87)
        win.isOpaque = false
        win.hasShadow = true
        // Just above normal so toolbars stay visible even when other apps are focused
        win.level = AppDelegate.overlayLevel
        win.collectionBehavior = [.transient, .ignoresCycle]
        win.isReleasedWhenClosed = false
        win.isMovableByWindowBackground = false

        buildUI(in: win)
        self.window = win

        loadState()
        updatePhaseVisual()
        readCwdState()
        positionWidget()

        win.orderFront(nil)

        trackingTimer = Timer.scheduledTimer(withTimeInterval: 0.5, repeats: true) { [weak self] _ in
            self?.onTimerTick()
        }
    }

    func close() {
        trackingTimer?.invalidate()
        trackingTimer = nil
        fileExplorerController?.close()
        fileExplorerController = nil
        window?.close()
        window = nil
    }

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

    // MARK: - UI Construction

    private func buildUI(in win: NSWindow) {
        let contentView = win.contentView!
        contentView.wantsLayer = true
        contentView.layer?.cornerRadius = 6

        // Compact bar (horizontal stack)
        let compactBar = NSStackView()
        compactBar.orientation = .horizontal
        compactBar.spacing = 6
        compactBar.edgeInsets = NSEdgeInsets(top: 4, left: 8, bottom: 4, right: 8)
        compactBar.translatesAutoresizingMaskIntoConstraints = false

        // Label (editable)
        labelField = NSTextField()
        labelField.stringValue = "Quad \(quadIndex + 1)"
        labelField.font = NSFont.monospacedSystemFont(ofSize: 11, weight: .medium)
        labelField.textColor = NSColor(white: 0.9, alpha: 1)
        labelField.backgroundColor = .clear
        labelField.isBordered = false
        labelField.isEditable = true
        labelField.focusRingType = .none
        labelField.setContentHuggingPriority(.defaultLow, for: .horizontal)
        labelField.widthAnchor.constraint(lessThanOrEqualToConstant: 80).isActive = true
        labelField.target = self
        labelField.action = #selector(labelChanged)
        compactBar.addArrangedSubview(labelField)

        // Separator
        let sep1 = makeSeparator()
        compactBar.addArrangedSubview(sep1)

        // Branch/project info (vertical stack)
        let infoStack = NSStackView()
        infoStack.orientation = .vertical
        infoStack.spacing = 0
        infoStack.alignment = .leading

        branchLabel = makeLabel("\u{2387} main", size: 10, color: NSColor(red: 0.4, green: 0.8, blue: 1, alpha: 1))
        projectLabel = makeLabel("project", size: 9, color: NSColor(white: 0.6, alpha: 1))
        infoStack.addArrangedSubview(branchLabel)
        infoStack.addArrangedSubview(projectLabel)
        compactBar.addArrangedSubview(infoStack)

        // Separator
        compactBar.addArrangedSubview(makeSeparator())

        // Phase dot + text
        let phaseStack = NSStackView()
        phaseStack.orientation = .horizontal
        phaseStack.spacing = 4

        phaseDot = NSView(frame: NSRect(x: 0, y: 0, width: 8, height: 8))
        phaseDot.wantsLayer = true
        phaseDot.layer?.cornerRadius = 4
        phaseDot.layer?.backgroundColor = Self.phases[0].color.cgColor
        phaseDot.widthAnchor.constraint(equalToConstant: 8).isActive = true
        phaseDot.heightAnchor.constraint(equalToConstant: 8).isActive = true

        phaseLabel = makeLabel("Active", size: 10, color: NSColor(white: 0.7, alpha: 1))

        phaseStack.addArrangedSubview(phaseDot)
        phaseStack.addArrangedSubview(phaseLabel)
        compactBar.addArrangedSubview(phaseStack)

        // Add click gesture to phase area
        let phaseClick = NSClickGestureRecognizer(target: self, action: #selector(onPhaseClick))
        phaseStack.addGestureRecognizer(phaseClick)

        // Spacer
        let spacer = NSView()
        spacer.setContentHuggingPriority(.defaultLow, for: .horizontal)
        compactBar.addArrangedSubview(spacer)

        // File explorer button
        let explorerBtn = makeIconButton(systemName: "folder", action: #selector(onExplorerClick))
        compactBar.addArrangedSubview(explorerBtn)

        // Gear button
        let gearBtn = makeIconButton(systemName: "gearshape", action: #selector(onGearClick))
        compactBar.addArrangedSubview(gearBtn)

        // Options panel (hidden by default)
        optionsPanel = NSStackView()
        optionsPanel.orientation = .vertical
        optionsPanel.spacing = 6
        optionsPanel.edgeInsets = NSEdgeInsets(top: 4, left: 8, bottom: 6, right: 8)
        optionsPanel.isHidden = true
        optionsPanel.translatesAutoresizingMaskIntoConstraints = false
        buildOptionsPanel()

        // Main vertical stack
        let mainStack = NSStackView()
        mainStack.orientation = .vertical
        mainStack.spacing = 0
        mainStack.translatesAutoresizingMaskIntoConstraints = false
        mainStack.addArrangedSubview(compactBar)
        mainStack.addArrangedSubview(optionsPanel)

        contentView.addSubview(mainStack)
        NSLayoutConstraint.activate([
            mainStack.topAnchor.constraint(equalTo: contentView.topAnchor),
            mainStack.bottomAnchor.constraint(equalTo: contentView.bottomAnchor),
            mainStack.leadingAnchor.constraint(equalTo: contentView.leadingAnchor),
            mainStack.trailingAnchor.constraint(equalTo: contentView.trailingAnchor),
        ])

        rootView = contentView
    }

    private func buildOptionsPanel() {
        // Quad buttons row
        let quadRow = NSStackView()
        quadRow.orientation = .horizontal
        quadRow.spacing = 4

        for i in 0..<4 {
            let btn = NSButton(title: "Q\(i + 1)", target: self, action: #selector(onQuadClick(_:)))
            btn.tag = i
            btn.bezelStyle = .inline
            btn.font = NSFont.monospacedSystemFont(ofSize: 10, weight: .medium)
            btn.isBordered = true
            quadButtons.append(btn)
            quadRow.addArrangedSubview(btn)
        }

        fifthButton = NSButton(title: "+ 5th", target: self, action: #selector(onFifthClick))
        fifthButton.bezelStyle = .inline
        fifthButton.font = NSFont.monospacedSystemFont(ofSize: 10, weight: .medium)
        quadRow.addArrangedSubview(fifthButton)

        optionsPanel.addArrangedSubview(quadRow)

        // Size row
        let sizeRow = NSStackView()
        sizeRow.orientation = .horizontal
        sizeRow.spacing = 4

        for size in ["S", "M", "L"] {
            let btn = NSButton(title: size, target: self, action: #selector(onSizeClick(_:)))
            btn.bezelStyle = .inline
            btn.font = NSFont.monospacedSystemFont(ofSize: 10, weight: .medium)
            btn.identifier = NSUserInterfaceItemIdentifier(size)
            sizeBtns[size] = btn
            sizeRow.addArrangedSubview(btn)
        }

        // Restart / Close buttons
        let restartBtn = NSButton(title: "\u{21BB} Restart", target: self, action: #selector(onRestartClick))
        restartBtn.bezelStyle = .inline
        restartBtn.font = NSFont.monospacedSystemFont(ofSize: 10, weight: .medium)
        sizeRow.addArrangedSubview(restartBtn)

        let closeBtn = NSButton(title: "\u{2715} Close", target: self, action: #selector(onCloseQuadClick))
        closeBtn.bezelStyle = .inline
        closeBtn.font = NSFont.monospacedSystemFont(ofSize: 10, weight: .medium)
        sizeRow.addArrangedSubview(closeBtn)

        optionsPanel.addArrangedSubview(sizeRow)
    }

    // MARK: - Timer

    private func onTimerTick() {
        if !ProcessHelper.windowExists(windowId: trackedWindowId) {
            close()
            appDelegate?.statusControllers.removeValue(forKey: quadIndex)
            return
        }

        if isPaused { return }

        positionWidget()

        cwdPollCounter += 1
        if cwdPollCounter >= 4 {
            cwdPollCounter = 0
            readCwdState()
        }
    }

    // MARK: - Positioning

    private func positionWidget() {
        guard let bounds = ProcessHelper.windowBounds(windowId: trackedWindowId),
              let screen = NSScreen.main,
              let window = window else { return }

        let screenHeight = screen.frame.height

        // CGWindow coords: origin at top-left of screen
        // NSWindow coords: origin at bottom-left of screen
        // Terminal top in NS coords:   screenHeight - bounds.origin.y
        // Terminal bottom in NS coords: screenHeight - bounds.maxY

        let termBottomNS = screenHeight - bounds.maxY
        let termRightNS = bounds.maxX

        // Cap widget width to 45% of terminal width
        let wWidth = min(widgetWidth, bounds.width * 0.45)
        let wHeight = widgetHeight

        // Bottom-right corner of this terminal, inset 8px
        let widgetX = termRightNS - wWidth - 8
        let widgetY = termBottomNS + 8

        window.setFrame(NSRect(x: widgetX, y: widgetY, width: wWidth, height: wHeight), display: true)
    }

    // MARK: - Branch / CWD Tracking

    private func readCwdState() {
        let url = QuadConfig.quadStateURL(index: quadIndex)
        guard let data = try? Data(contentsOf: url),
              let json = String(data: data, encoding: .utf8),
              json != lastCwdJson else { return }

        lastCwdJson = json

        guard let state = try? JSONSerialization.jsonObject(with: data) as? [String: String] else { return }

        let branch = state["branch"] ?? ""
        let cwd = state["cwd"] ?? ""
        let project = state["project"] ?? ""

        branchLabel.stringValue = "\u{2387} \(branch.isEmpty ? "(no branch)" : branch)"

        if !cwd.isEmpty, cwd != project {
            let parts = cwd.split(separator: "/")
            let shortDir = parts.count >= 2
                ? "\(parts[parts.count - 2])/\(parts[parts.count - 1])"
                : String(parts.last ?? "")
            projectLabel.stringValue = shortDir
            projectLabel.toolTip = cwd
        } else {
            projectLabel.stringValue = project
        }
    }

    // MARK: - Phase

    @objc private func onPhaseClick() {
        phaseIndex = (phaseIndex + 1) % Self.phases.count
        updatePhaseVisual()
        saveState()
    }

    private func updatePhaseVisual() {
        let phase = Self.phases[phaseIndex]
        phaseLabel.stringValue = phase.name
        phaseDot.layer?.backgroundColor = phase.color.cgColor

        // Tint background
        let baseColor = NSColor(red: 0.12, green: 0.12, blue: 0.18, alpha: 1)
        let phaseColor = phase.color
        let tinted = NSColor(
            red: baseColor.redComponent + (phaseColor.redComponent - baseColor.redComponent) * 0.15,
            green: baseColor.greenComponent + (phaseColor.greenComponent - baseColor.greenComponent) * 0.15,
            blue: baseColor.blueComponent + (phaseColor.blueComponent - baseColor.blueComponent) * 0.15,
            alpha: 0.87
        )
        rootView?.layer?.backgroundColor = tinted.cgColor
    }

    // MARK: - Actions

    @objc private func onExplorerClick() {
        if fileExplorerController != nil {
            fileExplorerController?.close()
            fileExplorerController = nil
            return
        }

        // Get root path from cwd state
        guard !lastCwdJson.isEmpty,
              let data = lastCwdJson.data(using: .utf8),
              let state = try? JSONSerialization.jsonObject(with: data) as? [String: String],
              let rootPath = state["cwd"], !rootPath.isEmpty,
              FileManager.default.fileExists(atPath: rootPath) else { return }

        fileExplorerController = FileExplorerController(
            trackedWindowId: trackedWindowId,
            quadIndex: quadIndex,
            rootPath: rootPath,
            statusWidget: self
        )
        fileExplorerController?.show()
    }

    @objc private func onGearClick() {
        optionsExpanded.toggle()
        optionsPanel.isHidden = !optionsExpanded

        if optionsExpanded {
            updateQuadButtonStates()
        }

        // Resize window to fit
        window?.invalidateShadow()
        DispatchQueue.main.async { [weak self] in
            self?.positionWidget()
        }
    }

    @objc private func onQuadClick(_ sender: NSButton) {
        let targetQuad = sender.tag
        guard !TerminalManager.isTerminalAlive(forQuad: targetQuad) else { return }

        let config = QuadConfig.loadOrDefault()
        DispatchQueue.global(qos: .userInitiated).async { [weak self] in
            // Launch single quad at the target position
            let windowIds = TerminalManager.launchQuadGrid(config: config)
            if let windowId = windowIds.first, let delegate = self?.appDelegate {
                DispatchQueue.main.async {
                    StatusCommand.execute(appDelegate: delegate, quadIndex: targetQuad, windowId: windowId)
                }
            }
        }
    }

    @objc private func onFifthClick() {
        // Find next available overflow slot
        var slot = 5
        while slot < 100 {
            if !TerminalManager.isTerminalAlive(forQuad: slot) { break }
            slot += 1
        }

        let capturedSlot = slot
        let config = QuadConfig.loadOrDefault()
        let launchScript: String
        let macScript = (config.setupDir as NSString).appendingPathComponent("QuadClaudeMac/Scripts/claude-launch-mac.sh")
        if FileManager.default.fileExists(atPath: macScript) {
            launchScript = macScript
        } else {
            launchScript = (config.setupDir as NSString).appendingPathComponent("claude-launch.sh")
        }

        // Snapshot existing windows
        let existingWindows: [CGWindowID]
        if let pid = ProcessHelper.terminalAppPID() {
            existingWindows = TerminalManager.getTerminalWindowIds(pid: pid)
        } else {
            existingWindows = []
        }

        _ = AppleScriptBridge.openTerminalWindow(command: "export QUAD_INDEX=\(capturedSlot); source '\(launchScript)'")

        // Find the new window after a delay and create a status widget
        DispatchQueue.global(qos: .userInitiated).async { [weak self] in
            Thread.sleep(forTimeInterval: 2.0)
            guard let pid = ProcessHelper.terminalAppPID() else { return }
            let currentWindows = TerminalManager.getTerminalWindowIds(pid: pid)
            let newWindows = currentWindows.filter { !existingWindows.contains($0) }

            if let newWindowId = newWindows.first {
                TerminalManager.saveWindowId(newWindowId, forQuad: capturedSlot)
                DispatchQueue.main.async {
                    guard let delegate = self?.appDelegate else { return }
                    StatusCommand.execute(appDelegate: delegate, quadIndex: capturedSlot, windowId: newWindowId)
                }
            }
        }
    }

    @objc private func onSizeClick(_ sender: NSButton) {
        let size = sender.identifier?.rawValue ?? "M"
        sizeMode = size
        applySizeMode()
        saveState()
        positionWidget()
    }

    private func applySizeMode() {
        let scale = Self.sizeScales[sizeMode] ?? 1.0

        // Scale fonts
        labelField?.font = NSFont.monospacedSystemFont(ofSize: 11 * scale, weight: .medium)
        branchLabel?.font = NSFont.monospacedSystemFont(ofSize: 10 * scale, weight: .regular)
        projectLabel?.font = NSFont.monospacedSystemFont(ofSize: 9 * scale, weight: .regular)
        phaseLabel?.font = NSFont.monospacedSystemFont(ofSize: 10 * scale, weight: .regular)

        // Highlight active size button
        for (key, btn) in sizeBtns {
            btn.contentTintColor = key == sizeMode
                ? NSColor(red: 0, green: 1, blue: 0.53, alpha: 1)
                : NSColor(white: 0.6, alpha: 1)
        }

        // positionWidget will set the correct frame size based on widgetWidth/widgetHeight
    }

    @objc private func onRestartClick() {
        // Close terminal via AppleScript, then relaunch
        if let pid = ProcessHelper.terminalAppPID(),
           let axWindow = AccessibilityBridge.findTerminalWindow(matchingWindowId: trackedWindowId, terminalPID: pid) {
            AXUIElementPerformAction(axWindow, kAXRaiseAction as CFString)
        }

        // For now, just notify user to close and relaunch manually
        // Full implementation would kill + relaunch
    }

    @objc private func onCloseQuadClick() {
        // Send close to Terminal window
        if let pid = ProcessHelper.terminalAppPID() {
            let script = """
            tell application "Terminal"
                close (every window whose id is \(trackedWindowId))
            end tell
            """
            let appleScript = NSAppleScript(source: script)
            appleScript?.executeAndReturnError(nil)
        }
    }

    @objc private func labelChanged() {
        saveState()
    }

    // MARK: - Quad Button States

    private func updateQuadButtonStates() {
        for (i, btn) in quadButtons.enumerated() {
            let alive = TerminalManager.isTerminalAlive(forQuad: i)
            btn.isEnabled = !alive
            if alive {
                btn.contentTintColor = NSColor(white: 0.4, alpha: 1)
            } else {
                btn.contentTintColor = NSColor(red: 0, green: 1, blue: 0.53, alpha: 1)
            }
        }
    }

    // MARK: - Persistence

    private func loadState() {
        let url = QuadConfig.statusStateURL(index: quadIndex)
        guard let data = try? Data(contentsOf: url),
              let state = try? JSONSerialization.jsonObject(with: data) as? [String: String] else { return }

        if let label = state["label"] { labelField?.stringValue = label }
        if let phase = state["phase"] {
            phaseIndex = Self.phases.firstIndex(where: { $0.name == phase }) ?? 0
        }
        if let size = state["size"], Self.sizeScales[size] != nil {
            sizeMode = size
        }
    }

    private func saveState() {
        let state: [String: String] = [
            "label": labelField?.stringValue ?? "",
            "phase": Self.phases[phaseIndex].name,
            "size": sizeMode
        ]
        if let data = try? JSONSerialization.data(withJSONObject: state, options: .prettyPrinted) {
            try? data.write(to: QuadConfig.statusStateURL(index: quadIndex), options: .atomic)
        }
    }

    // MARK: - Helpers

    private func makeLabel(_ text: String, size: CGFloat, color: NSColor) -> NSTextField {
        let label = NSTextField(labelWithString: text)
        label.font = NSFont.monospacedSystemFont(ofSize: size, weight: .regular)
        label.textColor = color
        label.backgroundColor = .clear
        label.isBordered = false
        label.isEditable = false
        return label
    }

    private func makeSeparator() -> NSView {
        let sep = NSView(frame: NSRect(x: 0, y: 0, width: 1, height: 20))
        sep.wantsLayer = true
        sep.layer?.backgroundColor = NSColor(white: 1, alpha: 0.15).cgColor
        sep.widthAnchor.constraint(equalToConstant: 1).isActive = true
        sep.heightAnchor.constraint(equalToConstant: 20).isActive = true
        return sep
    }

    private func makeIconButton(systemName: String, action: Selector) -> NSButton {
        let btn: NSButton
        if #available(macOS 13.0, *) {
            btn = NSButton(image: NSImage(systemSymbolName: systemName, accessibilityDescription: nil)!, target: self, action: action)
        } else {
            btn = NSButton(title: systemName, target: self, action: action)
        }
        btn.bezelStyle = .inline
        btn.isBordered = false
        btn.imageScaling = .scaleProportionallyDown
        btn.contentTintColor = NSColor(white: 0.7, alpha: 1)
        return btn
    }
}
