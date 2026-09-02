#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# QuadClaude for macOS — best-effort installer.
#
#   ⚠ EXPERIMENTAL / UNTESTED. The macOS port has not been built or run on a real
#   machine; this script was written by code review only. It may not work as-is.
#   Read it before running, and see SETUP-MAC.md for the manual steps it automates.
#
# What it does (each step guarded — a failure warns but the script keeps going
# where it safely can):
#   1. Verify Xcode / xcodebuild and XcodeGen (offer `brew install xcodegen`).
#   2. `xcodegen generate` in QuadClaudeMac/.
#   3. Build the QuadClaudeMac app + quadclaude CLI (Release).
#   4. Symlink the quadclaude CLI onto your PATH.
#   5. Run `quadclaude setup`.
# ─────────────────────────────────────────────────────────────────────────────
set -u

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MAC_DIR="$REPO_ROOT/QuadClaudeMac"

echo ""
echo "╔════════════════════════════════════════════════════════╗"
echo "║  QuadClaude macOS installer — EXPERIMENTAL / UNTESTED   ║"
echo "╚════════════════════════════════════════════════════════╝"
echo ""
echo "This is an untested, best-effort installer for the macOS port."
printf "Continue? [y/N]: "
read -r _go
case "$_go" in
    y|Y) ;;
    *) echo "Aborted."; exit 0 ;;
esac

if [ ! -d "$MAC_DIR" ]; then
    echo "✗ Can't find QuadClaudeMac/ at: $MAC_DIR"
    exit 1
fi

# ── 1. Prerequisites ─────────────────────────────────────────────
echo ""
echo "→ Checking prerequisites…"

if ! command -v xcodebuild >/dev/null 2>&1; then
    echo "  ✗ xcodebuild not found. Install Xcode (App Store) + Command Line Tools:"
    echo "      xcode-select --install"
    exit 1
fi
echo "  ✓ xcodebuild found"

if ! command -v xcodegen >/dev/null 2>&1; then
    echo "  ✗ xcodegen not found."
    if command -v brew >/dev/null 2>&1; then
        printf "  Install it now with 'brew install xcodegen'? [y/N]: "
        read -r _xg
        case "$_xg" in
            y|Y) brew install xcodegen || { echo "  ✗ brew install failed."; exit 1; } ;;
            *) echo "  Install xcodegen and re-run: brew install xcodegen"; exit 1 ;;
        esac
    else
        echo "  Homebrew not found. Install xcodegen manually, then re-run:"
        echo "      https://github.com/yonaskolb/XcodeGen"
        exit 1
    fi
fi
echo "  ✓ xcodegen found"

if ! command -v claude >/dev/null 2>&1; then
    echo "  ⚠ Claude Code CLI not found — QuadClaude launches it. Install later with:"
    echo "      npm install -g @anthropic-ai/claude-code"
fi

# ── 2. Generate the Xcode project ────────────────────────────────
echo ""
echo "→ Generating Xcode project (xcodegen generate)…"
( cd "$MAC_DIR" && xcodegen generate ) || { echo "  ✗ xcodegen generate failed."; exit 1; }

# ── 3. Build (Release). SYMROOT=build → ./build/Release/ ──────────
echo ""
echo "→ Building QuadClaudeMac.app (Release)…"
( cd "$MAC_DIR" && xcodebuild -project QuadClaudeMac.xcodeproj -scheme QuadClaudeMac \
    -configuration Release SYMROOT=build build ) \
    || { echo "  ✗ App build failed. Open the project in Xcode to see details."; exit 1; }

echo ""
echo "→ Building quadclaude CLI (Release)…"
( cd "$MAC_DIR" && xcodebuild -project QuadClaudeMac.xcodeproj -scheme quadclaude \
    -configuration Release SYMROOT=build build ) \
    || { echo "  ✗ CLI build failed. Open the project in Xcode to see details."; exit 1; }

# Locate build products (SYMROOT layout first, then XcodeGen's default).
APP=""
CLI=""
for p in "$MAC_DIR/build/Release/QuadClaudeMac.app" \
         "$MAC_DIR/build/Build/Products/Release/QuadClaudeMac.app"; do
    [ -d "$p" ] && { APP="$p"; break; }
done
for p in "$MAC_DIR/build/Release/quadclaude" \
         "$MAC_DIR/build/Build/Products/Release/quadclaude"; do
    [ -x "$p" ] && { CLI="$p"; break; }
done

[ -n "$APP" ] && echo "  ✓ App:  $APP" || echo "  ⚠ Could not locate built .app — check build output."
[ -n "$CLI" ] && echo "  ✓ CLI:  $CLI" || echo "  ⚠ Could not locate built quadclaude — check build output."

# ── 4. Symlink the CLI onto PATH ─────────────────────────────────
if [ -n "$CLI" ]; then
    echo ""
    echo "→ Putting quadclaude on your PATH…"
    LINK_DIR="/usr/local/bin"
    # Prefer ~/.local/bin if it's on PATH and /usr/local/bin isn't writable.
    if [ ! -w "$LINK_DIR" ] && printf '%s' "$PATH" | grep -q "$HOME/.local/bin"; then
        LINK_DIR="$HOME/.local/bin"; mkdir -p "$LINK_DIR"
    fi
    if ln -sf "$CLI" "$LINK_DIR/quadclaude" 2>/dev/null; then
        echo "  ✓ Symlinked → $LINK_DIR/quadclaude"
    else
        echo "  ⚠ Couldn't write $LINK_DIR/quadclaude. Try:"
        echo "      sudo ln -sf \"$CLI\" /usr/local/bin/quadclaude"
    fi
fi

# Copy the app to /Applications so the CLI can auto-launch it (best-effort).
if [ -n "$APP" ]; then
    printf "\nCopy QuadClaudeMac.app to /Applications? [y/N]: "
    read -r _cp
    case "$_cp" in
        y|Y) cp -R "$APP" /Applications/ 2>/dev/null \
                && echo "  ✓ Copied to /Applications" \
                || echo "  ⚠ Copy failed (try with sudo, or run from the build dir)." ;;
        *) echo "  Skipped. The CLI also finds the app in the build dir." ;;
    esac
fi

# ── 5. Run setup ─────────────────────────────────────────────────
echo ""
printf "Run 'quadclaude setup' now? [Y/n]: "
read -r _setup
case "$_setup" in
    n|N) echo "  Skipped. Run 'quadclaude setup' when ready." ;;
    *)
        if command -v quadclaude >/dev/null 2>&1; then
            quadclaude setup
        elif [ -n "$CLI" ]; then
            "$CLI" setup
        else
            echo "  ⚠ quadclaude not found on PATH — run its setup manually."
        fi
        ;;
esac

echo ""
echo "Done (best-effort). See SETUP-MAC.md for details, gaps, and troubleshooting."
echo "Reminder: grant Accessibility permission to QuadClaudeMac.app on first launch."
