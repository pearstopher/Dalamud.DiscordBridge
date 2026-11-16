# Architecture

This plugin listens to FFXIV chat (and a few game events) through Dalamud, queues each event, and a background worker posts it to whichever Discord channels the owner has subscribed to that chat kind. A Discord bot account (token supplied by the user) does the posting, normally through a per-channel **webhook** so each message shows the in-game sender's name and Lodestone portrait.

Relaying is one-way: FFXIV → Discord. Discord → FFXIV isn't supported. The only things read from Discord are the owner's configuration commands, plus webhook messages for the duplicate filter.

## Source map

All source is in `Dalamud.DiscordBridge/`.

| File | Responsibility |
|---|---|
| `DiscordBridgePlugin.cs` | `IDalamudPlugin` entry point. Loads/sanitizes config, creates every component, subscribes to Dalamud events, registers `/dcp` and debug commands, and caches the local player's name/world. Holds the static `DiscordBridgePlugin.Plugin` instance. |
| `Service.cs` | Static Dalamud service locator (`[PluginService]` properties: `Interface`, `State`, `PlayerState`, `ObjectTable`, `Chat`, `Data`, `Logger`, `Framework`), filled by `pluginInterface.Create<Service>()`. |
| `Configuration.cs` | `IPluginConfiguration` persisted by Dalamud as JSON. Bot token, owner, command prefix, per-channel subscriptions, prefixes, custom slugs, avatars, display toggles, duplicate window. |
| `Model/DiscordChannelConfig.cs` | Per-Discord-channel settings: subscribed `ChatTypes`, cached `WebhookId`, `IsContentFinder` (duty pop notifications). `IsBidirectional` exists but isn't used. |
| `Model/Queued*Event.cs` | Queue payloads. `QueuedXivEvent` (abstract base), `QueuedChatEvent`, `QueuedRetainerItemSaleEvent`, `QueuedContentFinderEvent`. |
| `DiscordMessageQueue.cs` | `ConcurrentQueue<QueuedXivEvent>` drained in order by one background task (semaphore-signalled, no busy-wait). Resolves sender name/world from SeString payloads, flattens SeString to Discord markdown, and dispatches to the right `DiscordHandler.Send*` method. |
| `DiscordHandler.cs` | Owns the `DiscordSocketClient`. Login/start, presence, **all Discord text commands** (`SocketClientOnMessageReceived`), `SendChatEvent` / `SendItemSaleEvent` / `SendContentFinderEvent`, webhook get-or-create, Lodestone avatar lookups (NetStone) with an in-memory cache. |
| `DuplicationFilter.cs` | `DuplicateFilter`: watches webhook messages in Discord and suppresses/deletes duplicates within `DuplicateCheckMS`. Useful when several players in the same FC/LS each run the plugin into the same channel. |
| `ContentParser.cs` | Regex parser for the posted format `{prefix}**[{slug}]** {text}`, used by the duplicate filter to compare messages. |
| `SpecialCharsHandler.cs` | Maps FFXIV private-use glyphs (U+E020–U+E0DB: HQ, auto-translate brackets, boxed letters/numbers, time icons…) to Unicode/emoji or custom guild emotes (`xivAtl`, `xivAtr`, `xivHq`). |
| `XivChatTypeExtensions.cs` | `TypeInfoDict`: the master table of supported `XivChatType` → slug (used in commands, e.g. `fc`, `ls1`) and display name. `GetInfo` masks with `& 0x7F`. Defines `IpcChatType = (XivChatType)99` for IPC messages. |
| `PluginCommandManager.cs` + `Attributes/` | Reflection-based registration of in-game slash commands from `[Command]`, `[HelpMessage]`, `[Aliases]`, `[DoNotShowInHelp]` attributes on methods of `DiscordBridgePlugin`. |
| `PluginUI.cs` | ImGui setup window (`/dcp`): token, owner name/ID, connection status, invite link. Save disposes and recreates `DiscordHandler`. |
| `API/` | IPC for other plugins: `DiscordBridge.APIVersion` (returns `1`) and `DiscordBridge.SendMessage(pluginName, avatarUrl, message)`. |
| `XivApi/` | Legacy XIVAPI client and result models. Mostly unused now (avatars come from NetStone; item icons are built as `beta.xivapi.com` asset URLs). |
| `Constant.cs` | Help/wiki/Discord/logo URLs. |
| `BuildInfo.cs` | Build configuration/time/path for the `… loaded (Debug build, built …, from …)` line logged at startup (see DEVELOPMENT.md). |
| `DiscordChatPears.json` | Plugin manifest metadata (name, author, description, tags). The InternalName comes from `<AssemblyName>DiscordChatPears` in the csproj. |
| `ConfigImport.cs` | One-time, read-only import of the official Discord Chat Bridge config (`pluginConfigs/Dalamud.DiscordBridge.json`) on first run, stripping its `$type` metadata. |

