---
name: plan-issues
description: Turn the owner's feature plan into well-scoped GitHub issues on the fork, labeled `claude` so /work-queue can implement them unattended.
argument-hint: <feature plan text>
---

# Plan issues from the owner's feature list

Input: $ARGUMENTS

1. Read `docs/ARCHITECTURE.md` and `docs/ADDING_FEATURES.md`, plus the code the features touch, so each issue names real files and classes.
2. Split the plan into issues that can each be merged independently (one coherent change, ideally under ~400 changed lines). Put shared groundwork in its own issue first.
3. Ask the owner only about real product decisions (behaviour they'd notice). Settle technical details yourself and write them into the issue.
4. Create each issue on the fork (never upstream) with `gh issue create --label claude --label <enhancement|bug|documentation|testing> --title ... --body-file ...`. Body template:

```
## Goal
<one paragraph: what the user gets>

## Context
<relevant files/classes, current behaviour>

## Acceptance criteria
- [ ] ...
- [ ] Builds with 0 errors and no new warnings
- [ ] Tests added/updated per docs/TESTING.md
- [ ] Docs/help text updated if user-facing

## Implementation notes
<approach, config fields, UI changes, edge cases>

## Dependencies
Depends on #X   (omit if none)

## Manual test plan
<steps the owner can run in-game, if applicable>
```

5. Reply with a table of the created issues (number, title, dependencies) and offer to start `/work-queue`.
