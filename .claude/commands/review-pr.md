---
description: Review the current branch's changes (vs the base branch) like a thorough PR reviewer — correctness, security, tests, and clarity — with prioritized findings.
argument-hint: "[optional base branch, e.g. main] [optional PR number]"
---

Do a careful, codebase-agnostic review of the pending changes on the current
branch. This is read-only analysis — **do not modify code.**

## 1. Establish the diff

- If `$ARGUMENTS` contains a PR number and the `gh` CLI is available, review that
  PR: `gh pr diff <number>`.
- Otherwise review the local branch against its base branch:
  - Base branch = the first branch name in `$ARGUMENTS`, else auto-detect:
    try `git symbolic-ref --short refs/remotes/origin/HEAD` (strip `origin/`),
    else fall back to `main`, then `master`.
  - Run `git --no-pager diff <base>...HEAD` and `git --no-pager log <base>..HEAD --oneline`.
- If there's no diff, say so and stop.

## 2. Review for

- **Correctness** — logic errors, off-by-one, null/undefined, wrong conditions,
  edge cases, race conditions, resource leaks.
- **Security** — hardcoded secrets/tokens, injection (SQL/command/path), missing
  authz checks, unsafe deserialization, secrets in logs, silent error-swallowing.
- **Error handling** — failures that are ignored or masked; missing validation.
- **Tests** — is new behavior covered? Are there obvious cases left untested?
- **Clarity / maintainability** — dead code, confusing naming, duplication,
  leftover debug output, unintended file changes.

Match the surrounding code's conventions — don't flag style the project clearly
accepts.

## 3. Report

Group findings by priority and be concrete (file + line + why it matters):

- **MUST FIX** — bugs, security issues, breakage.
- **SHOULD FIX** — real problems that aren't blocking.
- **CONSIDER** — optional improvements.

For each, give a one-line fix suggestion. End with a one-word recommendation:
**APPROVE**, **APPROVE WITH CHANGES**, or **REQUEST CHANGES**, and a one-sentence
rationale. If you found nothing substantive, say so plainly.