## Lifecycle

1. **Construct** (`DiscordBridgePlugin` ctor, game thread):
   - `Service` is injected.
   - `Configuration` is loaded. Legacy chat types are sanitized: values above 127 are masked to 7 bits, and types without a `TypeInfoDict` entry are removed.
   - The IPC provider and `DiscordHandler` are created. `DiscordHandler` creates the socket client, `SpecialCharsHandler`, `DiscordMessageQueue`, and `DuplicateFilter`.
   - `Task.Run` performs `Discord.Start()` (login, start the queue thread, create the Lodestone client) and sets idle presence. If already logged in, it caches the player name/world on the framework thread.
   - The UI draw hook, the chat/CF/login/logout/territory/framework events, and the slash commands are registered.
2. **Login**: caches player name/world, sets online presence, restarts the queue if it was stopped.
3. **Logout**: stops and clears the queue, sets idle presence.
4. **Territory change**: refreshes the cached player name/world.
5. **Dispose**: disposes IPC, `DiscordHandler` (stops the queue, logs out the socket), the command manager, and UI hooks, saves the config, and unsubscribes events.

## Message flow (chat line → Discord)

```
Game chat ─► IChatGui.ChatMessage ─► DiscordBridgePlugin.ChatOnOnChatMessage   (game thread)
               │  skip if IsHandled
               │  RetainerSale → QueuedRetainerItemSaleEvent, else QueuedChatEvent
               ▼
         DiscordMessageQueue.Enqueue (ConcurrentQueue)
               ▼  (queue thread, one event at a time, in order)
         RunMessageQueue
           ├─ QueuedChatEvent:
           │    resolve senderName/senderWorld (PlayerPayload, local-player cache, NPC/emote special cases)
           │    flatten SeString → text (_italic_, **glow**)
           │    └─► DiscordHandler.SendChatEvent
           ├─ QueuedRetainerItemSaleEvent: item row + icon URL ─► SendItemSaleEvent
           └─ QueuedContentFinderEvent ─► SendContentFinderEvent
               ▼
         SendChatEvent
           1. TellOutgoing is remapped to TellIncoming (both share slug "tell")
           2. avatar: explicit > ChatTypeAvatarURL[type] > DefaultAvatarURL > Lodestone portrait (NetStone, cached per name@world)
           3. channels = ChannelConfigs where ChatTypes contains type (none → return)
           4. SpecialCharsHandler.TransformToUnicode
           5. content = "{prefix}**[{customSlug|slug}]** {[displayName: ]message}"
           6. per channel (ResolveDeliveryAsync → ChooseRoute): DM → embed | ForceEmbedFallbackMode → embed |
                           no ManageWebhooks → error embed | else DuplicateFilter check → webhook
                              (username = "Name＠World", or the bot name when ForceDefaultNameAvatar is set)
```

Content-finder pops go to channels with `IsContentFinder` as an embed (duty name + image), sent through the webhook as "Dalamud Chat Bridge".

Other plugins can call IPC `DiscordBridge.SendMessage`, which enqueues a `QueuedChatEvent` with `ChatType = IpcChatType` (99). For that type, `SendChatEvent` omits the `**[slug]**` tag and the world.

## Discord commands

