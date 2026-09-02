---
description: Diagnose a QuadClaude setup — checks config, launch env, Claude CLI, worktrees, and hooks, then reports problems with suggested fixes.
argument-hint: ""
---

Run a health check on this machine's QuadClaude setup and report what's wrong (if
anything) with concrete fixes. This is read-only diagnosis — **don't change
anything**; suggest the fix and let the user run it.

Work through each check. In Git Bash, the Windows `%APPDATA%` folder is `$APPDATA`.

## Checks

1. **Claude CLI** — `claude --version` works and is on PATH.
2. **Shared config** — `$APPDATA/QuadClaude/config.json` exists and is valid JSON.
   Read it; note `projectsDir`, `layout`, `worktreeBase`, `worktreeBaseBranch`.
3. **Launch env** — `$APPDATA/QuadClaude/launch-env.sh` exists (written by `setup`
   / `launch`). If missing, setup hasn't been run or completed.
4. **Projects dir** — the `projectsDir` from config exists on disk.
5. **Hooks wired** — `~/.claude/settings.json` exists and references
   `QuadClaude.exe` in its hooks (Stop / UserPromptSubmit / Notification). If not,
   the glow/sound cues won't fire.
6. **Executable** — a built `QuadClaude.exe` exists (under the repo's
   `QuadClaude/publish/`). If not, it needs building.
7. **Worktrees** (only if `layout` is `worktrees`):
   - The base repo (`projectsDir/worktreeBase`) exists and is a git repo.
   - The base branch (`worktreeBaseBranch`) resolves in it (local or `origin/`).
   - Each quad's worktree folder exists — the pattern is `worktreePattern` with
     `{base}`→`worktreeBase` and `{n}`→1..4 (default `"{base} - Quad-{n}"`).
     `git -C <base> worktree list` shows what's actually registered.
8. **Helper commands** — which of the bundled commands are installed in
   `~/.claude/commands/` (`explain-quad-claude`, `review-pr`, `commit-and-pr`,
   `sync-base`, `new-worktree`, `quad-doctor`, `clean-worktree`).

## Report

Print a checklist (✓ / ✗ per item). For each ✗, give the one-line fix, e.g.:
- No config → `QuadClaude.exe setup`
- Missing worktree(s) → `/new-worktree`, or press `a` in a quad's onboarding menu
- Hooks not wired → re-run `QuadClaude.exe setup` (it merges settings.json)
- No executable → `cd QuadClaude && dotnet publish -c Release -r win-x64 --no-self-contained -o publish`

Finish with a one-line overall verdict (healthy / needs attention) and offer to
run any of the suggested fixes.
