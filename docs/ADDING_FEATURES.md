# Adding Features

## General checklist (every feature)

1. **Issue first.** The work has an issue with acceptance criteria (`/plan-issues` creates them). One issue = one mergeable change.
2. **Read** [ARCHITECTURE.md](ARCHITECTURE.md) for the area you're touching, plus [CODING_STANDARDS.md](CODING_STANDARDS.md).
3. **Branch** `issue/<n>-<slug>` from an up-to-date `master`.
4. **Design for testability.** Put decisions and formatting in pure helpers, and keep Discord/Dalamud calls in a thin shell.
5. **Implement** following the recipes below.
6. **Config** changes must be backward compatible: give new fields defaults and `Save()` after mutation.
7. **User-facing text:** update the Discord `help` embed, the `/dcp` UI text, `README.md`, **`docs/COMMANDS.md`** (any command) and **`docs/SETTINGS.md`** (any setting). `DocumentationTests` fail until new commands, settings and source files are documented.
8. **Rules:** add or update the feature's row in [DALAMUD_RULES.md](DALAMUD_RULES.md). Anything that breaks the rules must be opt-in, off by default, and say so to the user.
9. **Self-test:** if the feature's behaviour depends on the live game or Discord, add an in-game self-test to `SelfTests/GameSelfTests.cs` (never sending to the game server; use Echo) and reference its id in `docs/IN_GAME_TEST_PLAN.md`.
10. **Verify** per [TESTING.md](TESTING.md): build (0 errors, warnings ≤ baseline) and tests. Write manual in-game steps in the issue, referring to [DEVELOPMENT.md](../DEVELOPMENT.md) (debug commands, `/xllog`), and add a debug command if the feature is otherwise hard to trigger.
11. **Merge** locally (`--no-ff`, `Closes #n`), push to `origin`, comment, and close. **No PRs. Never touch upstream.**

## Recipes

### Relay a new chat type

1. `XivChatTypeExtensions.cs`: add a `TypeInfoDict` entry with a unique, short, lowercase `Slug` (it's what users type) and a `FancyName`. Types without an entry are **deleted from user configs at startup** by the constructor's sanitizer, so the entry is required.
2. If it should be part of `xl!setchannel any`, add it to `DefaultChatTypes` in `DiscordHandler.cs`.
3. If the sender has no `PlayerPayload` (system/NPC-style), add a case to the sender-resolution `switch` in `DiscordMessageQueue.RunMessageQueue` so it doesn't log a `playerLink was null` error. Consider whether `SendChatEvent` should skip the Lodestone lookup (see the `Echo`/61/68 cases).
4. Test: slug lookup round-trips. Manual: `/dcpdebug <slug>` posts to a subscribed channel.

### Relay a new kind of game event (not a chat line)

1. Add `Model/Queued<Thing>Event.cs : QueuedXivEvent` with `required` properties for everything the sender needs. Capture game data **on the game thread** when you enqueue.
2. Subscribe to the Dalamud event in the `DiscordBridgePlugin` constructor, enqueue the event, and **unsubscribe in `Dispose`**.
3. Add an `is Queued<Thing>Event` branch in `RunMessageQueue`, wrapped in its own try/catch.
4. Add `DiscordHandler.Send<Thing>EventAsync`. Choose target channels the way existing senders do (a `DiscordChannelConfig` flag or chat-type membership), and follow the DM → embed fallback → webhook pattern (use `ResolveDeliveryAsync` + a `switch` on `DeliveryRoute`).
5. If it needs a per-channel toggle, add a `bool` to `DiscordChannelConfig` (default `false`) and a `toggle<thing>` command (next recipe).

### Add a Discord text command

1. In `DiscordHandler.SocketClientOnMessageReceived`, add a block:
   ```csharp
   if (args[0] == this.plugin.Config.DiscordBotPrefix + "mycommand" &&
       await EnsureOwner(message.Author, message.Channel))
   {
       if (args.Length < 2)
       {
           await SendGenericEmbed(message.Channel, $"Usage: ``{this.plugin.Config.DiscordBotPrefix}mycommand <arg>``", "Error", EmbedColorError);
           return;
       }
       // validate, mutate config, this.plugin.Config.Save();
       await SendGenericEmbed(message.Channel, "OK! …", "Title", EmbedColorFine);
       return;
   }
   ```
2. **`EnsureOwner` is mandatory** for anything that changes state or reveals configuration.
3. Command names are lowercase with no separators (`togglesender`). Check that the name doesn't prefix-collide with an existing command.
4. Add a `.AddField(...)` line to the `help` embed. Embeds allow at most 25 fields, and there are 21 today. If you'd go past 25, split help into pages or categories as part of the issue.
5. Put argument parsing and validation in a pure helper and unit test it.

### Add an in-game slash command

1. Add a method on `DiscordBridgePlugin` with signature `void Name(string command, string args)` and the attributes `[Command("/pname")]`, `[HelpMessage("…")]`, optionally `[Aliases(...)]`, plus `[DoNotShowInHelp]` for debug commands. `PluginCommandManager` registers and unregisters it automatically.
2. Keep it non-blocking: enqueue or `Task.Run` any network work.

### Add a configuration option

1. `Configuration.cs`: add a property with a default initializer. Global settings go on `Configuration`, per-channel ones on `DiscordChannelConfig`, per-chat-type ones in a `Dictionary<XivChatType, T>`.
2. Expose it through a Discord command, the `/dcp` UI, or both. Settings needed before the bot connects (like the token) must be in the UI.
3. Read it where needed through `this.plugin.Config` / `DiscordBridgePlugin.Plugin.Config`.
4. Add a deserialization test with JSON that lacks the field, and confirm the default applies.

### Change the setup UI (`PluginUI`)

- ImGui runs every frame on the game thread. No I/O in `Draw()`. Copy config into local fields in `Show()` (as `token`/`username` are), and write back only on Save.
- If you split the UI into windows/tabs, consider migrating to Dalamud's `WindowSystem`. That should be its own issue.

### Add or change IPC (`API/`)

1. Add the method to `IDiscordBridgeAPI` and `DiscordBridgeAPI`. Register it in `DiscordBridgeProvider` with a `DiscordBridge.<Name>` label, and unregister it in `Dispose`.
2. **Never change existing IPC signatures**, since other plugins depend on them. Add new labels instead, and bump `APIVersion` when adding capabilities.
3. IPC messages enqueue with `XivChatTypeExtensions.IpcChatType`.

### Change how messages look

- The content format `{prefix}**[{slug}]** {text}` is parsed back by `ContentParser` for duplicate detection. If you change the format, update `ContentParser` and its tests in the same change, or the duplicate filter breaks silently.
- Glyph mappings live in `SpecialCharsHandler.BuildDict`.

## Things that need owner sign-off (label `needs-human`, don't implement autonomously)

- Anything that sends in-game chat or executes game commands **beyond** what #9 approved (local display, and owner-only opt-in "send as me")
- New network services or data leaving the machine beyond what's listed in ARCHITECTURE.md
- Removing or renaming user-visible commands or config fields
- Changing the plugin manifest identity (`InternalName`, `Name`, `Author`, `RepoUrl`) or publishing/releases
