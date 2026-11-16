---
name: work-issue
description: Implement a single GitHub issue on the fork end-to-end with no human input — branch, code, build/test, merge to master, push to origin, comment, close. Never opens a PR.
argument-hint: <issue-number>
---

# Work one issue: #$ARGUMENTS

Follow every step. Do not ask the user anything; this runs unattended. Re-read the **Hard rules** in CLAUDE.md: no pull requests, no contact with upstream.

## 1. Sync

```
git checkout master
git pull --ff-only origin master
git status --short          # must be clean; if not, stash with a descriptive name and note it in the issue
```

## 2. Understand the issue

- `gh issue view $ARGUMENTS --comments`
- If it has `needs-human`, or it depends on an open issue ("Depends on #X"), skip it and say why.
- Read the relevant docs in `docs/` (always `docs/ADDING_FEATURES.md`, `docs/CODING_STANDARDS.md`, `docs/TESTING.md` once they exist) and the code you'll touch.
- `gh issue edit $ARGUMENTS --add-label in-progress`

## 3. Branch and implement

- `git checkout -b issue/$ARGUMENTS-<short-slug>`
- Implement the smallest complete change that satisfies the acceptance criteria. Follow the coding standards and match the surrounding code.
- Add or update tests per `docs/TESTING.md` once a test project exists.
- Update docs (README, `docs/*`, in-plugin help text) if behaviour or configuration changes.

## 4. Verify

- `dotnet build Dalamud.DiscordBridge.sln -c Release`: 0 errors, warning count no higher than the baseline in CLAUDE.md.
- `dotnet test` if a test project exists: all green.
- Re-read your diff (`git diff master...HEAD`) against the acceptance criteria. Fix anything missing.
- If you can't get it green after a few genuine attempts, comment what you tried, label `needs-human`, remove `in-progress`, switch back to master (leave the branch), and stop this issue.

## 5. Commit, merge, push

```
git add -A
git commit -m "<imperative summary> (#$ARGUMENTS)" -m "<details>" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git checkout master
git merge --no-ff issue/$ARGUMENTS-<slug> -m "Merge issue #$ARGUMENTS: <title>" -m "Closes #$ARGUMENTS"
dotnet build Dalamud.DiscordBridge.sln -c Release      # sanity check on master
git push origin master
git branch -d issue/$ARGUMENTS-<slug>
```

Never `gh pr create`. Never push to `upstream`.

## 6. Report on the issue

Post one comment with `gh issue comment $ARGUMENTS --body-file <file>`:
- **What changed**: files and behaviour.
- **Commit**: merge commit SHA.
- **Verification**: build result and warning count, tests run.
- **Manual in-game test steps**: if behaviour can only be confirmed in FFXIV/Discord. If so, add the `needs-in-game-test` label.
- **Follow-ups**: link any new issues you created.

Then `gh issue edit $ARGUMENTS --remove-label in-progress --remove-label claude` and `gh issue close $ARGUMENTS` if it wasn't auto-closed.

## 7. Follow-up work you discover

- Bugs or necessary groundwork: create an issue labeled `claude` (+ `bug`/`enhancement`) so the queue picks it up.
- New feature ideas the owner didn't ask for: create an issue labeled `needs-human` (not `claude`) so the owner can approve it.
