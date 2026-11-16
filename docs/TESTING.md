# Testing

Verification has three levels. Every change needs level 1. Level 3 is the owner's job: Claude can't run FFXIV, so it writes the steps into the issue and labels it `needs-in-game-test`.

## 1. Build verification (always)

```powershell
dotnet build Dalamud.DiscordBridge.sln -c Release
```

- Must report **0 errors**.
- Warning count must not exceed the baseline in `CLAUDE.md` (13 at the time of writing). Compare the `N Warning(s)` line before and after your change.
- Requires Dalamud dev assemblies at `%AppData%\XIVLauncher\addon\Hooks\dev\` (the SDK's `DalamudLibPath`). If they're missing, the build fails resolving `Dalamud.dll`. Installing XIVLauncher with Dalamud provides them.
- Output: `Dalamud.DiscordBridge/bin/x64/Release/` (DLL and dependencies; point Dev Plugin Locations here). A packaged `latest.zip` and manifest are written to `bin/x64/Release/DiscordChatPears/`.

## 2. Unit tests

**Layout:** `Dalamud.DiscordBridge.Tests/` (xUnit 2.9, `net10.0-windows`, x64) is in the solution and references the plugin project. The plugin references Dalamud assemblies with `Private=false`, so the test project adds its own copy-local references to `Dalamud`, `Lumina`, `Lumina.Excel`, `FFXIVClientStructs`, and `Newtonsoft.Json` from `$(DalamudLibPath)`. That's `DALAMUD_HOME` when set (CI), otherwise `%AppData%\XIVLauncher\addon\Hooks\dev\`. The plugin's own output is unaffected. The plugin csproj has `InternalsVisibleTo Dalamud.DiscordBridge.Tests`, so internal types are testable.

```powershell
dotnet build Dalamud.DiscordBridge.sln -c Release
dotnet test Dalamud.DiscordBridge.sln -c Release --no-build
```

If a test needs a Dalamud assembly that isn't listed, add another `<Reference … Private="true" />` to the test csproj.

**What is unit-testable** (no live Dalamud services or Discord connection needed):

| Unit | What to test |
|---|---|
| `ContentParser` | prefix/slug/text groups, non-matching input, `ParseText` fallback |
| `SpecialCharsHandler.TransformToUnicode` | mapped glyphs, boxed letters (regional indicators + ZWSP), number squares, passthrough, emote substitution |
| `XivChatTypeExtensions` | `GetSlug`/`GetFancyName`/`GetBySlug`, `& 0x7F` masking, unknown type throws `ArgumentException` |
| `DiscordChannelConfig` | `SetUnique` de-duplication, `UnsetUnique` |
| `Configuration` | JSON round-trip and backward compatibility (deserialize an old-shape JSON sample) |
| Extracted pure helpers | e.g. message-content formatting, sender-name resolution, duplicate comparison |

**Not unit-testable as-is:** anything that touches `Service.*` at runtime, `DiscordSocketClient`, webhooks, NetStone, or ImGui. **Design guidance:** when you change logic in those areas, extract the decision-making into a pure `internal static` method (inputs → output) and test that, leaving a thin I/O shell. Example: build the `"{prefix}**[{slug}]** {text}"` string in a static formatter rather than inline in `SendChatEvent`.

**Rules:**
- Bug fixes: add a test that fails without the fix whenever the logic is reachable without I/O.
- New pure logic: test the happy path, edge cases (empty strings, null, unknown enum values), and anything you'd otherwise check by reading the code.
- Test names: `Method_Scenario_ExpectedResult`. One behaviour per test, Arrange/Act/Assert.
- Tests must be deterministic: no network, no real clock dependence (inject the time), no file system outside a temp dir.

## 3. In-game self-tests (dev builds)

`/dcp` → **Run self-tests** (or `/dcptest`) runs the automated in-game checks and writes `pluginConfigs\DiscordChatPears\selftest-results.json`. See [DEVELOPMENT.md §7](../DEVELOPMENT.md). When results exist, Claude reads that file to verify game-dependent behaviour. Every game- or Discord-dependent feature should add a self-test (`SelfTests/GameSelfTests.cs`).

## 4. Manual in-game checklist (owner)

Use the relevant parts for each change. Put the exact steps in the issue's "Manual test plan".

**Setup:** follow [DEVELOPMENT.md](../DEVELOPMENT.md) §1–4 (test Discord server, load the Debug DLL as a dev plugin, confirm the `loaded (...)` line in `/xllog`).

| Area | Check |
|---|---|
| Startup | Plugin loads without errors in `/xllog`, and the bot shows Idle at the title screen and Online after login |
| Relay | `xl!setchannel say` in a test channel. A `/say` line in-game appears with the correct name＠world and portrait |
| Debug commands | `/dcpdebug say` posts "Test Message". `/dcpsaledebug` posts a retainer sale with the item icon |
| Formatting | Auto-translate brackets, HQ icon, italics render sensibly. `xl!togglesender`, `xl!toggleembed`, `xl!toggledefaultnameavatar` behave as described |
| Duty pop | `xl!toggledf`, queue for a duty, and an embed appears on pop |
| Permissions | A non-owner running a command gets the "not allowed" embed |
| Lifecycle | Logout → no relaying, Idle. Login → relaying resumes. Disable/enable the plugin in `/xlplugins` → messages appear exactly once |
| IPC | Another plugin calling `DiscordBridge.SendMessage` posts without a `[slug]` tag |
| Logs | No new errors in `/xllog`, and no token or chat contents logged above Debug |
