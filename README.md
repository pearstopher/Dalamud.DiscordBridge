# Discord Chat Pears

A Dalamud plugin for FFXIV that relays your in-game chat to one or more Discord channels of your choosing. You can send the same chat messages to several channels.

**This is a fork** of [Discord Chat Bridge](https://github.com/reiichi001/Dalamud.DiscordBridge) by goat and Franz, maintained by pearstopher. It installs as a separate plugin (`DiscordChatPears`) and imports your Discord Chat Bridge settings the first time it loads. Disable Discord Chat Bridge afterwards so messages aren't relayed twice.

Two-way chat (showing Discord messages in game) is in development. See [docs/design/discord-to-game.md](docs/design/discord-to-game.md).

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
