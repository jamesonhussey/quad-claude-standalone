---
description: Refresh this worktree with the latest base branch (fetch + fast-forward or rebase), without discarding your work.
argument-hint: "[optional base branch, e.g. main]"
---

Bring the current worktree up to date with the latest base branch — the same
freshening the launcher does when a quad opens, but on demand mid-session.
**Never discard uncommitted work.**

## Steps

1. Determine the **base branch**: first word of `$ARGUMENTS`, else auto-detect via
   `git symbolic-ref --short refs/remotes/origin/HEAD` (strip `origin/`), else `main`.
2. `git --no-pager status` — if there are **uncommitted changes**, stop and tell
   the user to commit or stash first (offer `git stash`). Do not proceed over dirty state.
3. `git fetch origin <base>`.
4. Decide how to sync based on where HEAD is:
   - **On a feature branch:** offer to `git rebase origin/<base>` (replays your
     commits on top of the latest base) OR `git merge origin/<base>`. Ask which the
     user prefers; default to rebase for a clean history. If a rebase conflicts,
     stop and help resolve — never leave conflict markers.
   - **Detached / on the base branch itself:** `git checkout --detach origin/<base>`
     to sit on the freshest base (this mirrors the launcher's behavior).
5. Report the new state: `git --no-pager log --oneline -3` and the branch/HEAD.

If anything is ambiguous or risky, ask before running it.
