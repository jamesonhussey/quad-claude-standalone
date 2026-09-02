#!/bin/bash
# Claude launcher with project picker + layout support
# Usage: claude-launch.sh [quad-index]

# ── Sanitize inherited Claude session markers ──
# Quads are always interactive sessions and should save transcripts. But if this
# launcher (or the terminal that started it) was itself spawned from inside a
# Claude session — e.g. Windows Terminal launched via a Bash tool — it inherits
# CLAUDE_CODE_CHILD_SESSION=1, which silently disables transcript persistence for
# every quad. Scrub it here, the one choke point all quads pass through, so the
# interactive session reverts to its normal persist-by-default behavior. We only
# unset (not force-on): a quad that later spawns a headless `claude -p` worker
# re-sets the marker itself so those runs still stay out of session history.
unset CLAUDE_CODE_CHILD_SESSION

# ── Stop Ctrl+click opening URLs twice ──
# Claude Code's fullscreen renderer opens a URL on Ctrl+click, and Windows Terminal ALSO opens it
# on Ctrl+click — so one Ctrl+click spawned two browser tabs. Disable Claude Code's own mouse-click
# handling (link-open + click-to-move-cursor) so Windows Terminal is the single handler. Wheel
# scrolling is unaffected. (Claude Code issue #68568.) Comment this out to get click-to-move back.
export CLAUDE_CODE_DISABLE_MOUSE_CLICKS=1

# Export quad index so Claude hooks can target the correct terminal window
if [ -n "$1" ]; then
    export QUAD_INDEX="$1"
fi

# Instance name ($2) lets a parallel "dev" QuadClaude use a separate state dir.
# Defaults to QuadClaude. Exported so the C# track command writes to the right place.
INSTANCE="${2:-QuadClaude}"
export QUADCLAUDE_INSTANCE="$INSTANCE"

# ── Terminal title: [1] project-name for at-a-glance identification ──
# Grid quads use 1-4. Spawned extras (5.1, 5.2, ...) should set
# QUAD_LABEL directly before invoking this script.
_quadclaude_set_title() {
    local label="${1:-Claude}"
    local tag="${QUAD_LABEL:-$(( ${QUAD_INDEX:-0} + 1 ))}"
    printf '\033]0;[%s] %s\007' "$tag" "$label"
}
if [ -n "$QUAD_INDEX" ] || [ -n "$QUAD_LABEL" ]; then
    _quadclaude_set_title "Claude"
fi

# Source runtime config from QuadClaude setup (if available)
# Convert APPDATA to MSYS path if needed (Windows backslashes → forward slashes)
_appdata="${APPDATA:-}"
if command -v cygpath &>/dev/null && [ -n "$_appdata" ]; then
    _appdata="$(cygpath -u "$_appdata")"
fi
ENV_FILE="${_appdata}/${INSTANCE}/launch-env.sh"
if [ -f "$ENV_FILE" ]; then
    source "$ENV_FILE"
fi

# Directory this script lives in (the repo root) — used to find onboarding.sh
# and the QuadClaude executable. Exported so onboarding.sh can locate the exe.
QUADCLAUDE_SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export QUADCLAUDE_SCRIPT_DIR ENV_FILE
if [ -f "$QUADCLAUDE_SCRIPT_DIR/onboarding.sh" ]; then
    source "$QUADCLAUDE_SCRIPT_DIR/onboarding.sh"
fi

