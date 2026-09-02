---
description: Explain how QuadClaude works — the quad grid, glow/sound cues, worktrees, the push→base-branch flow, and how to turn off anything you don't like.
argument-hint: "[optional: a specific topic, e.g. 'glow colors' or 'turn off sounds']"
---

You are explaining the **QuadClaude** setup to the person running this command.
They may be new to it. Present the relevant parts below clearly and
conversationally (don't just dump the whole list) — and if they passed a topic
in `$ARGUMENTS`, focus on that. Offer to make any change for them at the end.

# What QuadClaude is

A launcher that opens **4 Claude Code terminals** snapped into a 2×2 grid, so
you can run four sessions at once. Each window is a "quad."

# The visual + sound cues

- **Glow border around a terminal:**
  - **Green** = Claude just finished (Stop). A soft sound plays too.
  - **Red** = Claude is waiting on you (a permission prompt or idle). An alert sound plays.
  - **Amber/orange** = Claude is actively working.
  - The glow clears the moment you submit your next prompt.
- **Status widget** per quad: shows the quad's label, current project, and git branch.

# One worktree per quad (the mental model)

- Each quad works in its **own git worktree** — a separate folder tied to the
  same repo, each on its own branch. Four quads = four branches in flight, no
  collisions, one shared git history.
- When a quad opens, the launcher **resets its worktree to the base branch**
  (e.g. `main`) so you always start from the latest code.
- Any quad that isn't fully set up shows an **onboarding checklist + a worktree
  tutorial** automatically — follow its prompts (or press `[t]` for the tutorial).

# The push → return-to-base flow

The bundled `CLAUDE.md` template suggests this habit (it's just a suggestion —
see "turning things off" below):

1. Work on a **feature branch**, never directly on the base branch.
2. Before pushing: optionally lint/build, then **self-review the diff**.
3. Push the feature branch and open a PR into the base branch.
4. **Return to the base branch** so the next task starts clean (the launcher
   also does this for you on the next open).

# Helper commands you now have

`/review-pr`, `/commit-and-pr`, `/sync-base`, `/new-worktree`,
`/clean-worktree`, `/quad-doctor`, and this one. They live as plain files in
`~/.claude/commands/` — delete any you don't want. Run `/quad-doctor` any time to
health-check your setup.

# Turning things off / making it yours

Nothing here is mandatory. To change behavior:

- **Re-run the wizard:** `QuadClaude.exe setup` — change projects dir, layout,
  base branch, sounds, permissions, etc.
- **Edit the config directly:** `%APPDATA%\QuadClaude\config.json`. Useful flags:
  - `soundsEnabled` — turn notification sounds off.
  - `idleOverlayEnabled`, `idleTintEnabled`, `partyModeEnabled`,
    `carouselModeEnabled` — the various overlay/idle effects.
  - `glowColorWorking` / `glowColorDone` / `glowColorNeedsInput` — glow colors.
  - `worktreeBaseBranch` — the branch worktrees reset to (default `main`).
  - `worktreeProvisionCommand`, `devServerSubdir` — optional per-repo setup for
    fresh worktrees (e.g. a code-gen step, a monorepo subfolder).
  - `permissionMode` / `allowList` — how much Claude may run without asking.
  - `mondayEnabled` — the Monday.com panel is **off by default**; only turn it
    on if you use Monday.
- **The push workflow (`CLAUDE.md`)** is a template. If the feature-branch /
  return-to-base flow doesn't fit your job, edit or delete `CLAUDE.md` in your
  repo — QuadClaude doesn't force it.

After explaining, ask if they'd like you to open `config.json`, adjust a
setting, or walk through creating their worktrees.
