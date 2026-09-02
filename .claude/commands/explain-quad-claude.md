---
description: Explain how QuadClaude works — the status-widget overlay & settings cog, the quad grid, glow/sound cues, worktrees, the push→base-branch flow, and how to change or turn off anything.
argument-hint: "[optional: a specific topic, e.g. 'the settings cog' or 'turn off sounds']"
---

You are explaining **QuadClaude** to the person who ran this command. Explain it
in your reply — never just point them at a file to read.

**If `$ARGUMENTS` names a topic:** focus on that topic.

**If `$ARGUMENTS` is empty (no topic given):** give the FULL walkthrough of every
section below, in order — do not abbreviate or skip any. You **must** fully
explain the section **"The status-widget overlay (and the settings cog ⚙)"** —
walk through every control on the bar and in the settings panel — *even though
they didn't ask about it specifically*. That overlay is what confuses people
most, and surfacing it proactively is a primary reason this command exists. Do
not decide it's "too much detail" and trim it; cover it in full.

Keep it well-organized (use the headings below), but **completeness beats
brevity** here — it's fine and expected for this to be long. Finish by offering to
make any change for them, and remind them they can ask about anything confusing
and can edit QuadClaude itself (the last two sections).

# What QuadClaude is

A launcher that opens **4 Claude Code terminals** snapped into a 2×2 grid, so
you can run four sessions at once. Each window is a "quad."

# The visual + sound cues

- **Glow border around a terminal:**
  - **Green** = Claude just finished (Stop). A soft sound plays too.
  - **Red** = Claude is waiting on you (a permission prompt or idle). An alert sound plays.
  - **Amber/orange** = Claude is actively working.
  - The glow clears the moment you submit your next prompt.

# The status-widget overlay (and the settings cog ⚙)

Every quad has a small floating widget bar. The left side is always visible; the
**gear (⚙)** opens a settings panel with the rest. This is the part most people
find confusing, so here's every control:

**The always-visible bar:**
- **Label** — an editable name for this quad. **Hidden by default**; turn it on
  with *Show label* in settings.
- **Dev-server pill `:3000`** — click it to open `http://localhost:<port>` in
  your browser. The dot beside it is grey when the server's off, lit when it's up.
  - **▶ (play)** — start this quad's dev server in a new terminal tab (runs your
    configured dev command, e.g. `npm run dev -- --port 3000`).
  - **■ (stop)** — stop that dev server.
- **Branch / project box** — the current git branch and folder for this quad.
- **Phase dot + text** (e.g. "Active") — click to cycle a manual status *you* set
  for yourself (a quick "where am I on this" marker); it also flashes the glow.
- **★ (focus star)** — promote this quad into the big pane when you're in a Focus
  layout.
- **Paste-image button** — paste a clipboard image straight into the Claude chat.
- **Explorer** — a file-tree browser for this quad's folder.
- **⚙ (gear)** — opens the settings panel below.
- **⋯ (more)** — overflow for buttons that don't fit when the widget is narrow.

**The settings panel (open the gear ⚙):**
- **Open Quad — Q1–Q4 / +5th** — jump focus to a specific quad, or spawn an extra
  5th session.
- **Monitor** — which display the grid uses.
- **Toggles** — show/hide each bar item: *Show label* (off by default),
  *Show explorer*, *Show phase*, *Show paste*, *Show Monday*.
- **Layout selector** — rearrange **all four** windows on the monitor:
  **2×2 Grid**, **4 Columns**, **Focus** (one big + the rest small), **Dual 2+2**,
  **Two-Up**, **Rows**. Just click one and the windows snap into it — try a few
  and keep whatever fits your screen.
- **Glow colors — W / D** — click the **W** (working) or **D** (done) swatch to
  change that glow's color, or the ✕ to disable it.
- **Idle overlay / Party mode / Carousel focus / Tint (+ slider)** — optional
  cosmetic behaviors for when a quad sits idle.
- **Size — S / M / L** — how big the widget bar itself is.
- **↻ Restart** — close and relaunch this quad's terminal.

If any of this doesn't behave the way it's described, that's fixable — it's your
code now (see "Make it your own" below).

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

# Ask about anything

If *any* part of QuadClaude is confusing or surprising — a button on the overlay,
a glow color, a setting, a behavior you didn't expect — just ask. There's no
wrong question here; it's better to ask than to guess.

# Make it your own (change QuadClaude itself)

QuadClaude is **your code now** — you're not stuck with how it ships. If something
isn't working, or you want a feature it doesn't have:

- **Open the repo in any editor** and change it. For VS Code:
  `code <path-to-quad-claude-standalone>`. The pieces:
  - `QuadClaude/` — the Windows C# app (overlay, glow, launcher logic)
  - `claude-launch.sh` / `onboarding.sh` — the per-quad launch + setup flow
  - `.claude/commands/` — these helper commands (plain markdown, edit freely)
  - `config.json` (`%APPDATA%\QuadClaude\`) — your settings
- **Rebuild after C# changes:**
  `cd QuadClaude && dotnet publish -c Release -r win-x64 --no-self-contained -o publish`
- **Or just tell me** what you want changed and I'll edit the code for you.

After explaining, ask if they'd like you to open `config.json`, adjust a setting,
walk through creating their worktrees, or change something about QuadClaude itself.
