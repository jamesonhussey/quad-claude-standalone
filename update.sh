#!/usr/bin/env bash
# QuadClaude updater (bash) — works on WSL/Ubuntu, macOS, and Git Bash.
# Pulls the latest code, refreshes the helper slash-commands into
# ~/.claude/commands, and (on Windows + .NET SDK) rebuilds the app.
#
# Usage:  bash update.sh      (run it from inside the repo)
set -e
cd "$(dirname "$0")"

echo "→ Pulling latest…"
git pull

# Refresh the bundled helper commands. `setup` installs them copy-if-missing (so
# it never clobbers your own commands), which means content updates to them don't
# reach the installed copy on their own — so the updater overwrites them here.
if [ -d .claude/commands ]; then
    mkdir -p "$HOME/.claude/commands"
    cp -f .claude/commands/*.md "$HOME/.claude/commands/"
    echo "→ Refreshed helper commands in ~/.claude/commands"
fi

# Rebuild the Windows app only when running under Git Bash on Windows with the
# SDK present. On WSL/Ubuntu/macOS this is skipped (the WPF app is built on the
# Windows side / the Mac port builds via Xcode — see SETUP-MAC.md).
case "$(uname -s 2>/dev/null)" in
    MINGW*|MSYS*|CYGWIN*)
        if command -v dotnet >/dev/null 2>&1 && [ -d QuadClaude ]; then
            echo "→ Rebuilding QuadClaude…"
            ( cd QuadClaude && dotnet publish -c Release -r win-x64 --no-self-contained -o publish >/dev/null )
            echo "→ Rebuilt QuadClaude/publish/QuadClaude.exe"
        fi
        ;;
esac

echo "→ Done. Restart QuadClaude / start a new Claude session to pick up the changes."
