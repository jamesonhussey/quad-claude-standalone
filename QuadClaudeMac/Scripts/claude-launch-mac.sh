#!/bin/zsh
# Claude launcher with project picker + layout support (macOS version)
# Usage: source claude-launch-mac.sh

# Export quad index so Claude hooks can target the correct terminal window
if [ -z "$QUAD_INDEX" ]; then
    export QUAD_INDEX=0
fi

# Source runtime config from QuadClaude setup (if available)
ENV_FILE="$HOME/Library/Application Support/QuadClaude/launch-env.sh"
if [ -f "$ENV_FILE" ]; then
    source "$ENV_FILE"
fi

# ── Branch/dir tracking for StatusWidget ──────────────────────
# Writes current directory + git branch to a JSON file on every prompt.
# The StatusWidget polls this file to show live branch/project info.
_quadclaude_track() {
    local qi="${QUAD_INDEX:-0}"
    local state_dir="$HOME/Library/Application Support/QuadClaude"
    local state_file="$state_dir/quad-${qi}.cwd.json"
    local cwd="$(pwd)"
    local project="$(basename "$cwd")"
    local branch=""

    # Get git branch if in a repo (fast — reads HEAD file directly)
    if [ -d ".git" ] || git rev-parse --git-dir &>/dev/null; then
        branch="$(git symbolic-ref --short HEAD 2>/dev/null || git rev-parse --short HEAD 2>/dev/null || echo "")"
    fi

    # Write JSON atomically (write to tmp, then move)
    local tmp="$state_file.tmp"
    printf '{"cwd":"%s","project":"%s","branch":"%s"}' \
        "$(echo "$cwd" | sed 's/\\/\\\\/g; s/"/\\"/g')" \
        "$(echo "$project" | sed 's/"/\\"/g')" \
        "$(echo "$branch" | sed 's/"/\\"/g')" \
        > "$tmp" 2>/dev/null && mv -f "$tmp" "$state_file" 2>/dev/null
}

# Install the tracker into the prompt hook
if [ -n "$QUAD_INDEX" ]; then
    mkdir -p "$HOME/Library/Application Support/QuadClaude" 2>/dev/null

    # Zsh uses precmd, bash uses PROMPT_COMMAND
    if [ -n "$ZSH_VERSION" ]; then
        precmd() { _quadclaude_track; }
    else
        PROMPT_COMMAND="_quadclaude_track${PROMPT_COMMAND:+;$PROMPT_COMMAND}"
    fi

    # Write initial state immediately
    _quadclaude_track
fi

# Fall back to default if not set by env file
PROJECTS_DIR="${QUADCLAUDE_PROJECTS_DIR:-$HOME/Projects}"
LAYOUT="${QUADCLAUDE_LAYOUT:-multi-project}"

# ── Layout: worktrees ──────────────────────────────────────────
if [ "$LAYOUT" = "worktrees" ] && [ -n "$QUADCLAUDE_WORKTREE_BASE" ]; then
    if [ "${QUAD_INDEX:-0}" = "0" ]; then
        TARGET="$PROJECTS_DIR/$QUADCLAUDE_WORKTREE_BASE"
    else
        QUAD_NUM=$((QUAD_INDEX + 1))
        TARGET="$PROJECTS_DIR/$QUADCLAUDE_WORKTREE_BASE - Quad-$QUAD_NUM"
    fi

    if [ -d "$TARGET" ]; then
        cd "$TARGET"
        _quadclaude_track 2>/dev/null
        LABEL="${QUADCLAUDE_LABELS[$QUAD_INDEX]:-Quad $((QUAD_INDEX + 1))}"
        echo "→ $LABEL: $(basename "$TARGET")"
        echo ""
        claude
        exec zsh -i
    else
        echo "⚠ Worktree dir not found: $TARGET"
        echo "  Falling back to project picker..."
        echo ""
    fi
fi

# ── Layout: dedicated-roles ────────────────────────────────────
if [ "$LAYOUT" = "dedicated-roles" ] && [ -n "$QUADCLAUDE_TARGET_DIR" ]; then
    if [ -d "$QUADCLAUDE_TARGET_DIR" ]; then
        cd "$QUADCLAUDE_TARGET_DIR"
        _quadclaude_track 2>/dev/null
        LABEL="${QUADCLAUDE_LABELS[$QUAD_INDEX]:-Quad $((QUAD_INDEX + 1))}"
        echo "→ Role: $LABEL"
        echo "→ Project: $(basename "$QUADCLAUDE_TARGET_DIR")"
        echo ""
        claude
        exec zsh -i
    else
        echo "⚠ Project dir not found: $QUADCLAUDE_TARGET_DIR"
        echo "  Falling back to project picker..."
        echo ""
    fi
fi

# ── Layout: multi-project / hybrid / fallback ─────────────────
echo "╔══════════════════════════════════════╗"
echo "║       Pick a project                 ║"
echo "╠══════════════════════════════════════╣"

# Build project list from directories
dirs=()
i=1
for d in "$PROJECTS_DIR"/*/; do
    [ -d "$d" ] || continue
    name=$(basename "$d")
    dirs+=("$name")
    printf "║  %2d) %-32s ║\n" "$i" "$name"
    ((i++))
done

echo "║                                      ║"
printf "║  %2d) %-32s ║\n" "0" "Stay in Projects root"
echo "╚══════════════════════════════════════╝"
echo ""
read "choice?Enter number: "

if [[ "$choice" =~ ^[0-9]+$ ]] && [ "$choice" -ge 1 ] && [ "$choice" -le "${#dirs[@]}" ]; then
    selected="${dirs[$choice]}"
    cd "$PROJECTS_DIR/$selected"
    echo "→ Opening in: $selected"
else
    cd "$PROJECTS_DIR"
    echo "→ Opening in: Projects root"
fi

# Update branch/project state before Claude takes over the shell
_quadclaude_track 2>/dev/null

echo ""
claude
exec zsh -i
