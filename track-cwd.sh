#!/bin/bash
# Writes current cwd + git branch to the QuadClaude state file.
# Called by Claude Code hooks to keep the StatusWidget up to date.
# Usage: track-cwd.sh [quad-index]

qi="${QUAD_INDEX:-${1:-0}}"
state_dir="${APPDATA}/${QUADCLAUDE_INSTANCE:-QuadClaude}"
state_file="$state_dir/quad-${qi}.cwd.json"
cwd="$(pwd)"
project="$(basename "$cwd")"
branch=""

# On a DETACHED HEAD, name the base ref (prefer staging/main) as "<base> (detached)" instead of a bare
# SHA that reads as a wrong branch; fall back to "detached (<sha>)".
if [ -d ".git" ] || git rev-parse --git-dir &>/dev/null; then
    branch="$(git symbolic-ref --short HEAD 2>/dev/null)"
    if [ -z "$branch" ]; then
        base="$(git for-each-ref --points-at HEAD --format='%(refname:short)' refs/heads refs/remotes/origin 2>/dev/null | sed 's#^origin/##' | grep -Ex 'staging|main|master' | head -1)"
        [ -z "$base" ] && base="$(git for-each-ref --points-at HEAD --format='%(refname:short)' refs/heads refs/remotes/origin 2>/dev/null | sed 's#^origin/##' | head -1)"
        if [ -n "$base" ]; then branch="$base (detached)"; else branch="detached ($(git rev-parse --short HEAD 2>/dev/null))"; fi
    fi
fi

# Preserve the sessionId a prior C# track hook wrote (this script has none of its own).
sid=""
if [ -f "$state_file" ]; then
    sid="$(sed -n 's/.*"sessionId"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$state_file" 2>/dev/null | head -1)"
fi

mkdir -p "$state_dir" 2>/dev/null
tmp="$state_file.tmp"
printf '{"cwd":"%s","project":"%s","branch":"%s","sessionId":"%s"}' \
    "$(echo "$cwd" | sed 's/\\/\\\\/g; s/"/\\"/g')" \
    "$(echo "$project" | sed 's/"/\\"/g')" \
    "$(echo "$branch" | sed 's/"/\\"/g')" \
    "$(echo "$sid" | sed 's/"/\\"/g')" \
    > "$tmp" 2>/dev/null && mv -f "$tmp" "$state_file" 2>/dev/null
