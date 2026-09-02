# CLAUDE.md — push workflow (template)

> **This is a customizable template.** Replace the `<placeholders>` with your own
> project's values, delete anything you don't use, and commit it to your repo so
> Claude Code follows your habits. It ships intentionally minimal.

## Working habits

Sensible defaults — keep the ones you like, delete the rest.

- **Commit or push only when asked.** Make the changes, then stop and let a human
  inspect the diff before anything lands. (The push flow below assumes this.)
- **Keep changes focused and minimal.** Do what was asked — no unprompted refactors
  of surrounding code, and no unnecessary abstractions, comments, or docstrings.
- **Ask when it's ambiguous; plan first for anything complex.** If the intent isn't
  clear, ask rather than guess. For a non-trivial change, agree on a short plan
  before writing code.
- **Don't lint/build after every edit.** They're slow mid-task — run them as the
  pre-push gate (see [Before you push](#before-you-push)) or when explicitly asked,
  and commit often so you always have a rollback point.

## How worktrees fit in

Each quad runs in its own git worktree. When a quad opens, the launcher **resets
that worktree to `<base-branch>`** (see `onboarding.sh` for the tutorial). So:

- Start every task from a clean `<base-branch>` — the launcher handles this for you.
- Do your work on a feature branch, not directly on `<base-branch>`.
- One task/feature per quad keeps the four windows from stepping on each other.

## Before you push

A light pre-push habit — skip any step your project doesn't have:

1. **Lint** (optional): `<lint-command>` — warnings are usually fine, fix errors.
2. **Build** (optional): `<build-command>` — should pass clean.
3. **Self-review the diff**: skim what you're about to push.
   ```bash
   git --no-pager diff <base-branch>...HEAD
   ```
   Look for: secrets or tokens, debug logging left in, unintended file changes,
   anything you can't explain.

Only push once the diff looks right.

```bash
git push -u origin <your-feature-branch>
```

## After you push

Return to the base branch so the next task starts clean:

```bash
git checkout <base-branch>
```

(Open a PR from your feature branch into `<base-branch>` in your git host's UI.)

---

**Placeholders to fill in**

| Placeholder            | Meaning                                        |
|------------------------|------------------------------------------------|
| `<base-branch>`        | The branch worktrees are cut from (e.g. `main`)|
| `<your-feature-branch>`| The branch you're working on                   |
| `<lint-command>`       | e.g. `npm run lint` (or delete this step)       |
| `<build-command>`      | e.g. `npm run build` (or delete this step)      |
