# CLAUDE.md

Personal fork of **Discord Chat Bridge** (`Dalamud.DiscordBridge`), a Dalamud plugin for FFXIV that relays in-game chat to Discord channels. The fork ships as **Discord Chat Pears** (InternalName `DiscordChatPears`, v2.x), a two-way bridge, from the owner's custom plugin repo.

- **Fork (ours):** `pearstopher/Dalamud.DiscordBridge` → git remote `origin`
- **Upstream (NOT ours):** `reiichi001/Dalamud.DiscordBridge` (itself a fork of `goaaats/Dalamud.DiscordBridge`) → git remote `upstream`, fetch-only

## ⛔ Hard rules — never break these

1. **Never open pull requests.** Not to upstream, not on the fork, not anywhere. Work is merged locally into `master` and pushed to `origin`.
2. **Never bother the upstream repository or its maintainers.** No issues, comments, reactions, discussions, PRs, releases, pushes, or API writes against `reiichi001/*`, `goaaats/*`, or `goatcorp/*` (that includes submitting to goatcorp's plugin repos). Read-only `git fetch upstream` is the only allowed interaction.
3. **Push only to `origin`.** The `upstream` remote's push URL is deliberately set to `DISABLED_never_push_upstream`; do not change it.
4. **All `gh` commands target the fork.** `gh repo set-default` is `pearstopher/Dalamud.DiscordBridge`; never pass `-R`/`--repo` pointing elsewhere.

These are enforced by [.claude/hooks/guard-upstream.ps1](.claude/hooks/guard-upstream.ps1) (PreToolUse hook) and deny rules in [.claude/settings.json](.claude/settings.json). If the hook blocks you, don't look for a workaround: the action is forbidden.

## Autonomous workflow (no human input)

The owner gives feature ideas; Claude turns them into GitHub issues and works through them unattended.

- **`/plan-issues <idea(s)>`**: turn the owner's plan into well-scoped issues labeled `claude`.
- **`/work-issue <number>`**: implement one issue end-to-end (branch → code → build/test → merge to master → push → comment → close).
- **`/work-queue`**: loop over every open `claude` issue (oldest first) until the queue is empty, then report back.

Labels:

| Label | Meaning |
|---|---|
| `claude` | Queued for autonomous work |
| `in-progress` | Claude is currently working on it |
| `needs-human` | Blocked on a decision only the owner can make; skip it and keep going |
| `needs-in-game-test` | Merged, but behaviour must be verified in FFXIV by the owner |
| `testing` | Test infrastructure or coverage |

Don't stop to ask questions mid-queue. If a decision is truly the owner's to make, leave a comment on the issue, label it `needs-human`, and move to the next issue. Make reasonable, documented choices for everything else.

## Build & verify

```powershell
dotnet build Dalamud.DiscordBridge.sln -c Release             # must be 0 errors
dotnet test Dalamud.DiscordBridge.sln -c Release --no-build    # all tests green
```

- Requires the .NET SDK (10.x is installed) and Dalamud dev files at `%AppData%\XIVLauncher\addon\Hooks\dev` (present on this machine). Project SDK: `Dalamud.NET.Sdk/15.0.0` (Dalamud API 15).
- Output: `Dalamud.DiscordBridge/bin/x64/Release/` (DLL). Packaged zip: `bin/x64/Release/DiscordChatPears/latest.zip`.
- Claude can't run FFXIV. Game- or Discord-dependent behaviour gets an **in-game self-test** (`SelfTests/GameSelfTests.cs`, dev builds: `/dcp` → Run self-tests or `/dcptest`), plus manual steps for anything that can't be automated, and the `needs-in-game-test` label.
- **Read the owner's latest in-game results** at `%APPDATA%\XIVLauncher\pluginConfigs\DiscordChatPears\selftest-results.json` (timestamped) before and after working on game-dependent code. A FAIL there is a bug to fix. Results older than the last relevant merge are stale.
- **Warning baseline: 13 warnings** (all nullable-reference warnings). Lower this number whenever a change reduces it. Don't add new warnings. Fix existing ones only when you're already touching that code.
- CI: `.github/workflows/build.yml` builds and tests on push to `master`, on demand, and daily. After pushing, check it with `gh run list --limit 3`; fix a red run before starting the next issue. Never push release tags (`v*`) unless the owner asks for a specific release; see DEVELOPMENT.md §9.
- In-game dev testing (loading the dev DLL, test Discord server, debug commands, reading `/xllog`): [DEVELOPMENT.md](DEVELOPMENT.md). Put in-game steps in issues in terms of that guide.
- This plugin is **not bound by Dalamud's plugin rules** (custom-repo distribution, owner decision in #9), but every feature must get a row in [docs/DALAMUD_RULES.md](docs/DALAMUD_RULES.md). Rule-breaking features must be opt-in and off by default.

## Project docs

Read these before implementing anything:

- [docs/README.md](docs/README.md) — index
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — how the plugin is structured and how a message flows
- [docs/CODING_STANDARDS.md](docs/CODING_STANDARDS.md) — style and conventions
- [docs/TESTING.md](docs/TESTING.md) — testing strategy and requirements
- [docs/ADDING_FEATURES.md](docs/ADDING_FEATURES.md) — the checklist for adding a feature
- [docs/DALAMUD_RULES.md](docs/DALAMUD_RULES.md) — per-feature Dalamud-rules compliance
- [docs/design/discord-to-game.md](docs/design/discord-to-game.md) — design and decision for the two-way (Discord → game) feature
- [DEVELOPMENT.md](DEVELOPMENT.md) — building, loading in game, debug commands, CI
- [docs/COMMANDS.md](docs/COMMANDS.md) / [docs/SETTINGS.md](docs/SETTINGS.md) — user-facing command and settings references. Keep them current: `DocumentationTests` fail when a command, setting or source file isn't documented.

## Git conventions

- Branch per issue: `issue/<number>-<short-slug>`, created from an up-to-date `master`. Branches stay local and are deleted after merge.
- Commit messages: imperative summary, reference the issue `(#N)`, and end with the Co-Authored-By trailer.
- Merge with `git merge --no-ff` and a message containing `Closes #N`, then `git push origin master`.
- Keep `<Version>` in the csproj unchanged unless an issue asks for a version bump (release tags `vX.Y.Z` must match `X.Y.Z.0`).
- Plugin identity is **Discord Chat Pears** (`DiscordChatPears`, set by `<AssemblyName>` in the csproj and `Dalamud.DiscordBridge/DiscordChatPears.json`). Don't change it unless the owner asks. The C# namespace and folder names stay `Dalamud.DiscordBridge`.
