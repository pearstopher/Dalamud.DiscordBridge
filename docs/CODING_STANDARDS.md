# Coding Standards

The codebase is inherited and inconsistent in places. These rules apply to **new and modified code**. Don't reformat untouched code: it bloats diffs and makes it harder to merge upstream changes.

## Language & project

- C# 14, `net10.0-windows`, **nullable reference types enabled**. Annotate honestly (`string?`) instead of suppressing with `!` or `null!`. `null!` is acceptable only for `[PluginService]` properties in `Service.cs`.
- Don't add `<NoWarn>` entries. The current list (`CA1416, CA1862, IDE0003, IDE0060`) is inherited.
- **Warning budget:** the baseline is recorded in `CLAUDE.md`. A change must not raise it. If you touch a method that has a warning, fix the warning.
- New NuGet packages need a reason in the issue/commit. Pin exact versions and commit the updated `packages.lock.json`. Dalamud already provides Newtonsoft.Json, Lumina, FFXIVClientStructs, and ImGui bindings, so don't add those as packages.

## Style

- Follow the surrounding file's layout: block-scoped namespaces (`namespace X { … }`), Allman braces, 4-space indentation.
- Naming: `PascalCase` for types, methods, properties, constants. `camelCase` for private fields and locals (no `_` prefix in this codebase). Async methods end in `Async` for **new** methods (existing `Send*Event` names stay as they are).
- Qualifying members with `this.` is optional (IDE0003 is off). Be consistent within a method.
- Prefer `var` when the type is obvious from the right-hand side.
- Use `string.IsNullOrEmpty`/`IsNullOrWhiteSpace` for checks, and string interpolation for messages.
- Don't commit commented-out code. Delete it; git history keeps it. Existing commented-out blocks can be removed when you're working in that function.
- Add XML doc comments (`/// <summary>`) on public members you add. Comments explain **why**, not what.
- One public type per file. File name matches the type. Queue payload types go in `Model/`.

## Architecture rules

- **Never block the game thread.** No `.Result`/`.Wait()`/`GetAwaiter().GetResult()` on network calls from Dalamud callbacks, UI `Draw`, or slash commands. Enqueue work or `Task.Run` it, and catch exceptions inside.
- **New relayable things go through the queue.** Add a `QueuedXivEvent` subclass and handle it in `RunMessageQueue` instead of calling Discord from an event handler. This preserves ordering and keeps the game thread free.
- **Game state access:** read `IClientState`/`IObjectTable`/`IPlayerState`/Excel data on the framework thread, or through `Service.Framework.RunOnFrameworkThread`. Prefer the existing `cachedPlayerName`/`cachedPlayerWorld` over re-reading.
- **Shared mutable state** touched from more than one context (game thread, queue thread, Discord gateway) must be synchronized (`lock` on a private object, or `Concurrent*` collections). Never hold a lock across `await`.
- **No `async void`** except Dalamud/UI event handlers whose signature requires it. Those must wrap their whole body in try/catch.
- **Every Dalamud subscription needs a matching unsubscription in `Dispose`.** Same for IPC registrations and command handlers.
- Use `Service.*` for Dalamud services. Don't stash `IDalamudPluginInterface` in new places.
- Reuse existing helpers: `SendGenericEmbed` for command replies, `GetOrCreateWebhookClient` for webhooks, `XivChatTypeExtensions.GetSlug/GetFancyName/GetBySlug` for chat types, `EnsureOwner` for permissions.
- Keep pure logic (parsing, formatting, mapping) in classes without Dalamud runtime dependencies so it can be unit tested (see [TESTING.md](TESTING.md)).

## Discord specifics

- **Every configuration-changing Discord command must call `await EnsureOwner(message.Author, message.Channel)`.**
- Validate argument counts and `return` after replying with an error embed.
- Command replies: `SendGenericEmbed(channel, text, title, EmbedColorFine | EmbedColorError)`.
- Update the `help` embed whenever you add or change a command.
- Webhook posts must pass `allowedMentions`. Don't let relayed game text ping `@everyone`/`@here`.
- Respect Discord limits: 2000 chars per message, 4096 per embed description, 80 chars for a webhook username. Truncate defensively.

## Configuration compatibility

- Users upgrade in place, so config changes must be **backward compatible**. New properties need a sensible default initializer, and older JSON must still deserialize.
- Don't rename or remove serialized properties. If you must, keep the old one as `[Obsolete]`, migrate in the plugin constructor, bump `Configuration.Version`, and `Save()`.
- Call `Config.Save()` immediately after mutating config.

## Logging & errors

- Use `Service.Logger` (`IPluginLog`), or the existing `static readonly IPluginLog Logger = Service.Logger;` pattern.
- Levels: `Verbose`/`Debug` for flow tracing, `Information` for rare noteworthy events, `Warning` for recoverable problems, `Error` for failures (pass the exception: `Logger.Error(ex, "…")`).
- **Privacy:** don't log chat message contents above `Debug`. **Never log the bot token**, webhook URLs, or webhook tokens.
- Catch exceptions at boundaries (event handlers, queue items, command handler) and log them. Don't swallow exceptions silently.

## Security

- Treat Discord message content as untrusted input. Validate before using it in URLs or config.
- Don't add features that execute game commands or send in-game chat from Discord without an explicit owner-approved issue. That's a ToS/safety boundary.

## Git

- Branch `issue/<n>-<slug>`. Commit `<imperative summary> (#n)` + Co-Authored-By trailer. Merge `--no-ff` with `Closes #n`. Never open PRs and never push upstream (see `CLAUDE.md`).
- Keep each change focused on its issue. Note unrelated problems you find as new issues instead of fixing them in passing.
