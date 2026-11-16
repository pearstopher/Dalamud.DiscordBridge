# Settings reference

Settings live in `%APPDATA%\XIVLauncher\pluginConfigs\DiscordChatPears.json`, saved as soon as you change them. Most are changed in the `/dcp` window or with Discord commands ([COMMANDS.md](COMMANDS.md)). **`xl!setup`** sets the channel and display settings below to sane values in one go (it changes `ChatTypes`, `IsContentFinder`, `InboundChatType`, `InboundPermission`, `InboundRoleId`, and the display options `InboundDisplayEnabled`, `InboundDisplayAsEcho`, `InboundReactions`, `MarkSentInGame`, `SenderInMessage`, `ForceEmbedFallbackMode`, `ForceDefaultNameAvatar`, `DuplicateCheckMS`). A few are only in the file: edit them with the game closed (or the plugin disabled), then reload.

On first load, settings are imported once from the official Discord Chat Bridge (`Dalamud.DiscordBridge.json`, read-only) if it exists.

## `/dcp` window

| Option | Setting | Default | What it does |
|---|---|---|---|
| Enter your bot token | `DiscordToken` | empty | The Discord bot's token, hidden unless you tick **Show**. Click **Save** to reconnect (in the background; the status shows *Reconnecting…*). Keep it secret. |
| Enter your Username | `DiscordOwnerName` | empty | Your Discord username, `name#1234`, or numeric user ID. Owner-only commands and send-as-me check this. |
| Show Discord messages in game chat | `InboundDisplayEnabled` | on | Master switch for showing Discord messages in your chat log (channels still need `setinbound`). |
| Show them as Echo instead of the channel's own colour | `InboundDisplayAsEcho` | off | Print every inbound message as a labelled Echo line, `[FC] [D] Name: text`, instead of mimicking the channel. |
| React on Discord when a message reaches the game | `InboundReactions` | on | 👁️ / ✅ / ⏸️ / ❌ reactions on your Discord messages. |
| Allow sending as my character | `AllowSendAsCharacter` | **off** | The only switch for send-as-me: when ticked, your own Discord messages are really sent as your character in every channel with inbound relay. Not controllable from Discord. Outside Dalamud's rules. |
| Tag messages sent by the plugin with [S] on my screen | `MarkSentInGame` | on | Re-shows plugin-sent messages on your screen with `SentTag`. Others see the normal message. |
| *Developer builds:* Run self-tests after each load | `DevRunSelfTestsOnLoad` | off | Run the in-game self-tests whenever the plugin (re)loads, once logged in and connected. |
| *Developer builds:* Self-tests may send a tell to myself | `DevSelfTestTellSelf` | off | Enables `send.tell-self` (one real tell to your own character). |

## Per Discord channel (set with Discord commands)

Stored under `ChannelConfigs` → channel ID.

| Setting | Command | Default | What it does |
|---|---|---|---|
| `ChatTypes` | `setchannel` / `unsetchannel` | none | Chat kinds relayed to this channel. |
| `IsContentFinder` | `toggledf` | off | Duty Finder pops posted here. |
| `InboundChatType` | `setinbound` | off | Default in-game kind for messages posted here (null = Discord → game off). |
| `InboundPermission` | `setinboundperm` | `OwnerOnly` | `OwnerOnly`, `Role`, or `Everyone`. |
| `InboundRoleId` | `setinboundperm role @role` | none | Role allowed when `InboundPermission` is `Role`. |
| `InboundSendAsOwner` | — | — | Obsolete (sending is controlled only by `AllowSendAsCharacter`). Kept so old configs load. |
| `WebhookId` | (automatic) | — | The webhook the plugin created in this channel. |
| `IsBidirectional` | — | — | Obsolete, never used. Kept so old configs load. |

## Global, set with Discord commands

| Setting | Command | Default |
|---|---|---|
| `PrefixConfigs` | `setprefix` / `unsetprefix` | none |
| `CustomSlugsConfigs` | `setchattypename` / `unsetchattypename` | none |
| `ChatTypeAvatarURL` | `setavatar <kind>` / `unsetavatar <kind>` | none |
| `DefaultAvatarURL` | `setavatar default <url>` / `unsetavatar default` | Dalamud logo |
| `CFPrefixConfig` | `setcfprefix` | empty |
| `SenderInMessage` | `togglesender` | off |
| `PostStatusMessages` | `togglestatus` | on |
| `ForceEmbedFallbackMode` | `toggleembed` | off |
| `ForceDefaultNameAvatar` | `toggledefaultnameavatar` | off |
| `DuplicateCheckMS` | `setduplicatems` | 5000 |
| `SelfTestChannelId` | `selftesthere` (dev) | none |

## Config file only

| Setting | Default | What it does |
|---|---|---|
| `DiscordBotPrefix` | `xl!` | Prefix for Discord commands. |
| `InboundSenderFormat` | `[D] {name}` | How shown messages are labelled; `{name}` is your character name (your messages) or the Discord name (others). |
| `SentTag` | `[S]` | Tag for plugin-sent messages on your screen. |
| `SendIntervalMs` | 2000 | Minimum time between send-as-me messages (at least 500). |
| `SendQueueLimit` | 5 | How many send-as-me messages may wait their turn; more are rejected (❌). |
| `Version` | 0 | Config format version (internal). |
