# Dalamud rules compliance

This plugin is distributed from the owner's **custom repository** (`pearstopher/ffxiv-plugins`), not the official Dalamud repo. The owner decided in #9 that it is **not bound** by Dalamud's plugin rules. We still record, for every feature, whether it follows them, so the risk is visible and nothing that breaks them is ever submitted to the official repo by accident.

Source: [Dalamud plugin restrictions](https://dalamud.dev/plugin-publishing/restrictions). The rules that matter for this plugin:

- **R1:** A plugin must not do anything a human player couldn't do.
- **R2:** A plugin must not interact with the game servers in a way that is **automatic** ("polling data or making requests without direct interaction from the user") or **outside of specification** ("allowing the player to do or submit things to the server" they normally couldn't).
- **R3:** A plugin must not collect account IDs of other players' characters.
- **R4:** A plugin must not be useful *only* in out-of-spec scenarios.

Square Enix's terms prohibit third-party tools in general. These rules are the Dalamud community's line for what's tolerated.

## Features

| Feature | Follows the rules? | Notes |
|---|---|---|
| Relay in-game chat to Discord (outbound) | ✅ Yes | Reads chat the client already received. No server interaction. |
| Duty-pop and retainer-sale notifications | ✅ Yes | Reads local events. |
| Lodestone portraits (NetStone) | ✅ Yes | Public Lodestone web pages, not game servers. Only names/worlds already visible in chat (R3 is about account IDs). |
| XIVAPI icon URLs | ✅ Yes | Public web assets. |
| IPC `DiscordBridge.SendMessage` | ✅ Yes | Other plugins post to Discord. No game interaction. |
| Discord configuration commands | ✅ Yes | Discord-only. |
| Debug commands (`/dcpdebug`, `/dcpsaledebug`, `/dcpprintlist`, `/dcpin`) | ✅ Yes | Local only. |
| `/dcpsend` debug command | ✅ Yes | You type it in game yourself (direct input), like typing the chat line. |
| Settings import from Discord Chat Bridge (#11) | ✅ Yes | Reads a local config file once. |
| Discord reactions and replies about inbound messages | ✅ Yes | Discord-only. |
| Discord → game: inbound config and routing (`setinbound`, `setinboundperm`, #13) | ✅ Yes | Discord-side only. Messages are queued; nothing reaches the game yet. |
| Discord → game text conversion (#14) | ✅ Yes | Pure text processing. |
| **Discord → game: local display** (#15) | ✅ Yes | Printed into your own chat log with `IChatGui.Print`, client-side only. Nobody else sees it, and nothing is sent to the server. |
| **Discord → game: owner "send as me"** (#16, `togglesend`) | ⚠️ **No (R2)** | Your character sends chat to the server because of a Discord message, without in-game input. It's mitigated: owner-only, opt-in (off by default), you typed the text yourself, rate-limited, and it can't run commands. Similar plugins exist (ChatTwo, ChatAnywhere) but take input inside the client. Never submit this feature to the official repo. |

## Adding a feature

Every new feature adds a row here (see [ADDING_FEATURES.md](ADDING_FEATURES.md)). Every issue states its compliance under a "Rules compliance" heading. Rule-breaking features must be **opt-in and off by default**, and their in-game UI or Discord reply must say so.