All commands are handled in `DiscordHandler.SocketClientOnMessageReceived`, a long `if (args[0] == prefix + "name" && await EnsureOwner(...))` chain. The prefix is `Configuration.DiscordBotPrefix`, default `xl!`. Messages from bots and webhooks are ignored. `EnsureOwner` matches `DiscordOwnerName` against `username#discriminator`, bare `username` (new-style accounts, discriminator `0000`), or a numeric user ID.

| Command | Effect |
|---|---|
| `setchannel <kinds>` / `unsetchannel <kinds>` | Add/remove chat types for this channel. `any` = `DefaultChatTypes`, `tell` = in+out, `p` = party+cross-party. |
| `listchannel` | Show this channel's chat types. |
| `setprefix <kinds> <prefix>` / `unsetprefix <kind>` | Per-type text/emoji prefix. |
| `setchattypename <kinds> <text>` / `unsetchattypename <kind>` | Override the `[slug]` label. |
| `setcfprefix <prefix\|none>` | Prefix for duty-pop posts. |
| `toggledf` | Toggle duty-pop notifications for this channel. |
| `toggleembed` | Global: post as embeds instead of webhooks. |
| `toggledefaultnameavatar` | Global: webhook uses the bot name/default avatar instead of the player's. |
| `togglesender` | Global: put `Name＠World:` in the message text. |
| `setduplicatems <ms>` | Duplicate-filter window. |
| `setavatar <kinds\|default> <url>` / `unsetavatar <kind\|default>` | Per-type fallback avatar. |
| `help` | Embed listing every command. **Keep it in sync when adding commands.** |

## Threading model

| Context | What runs there | Rules |
|---|---|---|
| Game/framework thread | Dalamud event callbacks, ImGui `Draw`, slash commands | Must return quickly. Never block on network I/O. Game state (`IClientState`, `IObjectTable`, `IPlayerState`, Excel sheets) is safest read here; use `Service.Framework.RunOnFrameworkThread` from elsewhere. |
| Queue task | `DiscordMessageQueue.RunAsync`: one background task that sleeps on a `SemaphoreSlim` until `Enqueue` signals it, and is cancelled by `Stop()` | Serial processing keeps message order. Each event is processed in its own try/catch, so one failure never ends the loop. `Start()` is idempotent, and `Stop()` waits up to 2 s for an in-flight event. |
| Discord.Net gateway | `Ready`, `MessageReceived` handlers (commands, duplicate filter) | Don't block the gateway. Shared state touched here and on the queue task needs synchronization (e.g. `DuplicateFilter` locks its recent-message set and never holds the lock across `await`). |

`Configuration` is mutated from both the gateway (commands) and the queue (webhook ID caching, removing dead channels) without locking. Be careful when adding new mutation points.

## Persistence

- Dalamud serializes `Configuration` with Newtonsoft.Json to `%AppData%\XIVLauncher\pluginConfigs\Dalamud.DiscordBridge.json`.
- `Config.Save()` is called right after every mutation.
- Dictionaries are keyed by `XivChatType` (enum) or Discord channel ID (`ulong`).
- `Version` exists but no migrations are implemented. The constructor's chat-type sanitizing is the only upgrade logic.
- The bot token is stored in plain text in that file (standard for Dalamud plugins). Never log it.

## External services

| Service | Used for | Notes |
|---|---|---|
| Discord gateway/REST (Discord.Net) | Bot login, commands, webhooks, embeds | Intents: `AllUnprivileged`, `GuildMessages`, `GuildWebhooks`, `MessageContent` (privileged, must be enabled in the Discord developer portal). Invite permission integer `2684742720`. |
| Lodestone (NetStone) | Player portraits | Network scrape per unseen `name@world`. Cached in a static `ConcurrentDictionary` for the process lifetime. |
| `beta.xivapi.com` asset API | Item icons for retainer sales | URL built directly, no request made by the plugin. |
| `xivapi.com` | Duty images (`/i/{folder}/{image}.png`), legacy `XivApiClient` | |
| goatcorp DalamudAssets | Default logo/avatar | `Constant.LogoLink` |

## Known issues

None open from the initial audit (#3–#8 fixed). Check `gh issue list --label bug`.
