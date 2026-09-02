---
description: Stage and commit the current work with a clean message, push the branch, and open a PR into the base branch.
argument-hint: "[optional base branch, e.g. main]"
---

Take the current work from uncommitted changes to an open PR. Codebase-agnostic.
**Confirm with the user before the two irreversible steps (push, PR).**

## 1. Assess

- `git --no-pager status` and `git --no-pager diff` (plus `git --no-pager diff --staged`).
- If there's nothing to commit and nothing unpushed, say so and stop.
- Determine the **base branch**: first word of `$ARGUMENTS`, else auto-detect via
  `git symbolic-ref --short refs/remotes/origin/HEAD` (strip `origin/`), else `main`.
- Determine the **current branch**. If it IS the base branch, stop and warn — the
  user should be on a feature branch. Offer to create one (`git checkout -b <name>`).

## 2. Commit

- Review the diff and write a concise, conventional commit message (a clear
  subject line; a short body only if the change needs explaining).
- Show the user the message and the list of files. Stage the appropriate files
  (`git add`) and commit. Don't add unrelated files; call out anything surprising.

## 3. Push — confirm first

- `git push -u origin <current-branch>`.

## 4. Open the PR — confirm first

- If `gh` is available: `gh pr create --base <base> --head <current-branch>`
  with a title from the commit subject and a short body summarizing the change.
  Surface the PR URL.
- If `gh` isn't available, print the branch name and the compare URL hint so the
  user can open the PR in their git host's UI.

Never force-push, never skip hooks, and never commit secrets — if the diff
contains anything that looks like a credential, stop and flag it.
