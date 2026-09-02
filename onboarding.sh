#!/bin/bash
# QuadClaude onboarding — a per-quad setup checklist + worktree tutorial.
#
# Sourced by claude-launch.sh. Shows automatically on any quad that isn't fully
# set up yet, so a first-time user can see — at a glance — what's already done
# (shared across all 4 quads) and what's still left for THIS quad.
#
# Mental model surfaced to the user:
#   • Shared items live in ONE config (%APPDATA%\QuadClaude\config.json →
#     launch-env.sh). Set them once from any quad → all 4 quads pick them up.
#   • The only per-quad thing is that quad's physical worktree folder.
#
# Inputs (already exported by claude-launch.sh before this runs):
#   QUAD_INDEX, ENV_FILE, PROJECTS_DIR, QUADCLAUDE_LAYOUT,
#   QUADCLAUDE_WORKTREE_BASE, QUADCLAUDE_WORKTREE_PATTERN,
#   QUADCLAUDE_WORKTREE_BASE_BRANCH, QUADCLAUDE_SCRIPT_DIR

# ── colors (fall back to empty when not a tty) ──
if [ -t 1 ]; then
    _C_DIM=$'\033[2m'; _C_GRN=$'\033[32m'; _C_YEL=$'\033[33m'
    _C_CYN=$'\033[36m'; _C_BLD=$'\033[1m'; _C_RST=$'\033[0m'
else
    _C_DIM=""; _C_GRN=""; _C_YEL=""; _C_CYN=""; _C_BLD=""; _C_RST=""
fi

# Path to the QuadClaude executable (used to run the shared setup wizard).
# Cross-platform: the Windows build is QuadClaude.exe; on macOS it's the
# `quadclaude` CLI (on PATH once the Mac app is built/installed).
_qc_exe() {
    local win="$QUADCLAUDE_SCRIPT_DIR/QuadClaude/publish/QuadClaude.exe"
    [ -f "$win" ] && { printf '%s' "$win"; return 0; }
    if command -v quadclaude >/dev/null 2>&1; then
        printf '%s' "$(command -v quadclaude)"; return 0
    fi
    return 1
}

# Resolve this quad's worktree folder from the shared pattern.
_qc_worktree_target() {
    local n=$(( ${QUAD_INDEX:-0} + 1 ))
    # (don't inline the default via ${..:-..} — the literal braces in the pattern
    #  confuse bash's parameter-expansion brace matching.)
    local pattern="$QUADCLAUDE_WORKTREE_PATTERN"
    [ -n "$pattern" ] || pattern='{base} - Quad-{n}'
    local name="${pattern//\{base\}/$QUADCLAUDE_WORKTREE_BASE}"
    name="${name//\{n\}/$n}"
    printf '%s/%s' "$PROJECTS_DIR" "$name"
}

_qc_base_repo() { printf '%s/%s' "$PROJECTS_DIR" "$QUADCLAUDE_WORKTREE_BASE"; }
_qc_is_git()   { git -C "$1" rev-parse --git-dir &>/dev/null; }

# A branch "exists" if a local or origin/ ref resolves in the base repo.
_qc_branch_exists() {
    local repo="$1" br="$2"
    _qc_is_git "$repo" || return 1
    git -C "$repo" rev-parse --verify --quiet "refs/heads/$br" &>/dev/null && return 0
    git -C "$repo" rev-parse --verify --quiet "refs/remotes/origin/$br" &>/dev/null && return 0
    return 1
}

# Print one checklist row.  $1 = done|todo|skip   $2 = label   $3 = value/hint
_qc_row() {
    local mark
    case "$1" in
        done) mark="${_C_GRN}[✓]${_C_RST}" ;;
        todo) mark="${_C_YEL}[ ]${_C_RST}" ;;
        *)    mark="${_C_DIM}[-]${_C_RST}" ;;
    esac
    printf '   %b %-22s %b%s%b\n' "$mark" "$2" "$_C_DIM" "$3" "$_C_RST"
}

# ── Is this quad fully set up? (used by claude-launch.sh to decide whether to show) ──
_quadclaude_should_onboard() {
    [ -f "$ENV_FILE" ] || return 0                 # never ran setup
    command -v claude &>/dev/null || return 0      # no Claude CLI
    [ -d "$PROJECTS_DIR" ] || return 0             # projects dir missing
    if [ "$QUADCLAUDE_LAYOUT" = "worktrees" ]; then
        [ -n "$QUADCLAUDE_WORKTREE_BASE" ] || return 0
        _qc_is_git "$(_qc_base_repo)" || return 0
        [ -d "$(_qc_worktree_target)" ] || return 0   # this quad's worktree missing
    fi
    return 1   # fully set up → don't onboard
}

