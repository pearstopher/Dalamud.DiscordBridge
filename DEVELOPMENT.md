# Development guide

How to build the plugin on your PC, load it into the game as a **dev plugin**, and check that a change works. Written for the owner and for Claude (who can build and unit-test, but can't run the game).

> **Plugin identity:** this fork is **Discord Chat Pears** (InternalName `DiscordChatPears`), so it installs alongside the official Discord Chat Bridge without clashing. Its commands use the `/dcp` prefix. On first load it imports the official plugin's settings once (read-only). **Disable the official plugin while testing**, though, or every chat line is relayed twice. Running both also means the `DiscordBridge.*` IPC endpoints belong to whichever loaded first (a warning in `/xllog`).

## 1. One-time setup

- **XIVLauncher with Dalamud.** Launch the game through XIVLauncher once, so Dalamud's files exist in `%APPDATA%\XIVLauncher\addon\Hooks\dev\`. The build reads Dalamud's DLLs from there. If XIVLauncher lives elsewhere, set `DALAMUD_HOME` to its `addon\Hooks\dev` folder.
- **The .NET 10 SDK.** `dotnet --list-sdks` should show `10.0.x` (`winget install Microsoft.DotNet.SDK.10` otherwise).
- **A test Discord server and bot** (keep testing out of your real server):
  1. Create a server you own, with a couple of text channels (e.g. `#fc-test`, `#ls-test`).
  2. At https://discord.com/developers/applications → **New Application** → **Bot**: reset and copy the token. Under **Privileged Gateway Intents**, enable **Message Content Intent** (needed for commands and for Discord → game relay).
  3. In game, `/dcp` → paste the token and your Discord username or user ID → **Save**. When the status says **Connected!**, click **Join my server** and add the bot to the test server.
  4. In a test channel: `xl!help`, then e.g. `xl!setchannel say,fc`.
  5. (Optional) A second Discord account, to test non-owner permissions.

## 2. Build

From the repo root:

```sh
dotnet build                          # Debug: Dalamud.DiscordBridge\bin\x64\Debug\DiscordChatPears.dll
dotnet build -c Release               # Release: bin\x64\Release\…  (+ packaged latest.zip)
dotnet test -c Release --no-build     # unit tests (after a Release build)
```

Use **Debug** for day-to-day testing in game. A good build ends with `0 Error(s)`, and the warning count must not exceed the baseline in `CLAUDE.md`.

## 3. Load it in game (first time only)

1. Start the game through XIVLauncher. **Disable the official Discord Chat Bridge** in `/xlplugins` if you have it.
2. `/xlsettings` → **Experimental** → **Dev Plugin Locations**: paste the **full path to the Debug DLL**, click **+**, then **Save and Close**:
   ```
   C:\Users\<you>\Documents\VSCode\ffxiv-discord-chat-bridge\Dalamud.DiscordBridge\bin\x64\Debug\DiscordChatPears.dll
   ```
3. `/xlplugins` → **Dev Tools → Installed Dev Plugins** → enable the plugin.
4. Recommended: in the dev plugin's settings, turn on **automatic reloading**. Dalamud then reloads the plugin about half a second after that DLL changes, so `dotnet build` is enough.

> Point the location at the **Debug** DLL. `dotnet build` only updates `bin\x64\Debug\`; if the location points at Release, the game keeps running whatever was last built as Release.

## 4. Check which build is running

`/xllog`, filter on `DiscordChatPears`. On every load the plugin logs:

```
Discord Chat Pears loaded (Debug build, built 2026-11-16 10:36:02, from C:\...\bin\x64\Debug\DiscordChatPears.dll)
```

and `... unloaded` when disabled. **Check this line each time you test.** If the build time is older than your last build, the game is running a stale DLL. Red `ERR` lines mentioning the plugin are bugs. The same log is in `%APPDATA%\XIVLauncher\dalamud.log` (attach it to issues). Set the log level to **Debug** in `/xllog` to see the plugin's routing messages.

Config file: `%APPDATA%\XIVLauncher\pluginConfigs\DiscordChatPears.json`. Back it up before experimenting; delete it to start fresh.

## 5. Edit → test loop

1. Change the code, then `dotnet build`.
2. With auto-reload on, the plugin reloads itself. Otherwise click **reload** in Installed Dev Plugins.
3. Confirm the new `loaded (...)` time in `/xllog`.
4. Test in game and in the test Discord server, watching `/xllog`.

If the build fails because the DLL is locked, disable the plugin in game, build, and re-enable.

## 6. Debug commands

Hidden from `/xlhelp`. Use them to exercise the pipeline without waiting for real chat.

| Command | What it does |
|---|---|
| `/dcpdebug <slug>` | Enqueues a fake "Test Message" from "Test Sender" as chat kind `<slug>` (e.g. `/dcpdebug fc`) → relayed to channels subscribed to that kind |
| `/dcpsaledebug` | Posts a fake retainer sale (Titanium Ore) to Discord and echoes it in game |
| `/dcpprintlist` | Prints every chat kind `(id) slug - name` to the chat log |

Planned with the two-way feature: `/dcpin <slug> <name> <text>` (#15) fakes an incoming Discord message, and `/dcpsend <slug> <text>` (#16) tests sending as your character.

## 7. What to check in game

Use [docs/TESTING.md § 3](docs/TESTING.md) as the checklist. Each issue's "Manual test plan" lists the specific steps. Issues that need a check in game carry the `needs-in-game-test` label:

```sh
gh issue list --label needs-in-game-test --state all
```

After testing, comment the result on the issue and remove the label.

## 8. CI

[.github/workflows/build.yml](.github/workflows/build.yml) builds and tests on every push to `master`, on demand, and **daily against the latest Dalamud**, so a game/Dalamud patch that breaks the plugin shows up as a failed run (GitHub emails the owner). The packaged `latest.zip` is uploaded as a 7-day artifact.

## 9. Releasing

Players install Discord Chat Pears from the owner's combined custom repo, [pearstopher/ffxiv-plugins](https://github.com/pearstopher/ffxiv-plugins):

```
https://raw.githubusercontent.com/pearstopher/ffxiv-plugins/main/pluginmaster.json
```

That repo lists every `pearstopher` repo with the **`dalamud-plugin`** topic (this one has it) that has a **`repo.json`** on its default branch. `repo.json` doesn't exist until the first release, so nothing is listed before then.

**One-time:** the repo secret **`PLUGIN_REPO_TOKEN`** (same token as Remove PF Duplicates uses) must exist, so the release can notify `ffxiv-plugins`: `gh secret set PLUGIN_REPO_TOKEN -R pearstopher/Dalamud.DiscordBridge`. Without it, the release still publishes, but the last step fails. You can then run *Update plugin list* manually in `ffxiv-plugins`.

**To release** (the owner decides when; **Claude never pushes release tags unless the owner asks for a specific release**):

1. `master` has everything, CI is green, and the in-game checks pass.
2. Set `<Version>` in `Dalamud.DiscordBridge/Dalamud.DiscordBridge.csproj` to the new version plus `.0` (`v2.1.0` → `2.1.0.0`), then commit and push.
3. Tag and push:
   ```sh
   git checkout master && git pull
   git tag v2.1.0
   git push origin v2.1.0
   ```
4. [.github/workflows/release.yml](.github/workflows/release.yml) then:
   - checks the tag matches `<Version>`, and stops if not
   - builds and tests
   - creates the GitHub Release with `latest.zip`
   - regenerates `repo.json` on `master` ([.github/scripts/make_repo_json.py](.github/scripts/make_repo_json.py))
   - fires `plugin-updated` at `ffxiv-plugins`, which rebuilds `pluginmaster.json`

If the version check fails, nothing was published: delete the tag (`git push origin :v2.1.0`, `git tag -d v2.1.0`), fix the csproj, and tag again. If a release goes wrong, release a new version rather than reusing the tag. `ReleaseVersionTests` fail if the csproj version is malformed or older than the one in `repo.json`.

## Troubleshooting

| Problem | Fix |
|---|---|
| Build error about Dalamud references | Run the game with Dalamud once, or set `DALAMUD_HOME`. |
| Plugin not listed under Installed Dev Plugins | The dev location must point at the `.dll` file itself, and it must exist. |
| Every message arrives in Discord twice | The official Discord Chat Bridge is still enabled. Disable it. |
| Status stays "Token empty or invalid" | Re-copy the bot token. Check `/xllog` for the login error. |
| Bot connects but ignores `xl!` commands | Message Content Intent is off in the developer portal, or your owner name/ID in `/dcp` doesn't match your Discord account. |
| Messages arrive as red FALLBACKMODE embeds | The bot lacks **Manage Webhooks** in that channel. |
| My change didn't take effect | Compare the `loaded (...)` build time with your last build. Point the dev location at Debug. |
