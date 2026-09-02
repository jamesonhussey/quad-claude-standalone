---
description: Create an additional git worktree from the base branch — for an ad-hoc extra task or to recreate a deleted quad folder.
argument-hint: "[optional name/branch, e.g. quad-5 or my-feature]"
---

Guide the user through adding a new git worktree cut from the base branch. A
worktree is a separate folder tied to the same repo, on its own branch — so it
can be worked in parallel with the others.

## Steps

1. **Find the base repo.** If the current directory is a git repo (or worktree),
   use its main repository. If not, ask the user for the path to the base repo.
   Confirm with `git -C <repo> rev-parse --git-dir`.
2. **Determine the base branch:** auto-detect via
   `git -C <repo> symbolic-ref --short refs/remotes/origin/HEAD` (strip `origin/`),
   else `main`. Confirm it with the user.
3. **Determine the name/branch:** use `$ARGUMENTS` if given, else ask. This becomes
   both the new branch name and (by default) the folder name. To match QuadClaude's
   quad folders, offer the pattern `"<repo-name> - Quad-<n>"` for the folder while
   using a branch like `quad-<n>`.
4. **Determine the location:** by default a sibling folder next to the base repo.
   Confirm the full target path with the user.
5. **Create it:**
   ```
   git -C <repo> fetch origin <base>
   git -C <repo> worktree add "<target-folder>" -b <branch> <base>
   ```
   If the branch already exists, drop `-b`. If the folder already exists, stop and
   tell the user.
6. **Confirm:** `git -C <repo> worktree list` and report the new folder + branch.

Keep it safe: never delete or overwrite an existing folder, and confirm the
target path before creating anything.
