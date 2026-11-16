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
3. **Game → Discord.** In a Discord channel: `xl!setchannel fc,ls1` (any chat kinds, or `any`). Your FC and LS1 chat now appears there. `xl!listchannel` shows what a channel receives.
4. **Discord → game.** In a Discord channel: `xl!setinbound fc`. Messages posted there appear in your chat log as `[D] Name: message`, in FC colour. **Only you see them.**
   - Kinds: `say, shout, yell, p, alliance, fc, ls1`–`ls8`, `cwls1`–`cwls8`, `nn`, `e`, or `off`.
   - Who can relay: `xl!setinboundperm owner` (default), `everyone`, or `role @SomeRole`.
   - Discord reacts ✅ when a message reached the game and ⏸️ if you weren't logged in.
   - A channel can be both: `setchannel fc` + `setinbound fc` gives you a two-way FC channel.
5. **Send as me (optional).** In `/dcp`, tick **Allow sending as my character**, then run `xl!togglesend` in that channel. Now **your own** messages there are said in game by your character (`/fc …`), so your FC sees them. Other people's messages are still only shown to you.

`xl!help` lists every Discord command. The `/dcp` window has the on/off switches.

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
