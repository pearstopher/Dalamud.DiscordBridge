# Command reference

Everything you can type, in Discord and in game. Discord commands use the bot prefix (default `xl!`; configurable, see [SETTINGS.md](SETTINGS.md)). **Owner** means the command only works for the Discord account set as owner in `/dcp`. Everyone else gets "You are not allowed…".

Kinds are chat-type names such as `say`, `fc`, `ls1`, `cwls1`, `p`, `tell`, `e` (Echo), `customemote`. In game, `/dcpprintlist` prints the full list.

## Discord: game → Discord (what gets relayed to this channel)

| Command | Who | What it does | Example |
|---|---|---|---|
| `xl!setchannel <kinds>` | Owner | Relay these chat kinds to this channel. Comma-separated. `any` = all regular chat (incl. both tell directions), `all` = **every** kind (system, loot, retainer sales…), `tell` = incoming and outgoing tells, `p` = party and cross-party. | `xl!setchannel fc,ls1,tell` · `xl!setchannel all` |
| `xl!unsetchannel <kinds>` | Owner | Stop relaying these kinds here (`any`/`all` work too). | `xl!unsetchannel all` |
| `xl!listchannel` | Owner | Show what this channel receives, plus its inbound (Discord → game) setting. | |
| `xl!toggledf` | Owner | Post Duty Finder pops in this channel (on/off). | |
| `xl!setprefix <kinds> <prefix>` | Owner | Text/emoji put in front of relayed lines of these kinds. | `xl!setprefix fc 🏠` |
| `xl!unsetprefix <kind>` | Owner | Remove that prefix. | |
| `xl!setcfprefix <prefix\|none>` | Owner | Prefix for Duty Finder posts (e.g. a role ping). | `xl!setcfprefix @here` |
| `xl!setchattypename <kinds> <text>` | Owner | Replace the `[slug]` label shown for these kinds. | `xl!setchattypename fc Home` |
| `xl!unsetchattypename <kind>` | Owner | Back to the default label. | |
| `xl!setavatar <kinds\|default> <url>` | Owner | Fallback avatar for these kinds (when no Lodestone portrait), or the global default. | `xl!setavatar default https://…/pear.png` |
| `xl!unsetavatar <kind\|default>` | Owner | Remove that avatar override. | |
| `xl!togglesender` | Owner | Global: also write `Name＠World:` inside each relayed message. | |
| `xl!toggleembed` | Owner | Global: post as embeds instead of webhooks (fallback mode). | |
| `xl!toggledefaultnameavatar` | Owner | Global: webhook posts use the bot's name and default avatar instead of the player's (combine with `togglesender`). | |
| `xl!setduplicatems <ms>` | Owner | Duplicate-filter window when several players relay the same chat (default 5000). | `xl!setduplicatems 3000` |
| `xl!help` | Anyone | List the commands. | |

## Discord: Discord → game (messages you post)

| Command | Who | What it does | Example |
|---|---|---|---|
| `xl!setinbound <kind\|off>` | Owner | Messages posted in this channel appear in game as this kind. Kinds: `say, shout, yell, p, alliance, fc, ls1–ls8, cwls1–cwls8, nn, e, customemote`, or `off`. | `xl!setinbound e` |
| `xl!setinboundperm <owner\|everyone\|role @role>` | Owner | Who may relay from this channel (default owner only; the owner is always allowed). | `xl!setinboundperm role @FC` |
| `xl!togglesend` | Owner | Opt-in **send as me**: your own messages here are said in game by your character. Also needs "Allow sending as my character" in `/dcp`. Outside Dalamud's rules. | |
| `xl!selftesthere [off]` | Owner | Developer builds: use this (private) channel for the Discord round-trip and reply-routing self-tests. Not listed in `xl!help`. | |

**Where a message goes** (first match wins):
1. **`!channel` override** at the start of the message, for that one message only:

   | Prefix | Goes to |
   |---|---|
   | `!e` / `!echo` | Echo (only you) |
   | `!s` / `!say`, `!sh` / `!shout`, `!y` / `!yell` | Say, Shout, Yell |
   | `!p` / `!party`, `!a` / `!alliance`, `!fc` | Party, Alliance, Free Company |
   | `!l1`–`!l8` (or `!ls1`…), `!cwl1`–`!cwl8` (or `!cwls1`…) | Linkshells, cross-world linkshells |
   | `!n` / `!nn` | Novice Network (shown only; can't be sent) |
   | `!em` / `!customemote` | Custom emote |
   | `!t First Last@World text` / `!tell …` | A tell to that person (`@World` optional) |

   An unknown `!word` (typo) gets ❌ plus a reply, and goes nowhere. Text like `!!!` or `! hi` is a normal message.
2. **Reply** (Discord's reply button) to a relayed message → back where it came from: the same channel, or for a tell, that person. A custom emote → custom emote.
   **Replies only work for channels you can talk in.** A reply to a relay from a **GM channel** (GM tell/say/shout/yell/party/FC/linkshell/novice network), a **retainer sale**, a system/loot/battle/NPC message, a standard emote, or anything whose channel can't be determined is **blocked**: ❌ plus a reply saying why. Nothing is shown or sent, and it never falls back to the default. To answer anyway, start the message with a `!channel` prefix. There is no `!` prefix for GM channels, and the plugin never sends to them.
3. **Default:** the channel's `setinbound` kind (for messages that aren't replies, or replies to someone's Discord message rather than a relay).

**Shown vs sent:** without send-as-me, messages are only *shown* on your screen (`[D]`). With send-as-me on, the **owner's** messages are *sent* through the game (`[S]` on your screen). Everyone else's are always only shown. Sending refuses text starting with `/`, over 500 bytes, or faster than one per 2 s (with a ❌ and a reply saying why).

**Reactions on your Discord message:** 👁️ shown on your screen · ✅ sent through the game · ⏸️ not logged in · ❌ rejected (with a reply) · ⚠️ error. Turn off with "React on Discord…" in `/dcp`.

**In game:** `[D]` = shown only to you · `[S]` = really sent by the plugin (only your screen shows the tag) · no tag = normal chat.

## In game

| Command | What it does |
|---|---|
| `/dcp` (alias `/discordpears`) | Open the settings window (token, owner, switches; self-tests in dev builds). |
| `/dcpin <kind> <name> <text>` | Developer: pretend a Discord message arrived (display path), e.g. `/dcpin fc Alice hi`. Hidden from `/xlhelp`. |
| `/dcpsend <kind> <text>` | Developer: send through the real send-as-me path, e.g. `/dcpsend e test`. Needs "Allow sending as my character". Hidden. |
| `/dcptest` | Developer builds: run the in-game self-tests (same as the button in `/dcp`). Hidden. |
| `/dcpdebug <kind>` | Developer: relay a fake "Test Message" of that kind to Discord. Hidden. |
| `/dcpsaledebug` | Developer: relay a fake retainer sale. Hidden. |
| `/dcpprintlist` | Print every chat kind (`id slug - name`). Hidden. |