# ── Branch/dir tracking for StatusWidget ──────────────────────
# Writes current directory + git branch to a JSON file on every prompt.
# The StatusWidget polls this file to show live branch/project info.
_quadclaude_track() {
    local qi="${QUAD_INDEX:-0}"
    local state_dir="${APPDATA}/${INSTANCE}"
    local state_file="$state_dir/quad-${qi}.cwd.json"
    local cwd="$(pwd)"
    local project="$(basename "$cwd")"
    local branch=""

    # Get git branch if in a repo. On a DETACHED HEAD (e.g. a worktree reset to the base branch after
    # a handoff), don't emit a bare SHA that reads as a wrong branch name — name the base ref that HEAD
    # sits on (prefer staging/main) as "<base> (detached)", falling back to "detached (<sha>)".
    if [ -d ".git" ] || git rev-parse --git-dir &>/dev/null; then
        branch="$(git symbolic-ref --short HEAD 2>/dev/null)"
        if [ -z "$branch" ]; then
            local base
            base="$(git for-each-ref --points-at HEAD --format='%(refname:short)' refs/heads refs/remotes/origin 2>/dev/null | sed 's#^origin/##' | grep -Ex 'staging|main|master' | head -1)"
            [ -z "$base" ] && base="$(git for-each-ref --points-at HEAD --format='%(refname:short)' refs/heads refs/remotes/origin 2>/dev/null | sed 's#^origin/##' | head -1)"
            if [ -n "$base" ]; then branch="$base (detached)"; else branch="detached ($(git rev-parse --short HEAD 2>/dev/null))"; fi
        fi
    fi

    # Preserve the sessionId the C# track hook wrote — bash has no session id of its own, so writing
    # the file without it would wipe the per-quad session and break the overlay's title resolution.
    local sid=""
    if [ -f "$state_file" ]; then
        sid="$(sed -n 's/.*"sessionId"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$state_file" 2>/dev/null | head -1)"
    fi

    # Write JSON atomically (write to tmp, then move)
    local tmp="$state_file.tmp"
    printf '{"cwd":"%s","project":"%s","branch":"%s","sessionId":"%s"}' \
        "$(echo "$cwd" | sed 's/\\/\\\\/g; s/"/\\"/g')" \
        "$(echo "$project" | sed 's/"/\\"/g')" \
        "$(echo "$branch" | sed 's/"/\\"/g')" \
        "$(echo "$sid" | sed 's/"/\\"/g')" \
        > "$tmp" 2>/dev/null && mv -f "$tmp" "$state_file" 2>/dev/null
}

# Install the tracker into PROMPT_COMMAND (runs on every prompt)
if [ -n "$QUAD_INDEX" ]; then
    mkdir -p "${APPDATA}/${INSTANCE}" 2>/dev/null
    _quadclaude_prompt() { _quadclaude_track; _quadclaude_set_title "$(basename "$(pwd)")"; }
    PROMPT_COMMAND="_quadclaude_prompt${PROMPT_COMMAND:+;$PROMPT_COMMAND}"
    # Write initial state immediately
    _quadclaude_track
fi

# ── Verify Claude Code is installed ───────────────────────────
_quadclaude_check_claude() {
    if ! claude --version &>/dev/null; then
        echo ""
        echo "╔══════════════════════════════════════════════════════╗"
        echo "║  ⚠  Claude Code CLI not found!                      ║"
        echo "║                                                      ║"
        echo "║  Install it with:                                    ║"
        echo "║    npm install -g @anthropic-ai/claude-code          ║"
        echo "║                                                      ║"
        echo "║  Then restart QuadClaude.                            ║"
        echo "╚══════════════════════════════════════════════════════╝"
        echo ""
        exec bash -i
    fi
}

# ── Monday task handoff (queued — applied AFTER you pick your worktree) ──
# The Monday panel writes quad-N.task.json to queue a session for this quad.
# We deliberately DON'T skip the project picker or cd anywhere here — you pick
# the worktree as usual (the QuadClaude intro), and the queued session is then
# opened in whatever you chose, via _launch_claude at the end of each layout.
TASK_FILE="${_appdata}/${INSTANCE}/quad-${QUAD_INDEX:-0}.task.json"
TASK_PROMPT_FILE="${_appdata}/${INSTANCE}/quad-${QUAD_INDEX:-0}.prompt.txt"
TASK_SESSION=""
TASK_RESUME=""
TASK_BRANCH=""
TASK_DIR=""
if [ -n "$QUAD_INDEX" ] && [ -f "$TASK_FILE" ]; then
    TASK_DIR=$(sed -n 's/.*"dir":"\([^"]*\)".*/\1/p' "$TASK_FILE")
    TASK_BRANCH=$(sed -n 's/.*"branch":"\([^"]*\)".*/\1/p' "$TASK_FILE")
    TASK_SESSION=$(sed -n 's/.*"sessionId":"\([^"]*\)".*/\1/p' "$TASK_FILE")
    TASK_RESUME=$(sed -n 's/.*"resume":\([a-z]*\).*/\1/p' "$TASK_FILE")
    rm -f "$TASK_FILE"
fi

