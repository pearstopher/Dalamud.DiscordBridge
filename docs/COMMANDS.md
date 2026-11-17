# Command reference

Everything you can type, in Discord and in game. Discord commands use the bot prefix (default `xl!`; configurable, see [SETTINGS.md](SETTINGS.md)). **Owner** means the command only works for the Discord account set as owner in `/dcp`. Everyone else gets "You are not allowed…".

Every `<kinds>` argument is a comma-separated list, case-insensitive. `tell` means both directions, `p` means party + cross-party, `any` = all regular chat, `all` = every kind. A typo is rejected with the unknown names listed; nothing is changed.

Kinds are chat-type names such as `say`, `fc`, `ls1`, `cwls1`, `p`, `tell`, `e` (Echo), `customemote`. In game, `/dcpprintlist` prints the full list.

## Discord: one-command setup

| Command | Who | What it does |
|---|---|---|
| `xl!setup` | Owner | **Start here.** Sane settings for this channel in one go. **Game → Discord:** all player chat (say, shout, yell, tells, party, alliance, PvP team, FC, LS1–8, CWLS1–8, Novice Network, emotes; GM messages in those come along), plus 🟢/⚫ **FC members logging in/out** (`fclogin`). This *replaces* the list, so retainer sales, system/loot/battle/NPC messages, Echo relays and Duty Finder pops are **off**. **Discord → game:** default Echo, owner only (replies and `!` prefixes reach every player channel). **Display:** shown in game, reactions and `[S]` tags on; Echo mode, sender-in-message, embed fallback, bot-name override off; duplicate window 5000 ms. **Never touched:** token, owner, prefix, your per-kind prefixes/names/avatars, other channels, and send-as-me (only the in-game checkbox controls it). Safe to run again. |

## Discord: game → Discord (what gets relayed to this channel)

**FC members logging in/out** (`fclogin`): posted as "🟢 Bob Smith has logged in." / "⚫ … has logged out." under the member's name and portrait. Included in `xl!setup`; on its own: `xl!setchannel fclogin` (off: `xl!unsetchannel fclogin`).

Mentions in relayed game text (`<@123>`, `<@&456>`, `@everyone`) show as text and **never ping** anyone (#41). Mentions you put in your own prefix (`setprefix`, `setcfprefix`, e.g. `@here` on duty pops or `<@yourId>` on tells) still ping, and only those.

| Command | Who | What it does | Example |
|---|---|---|---|
| `xl!setchannel <kinds>` | Owner | Relay these chat kinds to this channel. Comma-separated. `any` = all regular chat (incl. both tell directions), `all` = **every** kind (system, loot, retainer sales…), `tell` = incoming and outgoing tells, `p` = party and cross-party. **GM messages** come along with the matching player channel (a GM tell when tells are on, GM FC when FC is on, GM LS1 with LS1…), labelled `[gmtell]`, `[gmfc]`… (replies to them are blocked). | `xl!setchannel fc,ls1,tell` · `xl!setchannel all` |
| `xl!unsetchannel <kinds>` | Owner | Stop relaying these kinds here (`any`/`all` work too). | `xl!unsetchannel all` |
| `xl!listchannel` | Owner | Show what this channel receives, plus its inbound (Discord → game) setting. | |
| `xl!toggledf` | Owner | Post Duty Finder pops in this channel (on/off). | |
| `xl!ping` (or `xl!test`) | Anyone | **Connection test.** The bot reacts ✅ (plugin running, character logged in), ⏸️ (running, not logged in) or 💤 (running but paused). Nothing is shown or sent in game. **No reaction = the plugin (and so the game) isn't running.** If the bot can't add reactions, it replies `Pong: …` instead. | `xl!ping` |
| `xl!pause` / `xl!resume` | Owner | **Global.** Stop all relaying, both ways (chat, FC logins, retainer sales, duty pops, IPC; Discord messages get 💤 and go nowhere), keeping every setting. Lasts across game restarts until `xl!resume`. Same as clicking the plugin's icon in the game's **server info bar** (`↑↓` = running, `\| \|` = paused, `X` = not connected: click it to open `/dcp`) or the "Pause relaying" checkbox in `/dcp`. Pausing and resuming are announced in the bridged channels (unless `togglestatus` is off). Commands keep working while paused. | `xl!pause` |
| `xl!togglestatus` | Owner | **Global.** Post 🟢 *Online* when the plugin starts and 🔴 *Offline* when it stops (game closed normally, plugin disabled or reloaded) in every bridged channel (any channel with chat kinds, duty pops or `setinbound`). On by default. Posted once per plugin load: reconnects and `/dcp` Save don't repost. A crash posts nothing. When the plugin isn't running, nothing reads Discord, so your messages get **no reaction at all**. | |
| `xl!setprefix <kinds> <prefix>` | Owner | Text/emoji put in front of relayed lines of these kinds. | `xl!setprefix fc 🏠` |
| `xl!unsetprefix <kinds>` | Owner | Remove those prefixes. | `xl!unsetprefix fc,tell` |
| `xl!setcfprefix <prefix\|none>` | Owner | Prefix for Duty Finder posts (e.g. a role ping). | `xl!setcfprefix @here` |
| `xl!setchattypename <kinds> <text\|none>` | Owner | Replace the `[slug]` label shown for these kinds (`none` = back to default). | `xl!setchattypename fc Home` |
| `xl!unsetchattypename <kinds>` | Owner | Back to the default label. | |
| `xl!setavatar <kinds\|default> <url\|none>` | Owner | Fallback avatar for these kinds (when no Lodestone portrait), or the global default. Must be a full `https://` link; `none` removes it. | `xl!setavatar default https://…/pear.png` |
| `xl!unsetavatar <kinds\|default>` | Owner | Remove those avatar overrides. | |
| `xl!togglesender` | Owner | Global: also write `Name＠World:` inside each relayed message. | |
| `xl!toggleembed` | Owner | Global: post as embeds instead of webhooks (fallback mode). | |
| `xl!toggledefaultnameavatar` | Owner | Global: webhook posts use the bot's name and default avatar instead of the player's (combine with `togglesender`). | |
| `xl!setduplicatems <ms\|none>` | Owner | Duplicate-filter window when several players relay the same chat (default 5000; `0` or `none` = off). | `xl!setduplicatems 3000` |
| `xl!help` | Anyone | List the commands. | |

## Discord: Discord → game (messages you post)

| Command | Who | What it does | Example |
|---|---|---|---|
| `xl!setinbound <kind\|off>` | Owner | Messages posted in this channel appear in game as this kind. Kinds: `say, shout, yell, p, alliance, fc, ls1–ls8, cwls1–cwls8, nn, e, customemote`, or `off`. | `xl!setinbound e` |
| `xl!setinboundperm <owner\|everyone\|role @role>` | Owner | Who may relay from this channel (default owner only; the owner is always allowed). | `xl!setinboundperm role @FC` |
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

**Shown vs sent:** controlled **only in game**, by "Allow sending as my character" in `/dcp`. Unticked: messages are only *shown* on your screen (`[D]`). Ticked: the **owner's** messages are *sent* through the game (`[S]` on your screen), in every channel with `setinbound`. Everyone else's are always only shown. Sending refuses text starting with `/`, over 500 bytes, or faster than one per 2 s (with a ❌ and a reply saying why).

**Reactions on your Discord message:** 👁️ shown on your screen · ✅ sent through the game · ⏸️ not logged in · 💤 paused (`xl!resume`) · ❌ rejected (with a reply) · ⚠️ error. Turn off with "React on Discord…" in `/dcp`.

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
