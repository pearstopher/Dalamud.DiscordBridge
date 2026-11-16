# Discord Chat Bridge: Developer Documentation

Developer docs for this fork of **Discord Chat Bridge** (`Dalamud.DiscordBridge`), a Dalamud plugin that relays FFXIV chat to Discord. Written for anyone (human or Claude) who's new to the codebase.

| Doc | Read it when… |
|---|---|
| [ARCHITECTURE.md](ARCHITECTURE.md) | you need to know what each class does and how a chat line gets from the game to Discord |
| [CODING_STANDARDS.md](CODING_STANDARDS.md) | you're writing or reviewing any code |
| [TESTING.md](TESTING.md) | you need to verify a change (build, unit tests, in-game checklist) |
| [ADDING_FEATURES.md](ADDING_FEATURES.md) | you're adding a chat type, Discord command, config option, IPC endpoint, or UI |

Related:
- [../CLAUDE.md](../CLAUDE.md): repository rules (no PRs, never contact upstream) and the autonomous issue workflow
- [../README.md](../README.md): user-facing plugin description
- Upstream user guide (read-only reference): https://github.com/reiichi001/Dalamud.DiscordBridge/wiki/Setup-Guide

## At a glance

| | |
|---|---|
| Language / TFM | C# 14, `net10.0-windows`, nullable enabled |
| Project SDK | `Dalamud.NET.Sdk/15.0.0` (Dalamud API level 15) |
| Key packages | `Discord.Net` 3.20.1 (socket, REST, webhook), `NetStone` 1.4.1 (Lodestone scraping) |
| Entry point | `Dalamud.DiscordBridge/DiscordBridgePlugin.cs` |
| Build | `dotnet build Dalamud.DiscordBridge.sln -c Release` |
| Output | `Dalamud.DiscordBridge/bin/x64/Release/` (DLL), plus `…/Release/Dalamud.DiscordBridge/latest.zip` |
| License | AGPL-3.0 (inherited; derivative work must stay AGPL) |
