#!/bin/zsh
# Claude launcher with project picker + layout support (macOS version)
# Usage: source claude-launch-mac.sh
#
# EXPERIMENTAL / UNTESTED macOS port. Mirrors the tested Windows launcher
# (claude-launch.sh) as closely as zsh allows. Behaviour parity notes:
#   • Onboarding checklist runs (via the shared bash onboarding.sh) when a quad
#     isn't fully set up.
#   • worktrees layout: every quad (incl. Quad 1) opens its OWN worktree,
#     resets it to the configured base branch, and best-effort provisions it.
# The Monday.com task-handoff flow from the Windows script is intentionally NOT
# ported (there is no Monday UI in the Mac app yet).

# Export quad index so Claude hooks can target the correct terminal window
if [ -z "$QUAD_INDEX" ]; then
    export QUAD_INDEX=0
fi

# Source runtime config from QuadClaude setup (if available)
ENV_FILE="$HOME/Library/Application Support/QuadClaude/launch-env.sh"
if [ -f "$ENV_FILE" ]; then
    source "$ENV_FILE"
fi

# ── Repo root (two levels up from this script) ─────────────────
# This script lives at <repo>/QuadClaudeMac/Scripts/claude-launch-mac.sh, so the
# repo root is two directories above the script's own directory. Used to locate
# onboarding.sh and (via QUADCLAUDE_SCRIPT_DIR) the quadclaude CLI.
# ${(%):-%N} robustly yields this script's path even when sourced; :A makes it
# absolute; :h takes the dirname.
_qc_self="${${(%):-%N}:A}"
SCRIPT_DIR="${_qc_self:h}"          # …/QuadClaudeMac/Scripts
REPO_ROOT="${SCRIPT_DIR:h:h}"       # …            (repo root)

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

# ── Resolve this quad's worktree folder from the configurable pattern ──
# Mirrors the Windows launcher: substitute {base}→base repo name and {n}→quad
# number (1-based) in QUADCLAUDE_WORKTREE_PATTERN (default "{base} - Quad-{n}").
# Escaped braces (\{ \}) match the literal placeholder text in both bash & zsh.
_qc_worktree_target() {
    local n=$(( ${QUAD_INDEX:-0} + 1 ))
    local pattern="$QUADCLAUDE_WORKTREE_PATTERN"
    [ -n "$pattern" ] || pattern='{base} - Quad-{n}'
    local name="${pattern//\{base\}/$QUADCLAUDE_WORKTREE_BASE}"
    name="${name//\{n\}/$n}"
    printf '%s/%s' "$PROJECTS_DIR" "$name"
}

# ── Best-effort worktree provisioning (mirrors Windows _quadclaude_provision) ──
# A freshly-cut worktree has only tracked files — node_modules and .env* are
# gitignored, so the first build/dev in it can fail. Copy .env* from the base
# repo, install deps when missing, and run an optional per-repo provision cmd.
# Every step is best-effort: a failure warns but never blocks the session.
_quadclaude_provision() {
    local src="$1" dst rel f base
    dst="$(pwd)"
    [ -n "$src" ] && [ -d "$src" ] || return 0
    [ "$src" = "$dst" ] && return 0   # never provision the base repo over itself

    # 1. .env* from the base repo (copy-if-missing): repo root, plus an optional
    #    configured subdir (e.g. a monorepo app folder) if one is set.
    #    The (N) glob qualifier is zsh's null-glob: no match → skip silently.
    local subdirs=(".")
    [ -n "$QUADCLAUDE_WORKTREE_SUBDIR" ] && subdirs=("$QUADCLAUDE_WORKTREE_SUBDIR" ".")
    for rel in "${subdirs[@]}"; do
        [ -d "$src/$rel" ] || continue
        for f in "$src/$rel"/.env*(N); do
            [ -e "$f" ] || continue
            base="$(basename "$f")"
            [ -e "$dst/$rel/$base" ] || cp "$f" "$dst/$rel/$base" 2>/dev/null
        done
    done

    # 2. Dependencies — install only when a package.json exists and deps absent.
    if [ -f "$dst/package.json" ] && [ ! -d "$dst/node_modules" ]; then
        echo "→ First run in this worktree — installing dependencies (npm install)…"
        ( cd "$dst" && npm install ) >/dev/null 2>&1 \
            || echo "  ⚠ npm install failed — run it manually before building."
    fi

    # 3. Optional per-repo provision hook (whatever your stack needs).
    if [ -n "$QUADCLAUDE_PROVISION_CMD" ]; then
        echo "→ Provisioning: $QUADCLAUDE_PROVISION_CMD"
        ( cd "$dst" && eval "$QUADCLAUDE_PROVISION_CMD" ) >/dev/null 2>&1 \
            || echo "  ⚠ provision command failed — run it manually: $QUADCLAUDE_PROVISION_CMD"
    fi
}

