#!/bin/bash
# QuadClaude — bash installer (fallback for people who prefer bash over install.bat)
# Run from the repo root: bash setup.sh
#
# What this does:
#   1. Asks where your projects live
#   2. Builds QuadClaude from source
#   3. Resolves <REPO_PATH> in the settings template
#   4. Writes settings/settings.resolved.json to merge into ~/.claude/settings.json
#   5. Prints steps to create a Start Menu shortcut for taskbar pinning

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_PATH_WIN=$(cygpath -w "$SCRIPT_DIR" 2>/dev/null || echo "$SCRIPT_DIR" | sed 's|/c/|C:\\|; s|/|\\|g')

echo "╔══════════════════════════════════════╗"
echo "║       QuadClaude Installer            ║"
echo "╠══════════════════════════════════════╣"
echo "║  Repo: $SCRIPT_DIR"
echo "╚══════════════════════════════════════╝"
echo ""

# --- Projects directory ---
# Where your git projects live. Each quad's picker lists directories under here.
TEMPLATE="settings/settings.template.json"
DEFAULT_PROJECTS_DIR="$HOME/Projects"
read -r -p "Projects directory [$DEFAULT_PROJECTS_DIR]: " PROJECTS_INPUT
PROJECTS_DIR="${PROJECTS_INPUT:-$DEFAULT_PROJECTS_DIR}"
# Expand a leading ~ if the user typed one.
PROJECTS_DIR="${PROJECTS_DIR/#\~/$HOME}"
echo "→ Projects dir: $PROJECTS_DIR"
echo ""

# --- Check .NET SDK ---
if ! command -v dotnet &>/dev/null; then
    echo "❌ .NET SDK not found. Install it first:"
    echo "   winget install Microsoft.DotNet.SDK.9"
    exit 1
fi
echo "→ .NET SDK: $(dotnet --version)"

# --- Build QuadClaude ---
echo ""
echo "Building QuadClaude..."
cd "$SCRIPT_DIR/QuadClaude"
dotnet publish -c Release -r win-x64 --self-contained false -o publish 2>&1
echo "→ Built: QuadClaude/publish/QuadClaude.exe"

# --- Update claude-launch.sh projects dir ---
cd "$SCRIPT_DIR"
sed -i "s|^PROJECTS_DIR=.*|PROJECTS_DIR=$PROJECTS_DIR|" claude-launch.sh
echo "→ Updated claude-launch.sh PROJECTS_DIR"

# --- Generate resolved settings ---
echo ""
echo "Generating settings..."
REPO_ESCAPED=$(echo "$REPO_PATH_WIN" | sed 's/\\/\\\\\\\\/g')
sed "s|<REPO_PATH>|$REPO_ESCAPED|g" "$TEMPLATE" > settings/settings.resolved.json
echo "→ Wrote settings/settings.resolved.json"
echo ""
echo "═══════════════════════════════════════"
echo "  DONE! Next steps:"
echo ""
echo "  1. Review settings/settings.resolved.json"
echo "  2. Copy or merge it into ~/.claude/settings.json"
echo "     (or ask Claude Code to merge it for you)"
echo ""
echo "  3. Pin to taskbar (Windows 11):"
echo "     - Win+R → %AppData%\\Microsoft\\Windows\\Start Menu\\Programs"
echo "     - Create shortcut → $REPO_PATH_WIN\\QuadClaude\\publish\\QuadClaude.exe"
echo "     - Set arguments: launch"
echo "     - Search QuadClaude in Start → right-click → Pin to taskbar"
echo "═══════════════════════════════════════"