# ── Worktree provisioning (generic, best-effort) ──
# A freshly-cut worktree has only tracked files — node_modules and .env* are
# gitignored, so the first build/dev in it can fail ("Module not found", missing
# env vars). This copies .env* from the base repo, installs deps when missing,
# and runs an optional per-repo provision command (QUADCLAUDE_PROVISION_CMD, e.g.
# "npx prisma generate"). Every step is best-effort — a failure warns but never
# blocks the session from starting. Stack-specific steps are opt-in via config,
# so this works for any repo, not just one project.
_quadclaude_provision() {
    local src="$1" dst rel f base
    dst="$(pwd)"
    [ -n "$src" ] && [ -d "$src" ] || return 0
    [ "$src" = "$dst" ] && return 0   # never provision the base repo over itself

    # 1. .env* from the base repo (copy-if-missing): repo root, plus an optional
    #    configured subdir (e.g. a monorepo app folder) if one is set.
    local subdirs=(".")
    [ -n "$QUADCLAUDE_WORKTREE_SUBDIR" ] && subdirs=("$QUADCLAUDE_WORKTREE_SUBDIR" ".")
    for rel in "${subdirs[@]}"; do
        [ -d "$src/$rel" ] || continue
        for f in "$src/$rel"/.env*; do
            [ -e "$f" ] || continue
            base="$(basename "$f")"
            [ -e "$dst/$rel/$base" ] || cp "$f" "$dst/$rel/$base" 2>/dev/null
        done
    done

    # 2. Dependencies — install only when a package.json exists and deps are absent.
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

# Start Claude in the *already-chosen* directory. Uses the queued Monday session
# if one was handed off, otherwise a plain interactive session. Each layout
# branch calls this after it has cd'd into the working directory.
_launch_claude() {
    # Switch to the task's branch within the chosen worktree, if one was set.
    # Guard: never check out over uncommitted work — a branch switch would silently
    # overwrite files an active session is editing in this dir. Skip + warn instead.
    if [ -n "$TASK_BRANCH" ] && git rev-parse --git-dir &>/dev/null; then
        local _cur; _cur="$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo '')"
        if [ "$_cur" = "$TASK_BRANCH" ]; then
            : # already on the task branch — nothing to switch
        elif git diff --quiet 2>/dev/null && git diff --cached --quiet 2>/dev/null; then
            git checkout "$TASK_BRANCH" 2>/dev/null || git checkout -b "$TASK_BRANCH" 2>/dev/null || true
        else
            echo "⚠ $(pwd) has uncommitted changes — refusing to checkout '$TASK_BRANCH' over them."
            echo "  Staying on '${_cur:-current HEAD}'. Commit or stash, then switch manually."
            echo ""
        fi
    fi

    # Make a freshly-cut worktree runnable before Claude starts (worktrees layout
    # sets PROVISION_SRC to the base repo; other layouts leave it unset → skip).
    [ -n "$PROVISION_SRC" ] && _quadclaude_provision "$PROVISION_SRC"

    if [ -z "$TASK_SESSION" ]; then
        claude
        return
    fi

    echo "→ Monday session: ${TASK_SESSION}"
    [ -n "$TASK_BRANCH" ] && echo "→ Branch: ${TASK_BRANCH}"
    echo ""

    if [ "$TASK_RESUME" = "true" ]; then
        rm -f "$TASK_PROMPT_FILE"   # resuming — ignore any seed prompt
        claude --resume "$TASK_SESSION"
    elif [ -f "$TASK_PROMPT_FILE" ]; then
        # Seed the new session with the Monday task as its first message.
        # $(cat ...) safely carries multi-line / quoted content as one arg.
        local prompt; prompt="$(cat "$TASK_PROMPT_FILE")"
        rm -f "$TASK_PROMPT_FILE"
        claude --session-id "$TASK_SESSION" "$prompt"
    else
        claude --session-id "$TASK_SESSION"
    fi
}

# ── Resume short-circuit ───────────────────────────────────────
# Resuming an existing Monday session: we know the dir it lives in, so cd
# straight there and resume — no picker. (New sessions still go through the
# picker below so you choose the worktree.)
if [ "$TASK_RESUME" = "true" ] && [ -n "$TASK_DIR" ] && [ -d "$TASK_DIR" ]; then
    cd "$TASK_DIR"
    _quadclaude_track 2>/dev/null
    _quadclaude_set_title "$(basename "$(pwd)")"
    echo "→ Resuming in: $(basename "$(pwd)")"
    echo ""
    _launch_claude
    exec bash -i
fi

# Fall back to default if not set by env file
PROJECTS_DIR="${QUADCLAUDE_PROJECTS_DIR:-$HOME/Projects}"
LAYOUT="${QUADCLAUDE_LAYOUT:-multi-project}"

# ── First-run / incomplete-setup onboarding ───────────────────
# Any quad that isn't fully set up shows a per-quad checklist + worktree
# tutorial, then continues. Skipped once everything is in place.
if declare -f _quadclaude_should_onboard >/dev/null 2>&1 && _quadclaude_should_onboard; then
    quadclaude_onboarding
    # The [s]/[w]/[a] actions may have regenerated launch-env.sh or created this
    # quad's worktree — re-source and re-resolve so the layout below sees them.
    [ -f "$ENV_FILE" ] && source "$ENV_FILE"
    PROJECTS_DIR="${QUADCLAUDE_PROJECTS_DIR:-$PROJECTS_DIR}"
    LAYOUT="${QUADCLAUDE_LAYOUT:-$LAYOUT}"
fi

# ── Layout: worktrees ──────────────────────────────────────────
# Each quad auto-opens its worktree directory. No picker needed.
if [ "$LAYOUT" = "worktrees" ] && [ -n "$QUADCLAUDE_WORKTREE_BASE" ]; then
    # Every quad — including Quad 1 — opens its own worktree, never the bare base
    # repo. Driving git in the shared base checkout races any active work there
    # (a task-branch checkout would overwrite in-progress edits).
    QUAD_NUM=$((QUAD_INDEX + 1))
    if [ -n "$QUADCLAUDE_WORKTREE_PATTERN" ]; then
        PATTERN="$QUADCLAUDE_WORKTREE_PATTERN"
    else
        PATTERN='{base} - Quad-{n}'
    fi
    WORKTREE_NAME="${PATTERN//\{base\}/$QUADCLAUDE_WORKTREE_BASE}"
    WORKTREE_NAME="${WORKTREE_NAME//\{n\}/$QUAD_NUM}"
    TARGET="$PROJECTS_DIR/$WORKTREE_NAME"

    if [ -d "$TARGET" ]; then
        cd "$TARGET"
        if git rev-parse --git-dir &>/dev/null; then
            WT_BASE_BRANCH="${QUADCLAUDE_WORKTREE_BASE_BRANCH:-main}"
            git fetch origin "$WT_BASE_BRANCH" --quiet 2>/dev/null
            git checkout --detach "origin/$WT_BASE_BRANCH" 2>/dev/null
        fi
        _quadclaude_track 2>/dev/null
        _quadclaude_set_title "$(basename "$(pwd)")"
        LABEL="${QUADCLAUDE_LABELS[$QUAD_INDEX]:-Quad $((QUAD_INDEX + 1))}"
        echo "→ $LABEL: $(basename "$TARGET")"
        echo ""
        _quadclaude_check_claude
        PROVISION_SRC="$PROJECTS_DIR/$QUADCLAUDE_WORKTREE_BASE"  # base repo → env/deps source
        _launch_claude
        exec bash -i
    else
        echo "⚠ Worktree dir not found: $TARGET"
        echo "  Falling back to project picker..."
        echo ""
    fi
fi

# ── Layout: dedicated-roles ────────────────────────────────────
# All quads open the same project. Show role label.
if [ "$LAYOUT" = "dedicated-roles" ] && [ -n "$QUADCLAUDE_TARGET_DIR" ]; then
    if [ -d "$QUADCLAUDE_TARGET_DIR" ]; then
        cd "$QUADCLAUDE_TARGET_DIR"
        _quadclaude_track 2>/dev/null
        _quadclaude_set_title "$(basename "$(pwd)")"
        LABEL="${QUADCLAUDE_LABELS[$QUAD_INDEX]:-Quad $((QUAD_INDEX + 1))}"
        echo "→ Role: $LABEL"
        echo "→ Project: $(basename "$QUADCLAUDE_TARGET_DIR")"
        echo ""
        _quadclaude_check_claude
        _launch_claude
        exec bash -i
    else
        echo "⚠ Project dir not found: $QUADCLAUDE_TARGET_DIR"
        echo "  Falling back to project picker..."
        echo ""
    fi
fi

# ── Layout: multi-project / hybrid / fallback ─────────────────
# Show interactive project picker.
echo "╔══════════════════════════════════════╗"
echo "║       Pick a project                 ║"
echo "╠══════════════════════════════════════╣"

# Build project list from directories
dirs=()
i=1
for d in "$PROJECTS_DIR"/*/; do
    name=$(basename "$d")
    dirs+=("$name")
    printf "║  %2d) %-32s ║\n" "$i" "$name"
    ((i++))
done

echo "║                                      ║"
printf "║  %2d) %-32s ║\n" "0" "Stay in Projects root"
echo "╚══════════════════════════════════════╝"
echo ""
read -p "Enter number: " choice

if [[ "$choice" =~ ^[0-9]+$ ]] && [ "$choice" -ge 1 ] && [ "$choice" -le "${#dirs[@]}" ]; then
    selected="${dirs[$((choice-1))]}"
    cd "$PROJECTS_DIR/$selected"
    echo "→ Opening in: $selected"
else
    cd "$PROJECTS_DIR"
    echo "→ Opening in: Projects root"
fi

# Update branch/project state + title before Claude takes over the shell
_quadclaude_track 2>/dev/null
_quadclaude_set_title "$(basename "$(pwd)")"

echo ""
_quadclaude_check_claude
_launch_claude
exec bash -i