# ── The worktree + base-branch tutorial ──
_quadclaude_tutorial() {
    local base_repo bb
    base_repo="$(_qc_base_repo)"
    bb="${QUADCLAUDE_WORKTREE_BASE_BRANCH:-main}"
    clear 2>/dev/null
    cat <<EOF
${_C_BLD}${_C_CYN}What is a base branch?${_C_RST}

  Your repo has a main line of work — usually a branch called ${_C_BLD}main${_C_RST}
  (some teams use ${_C_BLD}develop${_C_RST} or ${_C_BLD}staging${_C_RST}). That's the ${_C_BLD}base branch${_C_RST}:
  the up-to-date, shared starting point. You don't commit to it directly —
  you branch OFF it, do your work, then merge back.

  QuadClaude's base branch is currently: ${_C_GRN}${bb}${_C_RST}
  Each quad ${_C_BLD}resets to a fresh copy of ${bb}${_C_RST} every time it opens, so you
  always start from the latest code — no stale branches, no leftover edits.

${_C_BLD}${_C_CYN}What is a git worktree?${_C_RST}

  Normally a repo lets you have ONE branch checked out at a time. To switch
  branches you stash or commit, switch, and switch back — painful when you
  want 4 Claudes working at once.

  A ${_C_BLD}worktree${_C_RST} is a second (third, fourth…) working folder tied to the SAME
  repo, each on its own branch. So Quad 1 can build a feature while Quad 2
  fixes a bug — separate folders, separate branches, zero collisions, all
  sharing one git history.

  QuadClaude expects one folder per quad, named like:
     ${_C_DIM}${QUADCLAUDE_WORKTREE_BASE:-<repo>} - Quad-1${_C_RST}
     ${_C_DIM}${QUADCLAUDE_WORKTREE_BASE:-<repo>} - Quad-2${_C_RST}   … through Quad-4

${_C_BLD}${_C_CYN}Make them the easy way${_C_RST}

  Back on the checklist, press ${_C_BLD}[a]${_C_RST} and QuadClaude creates all 4 for you.

${_C_BLD}${_C_CYN}Make them by hand${_C_RST}

  From inside your base repo (${_C_DIM}${base_repo}${_C_RST}):

     ${_C_GRN}git worktree add "../${QUADCLAUDE_WORKTREE_BASE:-<repo>} - Quad-1" -b quad-1 ${bb}${_C_RST}
     ${_C_GRN}git worktree add "../${QUADCLAUDE_WORKTREE_BASE:-<repo>} - Quad-2" -b quad-2 ${bb}${_C_RST}
     ${_C_DIM}… and so on. Verify with:  git worktree list${_C_RST}

${_C_BLD}${_C_CYN}Or ask an AI to do it — paste this to Claude:${_C_RST}

${_C_DIM}  ┌────────────────────────────────────────────────────────────────┐${_C_RST}
  Set up 4 git worktrees for parallel Claude Code sessions.
  Base repo: "${base_repo}"
  Base branch: ${bb}
  Create 4 sibling folders next to the repo named
  "${QUADCLAUDE_WORKTREE_BASE:-<repo>} - Quad-1" through "... - Quad-4",
  each on its own new branch (quad-1 … quad-4) cut from ${bb},
  using \`git worktree add\`. Then run \`git worktree list\` to confirm.
${_C_DIM}  └────────────────────────────────────────────────────────────────┘${_C_RST}

EOF
    read -rp "  Press Enter to go back to the checklist… " _
}

# ── Create worktree(s).  $1 = "this" | "all" ──
_quadclaude_make_worktrees() {
    local which="$1" base_repo bb n start end i target
    base_repo="$(_qc_base_repo)"
    bb="${QUADCLAUDE_WORKTREE_BASE_BRANCH:-main}"

    if [ -z "$QUADCLAUDE_WORKTREE_BASE" ] || ! _qc_is_git "$base_repo"; then
        echo ""
        echo "  ⚠ No base repo set yet (or it isn't a git repo)."
        echo "    Press [s] first to choose your base repo, or clone it into:"
        echo "      $PROJECTS_DIR"
        echo ""
        read -rp "  Press Enter… " _
        return
    fi

    if [ "$which" = "all" ]; then start=1; end=4; else
        n=$(( ${QUAD_INDEX:-0} + 1 )); start=$n; end=$n
    fi

    echo ""
    git -C "$base_repo" fetch origin "$bb" --quiet 2>/dev/null
    for (( i=start; i<=end; i++ )); do
        local pattern="$QUADCLAUDE_WORKTREE_PATTERN"
        [ -n "$pattern" ] || pattern='{base} - Quad-{n}'
        local name="${pattern//\{base\}/$QUADCLAUDE_WORKTREE_BASE}"; name="${name//\{n\}/$i}"
        target="$PROJECTS_DIR/$name"
        if [ -d "$target" ]; then
            echo "  ${_C_GRN}[✓]${_C_RST} $name  (already exists)"
            continue
        fi
        if git -C "$base_repo" worktree add "$target" -b "quad-$i" "$bb" >/dev/null 2>&1; then
            echo "  ${_C_GRN}[✓]${_C_RST} $name  (branch quad-$i from $bb)"
        elif git -C "$base_repo" worktree add "$target" "$bb" >/dev/null 2>&1; then
            echo "  ${_C_GRN}[✓]${_C_RST} $name  (detached at $bb)"
        else
            echo "  ${_C_YEL}[!]${_C_RST} $name  — failed. Try by hand: git worktree add \"$target\" -b quad-$i $bb"
        fi
    done
    echo ""
    read -rp "  Press Enter to re-check… " _
}

# ── Main checklist loop ──
quadclaude_onboarding() {
    local quad_num=$(( ${QUAD_INDEX:-0} + 1 ))
    local label="${QUADCLAUDE_LABELS[$QUAD_INDEX]:-Quad $quad_num}"

    while true; do
        # Re-read shared config each pass so another quad's changes show up live.
        [ -f "$ENV_FILE" ] && source "$ENV_FILE"

        local base_repo bb target
        base_repo="$(_qc_base_repo)"
        bb="${QUADCLAUDE_WORKTREE_BASE_BRANCH:-main}"
        target="$(_qc_worktree_target)"

        clear 2>/dev/null
        echo ""
        echo "  ${_C_BLD}${_C_CYN}QuadClaude Setup — ${label}${_C_RST}"
        echo ""
        echo "  ${_C_BLD}Shared${_C_RST} ${_C_DIM}(set once from any quad → applies to all 4)${_C_RST}"

        command -v claude &>/dev/null \
            && _qc_row done "Claude Code CLI" "installed" \
            || _qc_row todo "Claude Code CLI" "npm i -g @anthropic-ai/claude-code"

        [ -f "$ENV_FILE" ] \
            && _qc_row done "QuadClaude config" "configured" \
            || _qc_row todo "QuadClaude config" "press [s] to run setup"

        [ -d "$PROJECTS_DIR" ] \
            && _qc_row done "Projects folder" "$PROJECTS_DIR" \
            || _qc_row todo "Projects folder" "${PROJECTS_DIR:-not set} (missing)"

        [ -n "$QUADCLAUDE_LAYOUT" ] \
            && _qc_row done "Layout" "$QUADCLAUDE_LAYOUT" \
            || _qc_row todo "Layout" "press [s] to choose"

        if [ "$QUADCLAUDE_LAYOUT" = "worktrees" ]; then
            if [ -n "$QUADCLAUDE_WORKTREE_BASE" ] && _qc_is_git "$base_repo"; then
                _qc_row done "Base repo" "$QUADCLAUDE_WORKTREE_BASE"
            else
                _qc_row todo "Base repo" "${QUADCLAUDE_WORKTREE_BASE:-not set} — clone it or press [s]"
            fi
            if _qc_branch_exists "$base_repo" "$bb"; then
                _qc_row done "Base branch" "$bb"
            else
                _qc_row todo "Base branch" "$bb — not found in base repo (press [t] to learn more)"
            fi
        fi

        # This-quad section (worktree layout only — other layouts have no per-quad step).
        if [ "$QUADCLAUDE_LAYOUT" = "worktrees" ]; then
            echo ""
            echo "  ${_C_BLD}This quad${_C_RST} ${_C_DIM}(${label})${_C_RST}"
            if [ -d "$target" ]; then
                _qc_row done "Worktree folder" "$(basename "$target")"
            else
                _qc_row todo "Worktree folder" "$(basename "$target") — press [w] or [a]"
            fi
        fi

        echo ""
        if _quadclaude_should_onboard; then
            echo "  ${_C_DIM}Actions:${_C_RST}  ${_C_BLD}[s]${_C_RST} shared setup   ${_C_BLD}[w]${_C_RST} make this quad's worktree"
            echo "            ${_C_BLD}[a]${_C_RST} make all 4 worktrees   ${_C_BLD}[t]${_C_RST} worktree tutorial"
            echo "            ${_C_BLD}[r]${_C_RST} re-check   ${_C_BLD}[Enter]${_C_RST} skip & open a shell"
        else
            echo "  ${_C_GRN}All set for ${label}!${_C_RST}  ${_C_BLD}[Enter]${_C_RST} to start Claude   ${_C_BLD}[t]${_C_RST} tutorial"
        fi
        echo ""
        read -rp "  > " choice
        case "$choice" in
            s|S)
                local exe; if exe="$(_qc_exe)"; then
                    echo ""; "$exe" setup; echo ""
                    read -rp "  Setup done — press Enter to re-check… " _
                else
                    echo "  ⚠ QuadClaude app/CLI not found — build & install it first (see README),"
                    echo "    then run setup: QuadClaude.exe setup (Windows) or quadclaude setup (macOS)."
                    read -rp "  Press Enter… " _
                fi
                ;;
            w|W) _quadclaude_make_worktrees this ;;
            a|A) _quadclaude_make_worktrees all ;;
            t|T) _quadclaude_tutorial ;;
            r|R) : ;;   # loop re-renders
            "")  clear 2>/dev/null; return 0 ;;
            *)   : ;;
        esac
    done
}

# When this file is EXECUTED directly (e.g. `bash onboarding.sh` from the macOS
# zsh launcher, which can't safely source a bash script) rather than sourced,
# run the checklist immediately. When sourced (Windows claude-launch.sh),
# BASH_SOURCE[0] != $0, so this is a no-op and the launcher calls
# quadclaude_onboarding itself.
if [ "${BASH_SOURCE[0]}" = "${0}" ]; then
    quadclaude_onboarding
fi
