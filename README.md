# Discord Chat Pears

A Dalamud plugin for FFXIV that relays your in-game chat to one or more Discord channels of your choosing. You can send the same chat messages to several channels.

**This is a fork** of [Discord Chat Bridge](https://github.com/reiichi001/Dalamud.DiscordBridge) by goat and Franz, maintained by pearstopher. It installs as a separate plugin (`DiscordChatPears`) and imports your Discord Chat Bridge settings the first time it loads. Disable Discord Chat Bridge afterwards so messages aren't relayed twice.

**Discord → game:** run `xl!setinbound fc` (or `ls1`, `cwls1`, `say`, …) in a Discord channel, and messages posted there show up in **your** chat log as `[D] Name: message`, coloured like that channel. Only you see them. `xl!setinboundperm` controls who can relay (owner only by default). Toggle it in `/dcp`.

**Send as me (opt-in, off by default):** tick "Allow sending as my character" in `/dcp`, then run `xl!togglesend` in a channel. **Your own** messages there are then said in game by your character, so your FC/LS sees them. Everyone else's are still only shown to you. Messages are rate-limited and can never run commands. ⚠️ This is outside Dalamud's plugin rules (the server receives chat you didn't type in game), so use it at your own risk. See [docs/design/discord-to-game.md](docs/design/discord-to-game.md).

## Install

In game: `/xlsettings` → **Experimental** → **Custom Plugin Repositories**, add

```
https://raw.githubusercontent.com/pearstopher/ffxiv-plugins/main/pluginmaster.json
```

then install **Discord Chat Pears** from `/xlplugins` (available after the first release).

## Use

- In game: `/dcp` opens the setup window.
- In Discord: `xl!help` lists the bot commands.
- Setup guide (upstream, still accurate for the outbound relay): https://github.com/reiichi001/Dalamud.DiscordBridge/wiki/Setup-Guide
- Which features follow Dalamud's plugin rules: [docs/DALAMUD_RULES.md](docs/DALAMUD_RULES.md)
- Developing and testing: [DEVELOPMENT.md](DEVELOPMENT.md)

License: AGPL-3.0 (inherited from the original project).