# ── First-run / incomplete-setup onboarding ───────────────────
# Decide whether this quad still needs setup. Mirrors the triggers the Windows
# launcher relies on: no env file, no Claude CLI, or (worktrees layout) this
# quad's worktree folder doesn't exist yet.
_qc_should_onboard() {
    [ -f "$ENV_FILE" ] || return 0                 # never ran setup
    command -v claude &>/dev/null || return 0      # no Claude CLI
    if [ "$LAYOUT" = "worktrees" ]; then
        [ -n "$QUADCLAUDE_WORKTREE_BASE" ] || return 0
        [ -d "$(_qc_worktree_target)" ] || return 0   # this quad's worktree missing
    fi
    return 1   # fully set up → don't onboard
}

# onboarding.sh is a bash script; zsh can't safely source it, so run it as a
# bash SUBPROCESS with the vars it reads exported into that process. It renders
# the per-quad checklist + worktree tutorial and returns. Its [s] action runs
# `quadclaude setup`; [w]/[a] create worktrees — any of which may regenerate
# launch-env.sh or create this quad's worktree, so we re-source & re-resolve
# after it returns.
if [ -f "$REPO_ROOT/onboarding.sh" ] && _qc_should_onboard; then
    export QUADCLAUDE_SCRIPT_DIR="$REPO_ROOT"
    export ENV_FILE QUAD_INDEX PROJECTS_DIR \
        QUADCLAUDE_PROJECTS_DIR QUADCLAUDE_LAYOUT QUADCLAUDE_WORKTREE_BASE \
        QUADCLAUDE_WORKTREE_PATTERN QUADCLAUDE_WORKTREE_BASE_BRANCH \
        QUADCLAUDE_WORKTREE_SUBDIR QUADCLAUDE_PROVISION_CMD QUADCLAUDE_TARGET_DIR
    /bin/bash "$REPO_ROOT/onboarding.sh"
    # Re-source & re-resolve so a just-created worktree / just-run setup takes effect.
    [ -f "$ENV_FILE" ] && source "$ENV_FILE"
    PROJECTS_DIR="${QUADCLAUDE_PROJECTS_DIR:-$PROJECTS_DIR}"
    LAYOUT="${QUADCLAUDE_LAYOUT:-$LAYOUT}"
fi

# ── Layout: worktrees ──────────────────────────────────────────
# Every quad — including Quad 1 (index 0) — opens its OWN worktree via the
# configurable pattern, never the bare base repo (driving git in the shared
# base checkout would race any active work there).
if [ "$LAYOUT" = "worktrees" ] && [ -n "$QUADCLAUDE_WORKTREE_BASE" ]; then
    TARGET="$(_qc_worktree_target)"

    if [ -d "$TARGET" ]; then
        cd "$TARGET"
        # Reset this worktree to a fresh copy of the base branch (best-effort,
        # silenced). Detaching avoids clobbering local branches and mirrors the
        # Windows launcher's pre-launch sync.
        if git rev-parse --git-dir &>/dev/null; then
            BB="${QUADCLAUDE_WORKTREE_BASE_BRANCH:-main}"
            git fetch origin "$BB" --quiet 2>/dev/null
            git checkout --detach "origin/$BB" 2>/dev/null
        fi
        _quadclaude_track 2>/dev/null
        LABEL="${QUADCLAUDE_LABELS[$((QUAD_INDEX + 1))]:-Quad $((QUAD_INDEX + 1))}"
        echo "→ $LABEL: $(basename "$TARGET")"
        echo ""
        # Make a freshly-cut worktree runnable before Claude starts.
        _quadclaude_provision "$PROJECTS_DIR/$QUADCLAUDE_WORKTREE_BASE"
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
        LABEL="${QUADCLAUDE_LABELS[$((QUAD_INDEX + 1))]:-Quad $((QUAD_INDEX + 1))}"
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
