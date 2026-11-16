# In-game test plan

One session that checks everything Claude couldn't test without the game: the outbound relay after the bug fixes (#3–#8), the rename (#11), and the two-way feature (#13–#16). Tick each box. When something fails, note the step number and attach the matching part of `%APPDATA%\XIVLauncher\dalamud.log` to the related issue (`gh issue list --label needs-in-game-test --state all`).

**You'll need:** the dev build loaded ([DEVELOPMENT.md](../DEVELOPMENT.md) §1–4), a private test Discord server with the bot, two text channels `#out` and `#two-way`, a second Discord account (for non-owner checks), and ideally an FC or linkshell where you can say test lines. `/xllog` open, log level **Debug**, filtered on `DiscordChatPears`.

## 0. Automated self-tests (do these first)

Dev builds have a **Run self-tests** button in `/dcp` (and `/dcptest`). It runs the checks below in about 30 seconds and writes `%APPDATA%\XIVLauncher\pluginConfigs\DiscordChatPears\selftest-results.json`, which Claude can read directly. Tick **Run self-tests after each load** and turn on Dalamud's automatic reloading, and every `dotnet build` re-runs them by itself once you're logged in.

For the Discord round trip, run `xl!selftesthere` once in a **private** channel. The test posts a tagged message there and deletes it again.

- [ ] 0.1 All self-tests **PASS** (a WARN is fine if its message explains it). If any **FAIL**, stop and report the results file.

| Self-test | Checks | Replaces plan steps |
|---|---|---|
| `env.build` | Which DLL/build is loaded | A1 |
| `env.logged-in` | You're logged in (game tests need it) | — |
| `env.discord-ready` | Bot connected | A3 (status) |
| `env.owner` | Owner configured | — |
| `data.item-sheet` | Item sheet and icon URL for retainer sales | B3 (data part) |
| `outbound.tell-recipient` | Tell recipient read as Name＠World | B10 (extraction part) |
| `ipc.api-version` | IPC answers, and who owns it | A4 |
| `lodestone.portrait` | NetStone still finds your portrait | B1 (portrait part) |
| `inbound.display.loop-guard` | A shown line appears and is **not** relayed back | C1, C10 |
| `inbound.display.echo-mode` | Same, in Echo mode | C8 (echo part) |
| `send.sanitizer` | Client sanitizer keeps plain text | — |
| `send.echo-roundtrip` | Send path via `/e`: typed, echoed, swallowed | D2, D3 (loop part) |
| `send.tell-self` | *(opt-in)* Real tell to yourself via the send path; both copies swallowed | D3 |
| `send.slash-rejected` | `/logout` is rejected and you stay logged in | D5 |
| `discord.channels` | Every configured channel reachable, webhook permission | B4 (permission part) |
| `discord.round-trip` | Post → gateway → back, Message Content intent working | — |

The steps below are still manual: things that need a second Discord account, other players, logging out, reloading, or judging how something looks. Steps fully covered above are marked *(automated)*; skip them if section 0 passed.

## A. Load and identity (#10, #11)

