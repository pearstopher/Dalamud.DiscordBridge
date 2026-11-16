# Discord Chat Pears

Two-way chat between Final Fantasy XIV and Discord, as a Dalamud plugin.

- **Game → Discord:** relay your in-game chat (FC, linkshells, party, tells, say…) to the Discord channels you choose, with each speaker's name and Lodestone portrait.
- **Discord → game:** messages posted in a Discord channel show up in **your** chat log, coloured like the in-game channel they're linked to.
- **Send as me** (opt-in): your **own** Discord messages are said in game by your character, so your Free Company or linkshell sees them.

**This is a fork** of [Discord Chat Bridge](https://github.com/reiichi001/Dalamud.DiscordBridge) by goat and Franz, maintained by pearstopher. It installs as a separate plugin (`DiscordChatPears`), and the first time it loads it imports your Discord Chat Bridge settings. **Disable Discord Chat Bridge afterwards**, or every message is relayed twice.

## Install

In game, `/xlsettings` → **Experimental** → **Custom Plugin Repositories**, add:

```
https://raw.githubusercontent.com/pearstopher/ffxiv-plugins/main/pluginmaster.json
```

Save, then install **Discord Chat Pears** from `/xlplugins`.

## Set up

1. **Create a bot.** At https://discord.com/developers/applications → **New Application** → **Bot**, reset and copy the **token**. Under **Privileged Gateway Intents**, turn on **Message Content Intent**.
2. **Connect it.** In game, type `/dcp`, paste the token and your Discord username (or user ID), and click **Save**. When the status says **Connected!**, click **Join my server** and add the bot to your server.
3. **Quick start: `xl!setup`** in your channel sets everything sanely in one go: all player chat both ways, notifications (retainers, system, loot, duty pops) off, Discord → game defaulting to silent Echo. Send-as-me stays off until you opt in. Adjust the details with the commands below if you like.
4. **Game → Discord (details).** In a Discord channel: `xl!setchannel fc,ls1` (any chat kinds), `xl!setchannel any` (all regular chat), or `xl!setchannel all` (**every** kind, including system messages). Your FC and LS1 chat now appears there. `xl!listchannel` shows what a channel receives.
5. **Discord → game.** In a Discord channel: `xl!setinbound fc`. Messages posted there appear in your chat log exactly like game chat, with a `[D]` tag (e.g. `[FC]<[D] Ane Mora> hi`, `[D] Ane Mora purrs.`, `>> [D] Bob Smith@World: hi`). Your own messages use your character name. **Only you see them.**
   - Kinds: `say, shout, yell, p, alliance, fc, ls1`–`ls8`, `cwls1`–`cwls8`, `nn`, `e` (Echo: only you see it), `customemote`, or `off`.
   - Who can relay: `xl!setinboundperm owner` (default), `everyone`, or `role @SomeRole`.
   - Discord reacts 👁️ when a message was shown to you, ✅ when it was really sent, and ⏸️ if you weren't logged in.
   - A channel can be both: `setchannel fc` + `setinbound fc` gives you a two-way FC channel.
6. **Replies.** Reply (Discord's reply button) to a relayed message and your reply goes to **where that message came from**: the same channel, or for a tell, back to that person. Non-replies use the channel's `setinbound` kind. Replies to things you can't talk in (**GM messages**, retainer sales, system messages, standard emotes…) are blocked with ❌ and never go anywhere.
7. **One-off channel override.** Start a message with `!e`, `!s`, `!fc`, `!l1`, `!cwl1`, `!p`, `!em`… to send just that message there, ignoring replies and the default. For a tell: `!t First Last@World message`. Unknown channels (typos) are rejected with ❌ instead of going anywhere.
8. **Send as me (optional).** In `/dcp`, tick **Allow sending as my character**. That's the only switch (Discord can't change it). Now **your own** messages there are said in game by your character (`/fc …`, or `/tell Name@World …` when replying to a tell) in every channel with inbound relay, so the other players see them. Other people's messages are still only shown to you.

**Telling them apart in game:** `[D]` = shown on your screen only (nobody else sees it). `[S]` = really sent by the plugin as your character. Only *your* screen shows the tag; others see your normal message. No tag = normal game chat you typed yourself.

**Is it running?** When the plugin starts, the bot posts 🟢 *Online* in your bridged channels, and 🔴 *Offline* when it stops (game closed, plugin disabled). While it's off, nothing reads Discord, so messages get no reaction at all. `xl!togglestatus` turns these posts off. A crash posts nothing.

`xl!help` lists the Discord commands. Full references: **[docs/COMMANDS.md](docs/COMMANDS.md)** (every command, `!` override, reaction and tag) and **[docs/SETTINGS.md](docs/SETTINGS.md)** (every setting and its default). The `/dcp` window has the on/off switches.

## Safety and Dalamud's rules

- Showing Discord messages in your chat log is client-side only. Nothing is sent to the game server.
- **Send as me** *does* send chat to the game server without you typing in game. That's **outside Dalamud's plugin rules**, which is why this plugin is distributed from a custom repository and the feature is off by default. Use it at your own risk. Safeguards:
  - only the configured owner's messages are sent
  - two switches must both be on
  - messages can never start a command (anything starting with `/` is rejected, with a reply saying why)
  - at most one message every 2 seconds, and a capped backlog
  - over-long messages are rejected rather than cut off
- The full per-feature list is in [docs/DALAMUD_RULES.md](docs/DALAMUD_RULES.md).

## More

- Original setup guide (still accurate for the game → Discord part): https://github.com/reiichi001/Dalamud.DiscordBridge/wiki/Setup-Guide
- How the two-way feature was designed: [docs/design/discord-to-game.md](docs/design/discord-to-game.md)
- Building, testing in game, releasing: [DEVELOPMENT.md](DEVELOPMENT.md) · Developer docs: [docs/](docs/README.md) · In-game test plan: [docs/IN_GAME_TEST_PLAN.md](docs/IN_GAME_TEST_PLAN.md)

License: AGPL-3.0 (inherited from the original project).
