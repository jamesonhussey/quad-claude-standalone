---
description: Safely remove a git worktree (and optionally its branch) — the inverse of /new-worktree — without losing unsaved work.
argument-hint: "[optional worktree path or name to remove]"
---

Remove a git worktree cleanly. **Never remove the main working tree, and never
discard unmerged or unpushed work without the user's explicit OK.**

## Steps

1. **List worktrees:** `git worktree list`. Identify the main repo (the first entry)
   vs the linked worktrees.
2. **Pick the target:** match `$ARGUMENTS` (a path or folder name) against the list,
   else ask the user which worktree to remove. Refuse to remove the **main** working
   tree — that's the repo itself.
3. **Safety checks on the target worktree:**
   - Uncommitted changes: `git -C <target> status --porcelain`. If non-empty, stop
     and warn — offer to commit or stash first.
   - Unpushed commits: check whether its branch is ahead of its upstream
     (`git -C <target> log --oneline @{u}..` or compare to `origin/<base>`). If it
     has commits that exist nowhere else, warn clearly and get explicit confirmation
     before proceeding.
4. **Remove:** confirm with the user, then `git worktree remove "<target>"`
   (only use `--force` if the user explicitly accepts losing the safety checks).
5. **Branch cleanup (optional):** ask whether to also delete the worktree's branch.
   If yes and it's safe, `git branch -d <branch>` (use `-D` only on explicit
   confirmation that unmerged commits can be dropped).
6. **Prune + report:** `git worktree prune`, then show the updated
   `git worktree list`.

If anything is ambiguous or risky, ask before acting.