- [ ] A1. *(automated: `env.build`)* `/xllog` shows `Discord Chat Pears loaded (Debug build, built <just now>, from …\DiscordChatPears.dll)`.
- [ ] A2. First load with an old Discord Chat Bridge config present: chat says "imported your settings", and `/dcp` shows your token and owner. Delete `pluginConfigs\DiscordChatPears.json` to repeat.
- [ ] A3. `/dcp` opens "Discord Chat Pears Setup". `/discordpears` also opens it. Status reaches **Connected!**.
- [ ] A4. With the official Discord Chat Bridge also enabled: our plugin loads, and `/xllog` shows the IPC **warning** (not an error). Disable the official plugin for the rest of the session.
- [ ] A5. Disable and re-enable Discord Chat Pears in `/xlplugins`: `… unloaded`, then `… loaded`, with no errors (#6).

## B. Game → Discord regression (#3–#8)

- [ ] B1. `xl!setchannel say,fc` in `#out`. `/say hello` appears once in `#out`, with your name＠world and Lodestone portrait.
- [ ] B2. After the re-enable in A5, `/say once` still appears **exactly once** (#6).
- [ ] B3. `/dcpdebug say` posts "Test Message". `/dcpsaledebug` posts a retainer sale with the Titanium Ore icon (#4).
- [ ] B4. `xl!toggleembed` → posts become embeds. Toggle back. Remove the bot's Manage Webhooks permission → red FALLBACKMODE embed. Restore it.
- [ ] B5. From the second account: `xl!setavatar say https://example.com/a.png` → "You are not allowed" (#3). Same for `xl!unsetavatar say`.
- [ ] B6. DM the bot `xl!setchannel say`, then `/say dm test` → an embed arrives in the DM (#5). Reload the plugin and repeat: it still works, and the DM config wasn't deleted.
- [ ] B7. (If you have custom emotes `xivAtl`, `xivAtr`, `xivHq` in the server) An auto-translate phrase and an HQ item link show the custom emotes (#8).
- [ ] B8. (Two players running the plugin into one channel) A shared FC line appears once, and the **older** copy is the one kept (#7).
- [ ] B10. Send someone a tell → Discord shows `**[tell]** → Them＠World: text` from you. A tell you receive looks as before (#19).
- [ ] B9. Idle for a minute with the plugin loaded: no unusual CPU use from the game (#4).

## C. Discord → game: display (#13–#15)

- [ ] C1. *(automated: `inbound.display.loop-guard`)* `/dcpin fc Alice hello there` → an FC-coloured `[D] Alice: hello there` in your chat log, and **nothing** appears in `#out` (loop guard).
- [ ] C2. `xl!setinbound fc` in `#two-way` → reply "Inbound (Discord → game): Free Company (owner only)". `xl!listchannel` shows the same line.
- [ ] C3. As the owner, post `test 123` in `#two-way` → `[D] <you>: test 123` in game, ✅ on the Discord message, and nothing relayed to `#out`.
- [ ] C4. Post `**hi** <:someEmote:1> 👍 ||secret||` with an image attached → game shows `hi :someEmote: (+1) [spoiler] [image]`.
- [ ] C5. Reply to someone's message → `re @Them: …`.
- [ ] C6. From the second account → nothing (Debug: "isn't allowed"). `xl!setinboundperm everyone` → it shows. Set it back to `owner`.
- [ ] C7. At the title screen (logged out), post → ⏸️, and nothing printed after you log in.
- [ ] C8. `/dcp`: untick "Show Discord messages in game chat" → nothing shown, no reaction. Tick it again. Tick "Show as Echo" → printed as Echo `[FC] [D] you: …`. Untick it.
- [ ] C9. `xl!setinbound off` → nothing shown. `xl!setinbound tell` → error listing the allowed kinds.
- [ ] C10. *(automated: `inbound.display.loop-guard`)* **Loop check:** `xl!setchannel fc` **and** `xl!setinbound fc` on the same channel. A post there shows in game and is **not** reposted to that channel by the webhook.

## D. Discord → game: send as me and replies (#16, #21, #22)

Test with **tells to yourself**, not FC, so nobody else sees test lines. A tell to yourself shows twice in game (outgoing `>>` and incoming). Neither copy should be relayed back to Discord. Have `xl!setchannel tell` on your outbound channel so your tells are relayed there to reply to.

- [ ] D1. With "Allow sending as my character" **off**: `/dcpsend e hi` → error "turned off in /dcp".
- [ ] D2. Tick it (a red warning appears). `/dcpsend e hello from dev` → Echo line from you, and nothing in Discord.
- [ ] D3. *(automated, opt-in: `send.tell-self`, tick "Self-tests may send a tell to myself")* In game, `/tell <your name>@<world> ping` yourself. Discord shows the relayed tell (`→ You＠World: ping`).
- [ ] D4. In the channel with `setinbound` (any default kind) run `xl!togglesend`. In Discord, **reply** to the relayed `[tell] → You＠World: ping` with `pong` → your character sends `/tell You@World pong`. In game you see it twice (outgoing and incoming), with ✅ on Discord, and **neither copy is relayed back** to Discord.
- [ ] D5. *(automated: `send.slash-rejected`)* **Command safety:** Discord's client turns `/logout` into its own slash-command menu, so post ` /logout` (leading space) or `` `/logout` ``. Expect ❌ with the reply "Not sent to game: messages can't start with /". You are **not** logged out, and nothing is said in game. In display-only channels the text is shown literally as `/logout` (printing never runs commands).
- [ ] D6. Reply to the tell with 600 characters → ❌ "too long for game chat".
- [ ] D7. Reply 10 times quickly → about one every 2 s, and the extras ❌ "too many at once".
- [ ] D8. **Replies go to the right place** (display mode is fine here, no sending needed):
  - Reply to a relayed **incoming** tell from someone → shown as a tell-coloured `[D] you: → Them＠World: …` (or sent as a tell to them, in send mode).
  - Reply to a relayed `[say]` (or `[ls1]`) line → shown/sent in Say (LS1), not the channel's default.
  - Reply to a relayed emote or system message, or a normal non-reply message → uses the channel's default (`setinbound`).
  - Reload the plugin, then reply to an *older* relayed tell → still goes to that person (parsed from the message).
- [ ] D9. From the second account (with `setinboundperm everyone`) → shown locally only, never sent.
- [ ] D10. `xl!togglesend` again → back to display-only. Untick "Allow sending as my character".

## E. Wrap-up

- [ ] E1. `/xllog` has no unexpected `ERR` lines from DiscordChatPears.
- [ ] E2. Comment the results on each `needs-in-game-test` issue and remove the label from the ones that passed.
