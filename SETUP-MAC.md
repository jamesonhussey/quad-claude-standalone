# QuadClaude for macOS

> ## ⚠️ EXPERIMENTAL — UNTESTED
>
> The macOS port (`QuadClaudeMac/`) is a **work in progress** and is **not yet
> verified on a real machine**. It is developed on Windows by code review only —
> it has **not been compiled or run** through Xcode. Treat every step below as a
> best-effort guide, expect rough edges, and read the source before relying on it.
> The mature, tested QuadClaude is the **Windows** app (`QuadClaude/`).

QuadClaude launches four Claude Code terminals in a 2×2 grid, each with a
glow/sound cue and (in the worktrees layout) its own git worktree. The macOS
port is native Swift/AppKit and targets **macOS 13+**.

---

## What builds

`QuadClaudeMac/project.yml` (XcodeGen) declares two targets:

| Target | Type | What it is |
|--------|------|------------|
| `QuadClaudeMac` | app (`LSUIElement`, background) | Hosts the overlay windows (glow, status widget) and listens on a Unix socket for CLI commands. |
| `quadclaude` | command-line tool | The CLI shim you actually run (`quadclaude setup`, `launch`, `glow`, …). It talks to the app over the socket, and runs `setup`/`track` itself. |

The two are separate targets and do **not** share a module, so a little logic is
duplicated between the app and the CLI (this is expected).

---

## Prerequisites

- **Xcode** (with Command Line Tools) — provides `xcodebuild`.
- **XcodeGen** — generates the `.xcodeproj` from `project.yml`:
  ```sh
  brew install xcodegen
  ```
- **Claude Code CLI** — QuadClaude launches it; install/verify separately:
  ```sh
  npm install -g @anthropic-ai/claude-code
  claude --version
  ```

---

## Build

```sh
cd QuadClaudeMac

# 1. Generate the Xcode project from project.yml
xcodegen generate

# 2. Build both targets (Release). SYMROOT=build places products under
#    ./build/Release/, which is where the CLI's dev-mode auto-launch looks.
xcodebuild -project QuadClaudeMac.xcodeproj -scheme QuadClaudeMac \
    -configuration Release SYMROOT=build build
xcodebuild -project QuadClaudeMac.xcodeproj -scheme quadclaude \
    -configuration Release SYMROOT=build build
```

Or open `QuadClaudeMac.xcodeproj` in Xcode and build the `QuadClaudeMac` and
`quadclaude` schemes.

After a Release build you should have:

- `QuadClaudeMac/build/Release/QuadClaudeMac.app` — the background app
- `QuadClaudeMac/build/Release/quadclaude` — the CLI tool

> Scheme/product paths depend on your Xcode version and settings. If
> `SYMROOT=build` doesn't land things in `build/Release/`, check
> `build/Build/Products/Release/` instead (XcodeGen's default derived layout).

---

## Install

### The app

The CLI auto-launches the app when it finds it in one of these locations
(see `QuadClaudeMac/quadclaude/main.swift` → `launchApp()`):

- `/Applications/QuadClaudeMac.app`
- `~/Applications/QuadClaudeMac.app`
- `~/quad-claude-standalone/QuadClaudeMac/build/{Release,Debug}/QuadClaudeMac.app`

Copying it to `/Applications` is the most reliable:

```sh
cp -R build/Release/QuadClaudeMac.app /Applications/
```

### The `quadclaude` CLI on your PATH

So `quadclaude setup` / `quadclaude launch` and the onboarding `[s]` action work
from any shell, symlink the built tool onto your PATH:

```sh
# /usr/local/bin (may need sudo), or ~/.local/bin if that's on your PATH
ln -sf "$(pwd)/build/Release/quadclaude" /usr/local/bin/quadclaude
quadclaude            # should print usage
```

The onboarding checklist locates the CLI via `command -v quadclaude`, so this
symlink is what makes its `[s]` (run setup) action work.

---

## Configure

```sh
quadclaude setup
```

The CLI wizard (in `main.swift`) currently asks for:

1. **Projects directory**
2. **Layout** — `multi-project` (each quad picks any project) or `worktrees`
   (one base repo + a git worktree per quad). Choosing `worktrees` also asks for
   the **base repo name** and the **base branch** (default `main`) each worktree
   resets to on open.
3. **Permission mode** — `bypassPermissions` / `auto` / `manual`
4. **Sounds**

It writes:

- `~/Library/Application Support/QuadClaude/config.json`
- Claude Code hooks into `~/.claude/settings.json` (glow + track + optional sound)

At **launch** time (`quadclaude launch`), the app writes
`~/Library/Application Support/QuadClaude/launch-env.sh`, exporting the shared
contract the launch script reads: `QUADCLAUDE_PROJECTS_DIR`, `QUADCLAUDE_LAYOUT`,
`QUADCLAUDE_WORKTREE_BASE`, `QUADCLAUDE_WORKTREE_PATTERN`,
`QUADCLAUDE_WORKTREE_BASE_BRANCH`, and (when configured) `QUADCLAUDE_WORKTREE_SUBDIR`,
`QUADCLAUDE_PROVISION_CMD`, `QUADCLAUDE_TARGET_DIR`, plus `QUADCLAUDE_LABELS`.

### Known setup gaps (macOS)

- The **CLI `setup` is intentionally minimal**. A fuller wizard exists in the app
  target (`QuadClaudeMac/QuadClaudeMac/Commands/SetupCommand.swift`, with terminal
  profile, dedicated-roles labels, worktree base-branch prompt, and opt-in helper-
  command install), but it is **not currently wired to the `quadclaude setup`
  command** — the two targets don't share code. Until it's wired, the CLI wizard is
  what runs.
- To use advanced fields not covered by the CLI wizard (e.g.
  `worktree_provision_command`, `dev_server_subdir`, dedicated-roles labels), edit
  `~/Library/Application Support/QuadClaude/config.json` by hand. Keys are
  `snake_case` (e.g. `worktree_base`, `worktree_base_branch`, `worktree_pattern`,
  `worktree_provision_command`, `dev_server_subdir`).
- **Monday.com integration is not ported.** `mondayEnabled` defaults to `false`
  and there is no Monday UI in the Mac app.

---

## Run

```sh
quadclaude launch
```

This opens four Terminal.app windows in a 2×2 grid, each running
`QuadClaudeMac/Scripts/claude-launch-mac.sh` (zsh). That script:

- runs the onboarding checklist (via the shared `bash onboarding.sh`) when a quad
  isn't fully set up;
- in the `worktrees` layout, opens **each quad's own worktree**, resets it to a
  fresh copy of the base branch (`git fetch` + `git checkout --detach`), and
  best-effort provisions it (copy `.env*`, `npm install`, optional provision cmd);
- otherwise shows a project picker.

macOS will prompt for **Accessibility permission** the first time (needed to
position terminal windows): System Settings → Privacy & Security → Accessibility →
add `QuadClaudeMac.app`.

Other CLI commands: `quadclaude glow --color {green|red|yellow}`,
`quadclaude kill-glow`, `quadclaude track`, `quadclaude quit`.

---

## Cross-platform bits (work regardless of the app)

- The bundled **helper slash-commands** in `.claude/commands/*.md` and the
  **`CLAUDE.md`** push-workflow template are plain Claude Code files — they work in
  any Claude Code session on macOS whether or not you build the app. Copy the
  commands into `~/.claude/commands/` to use them everywhere (the Windows setup and
  the app-target wizard do this for you; the minimal CLI wizard does not yet).
- Hooks live in `~/.claude/settings.json` and are honored by Claude Code directly.
