using System.Collections.Generic;
using Dalamud.DiscordBridge.Model;
using Dalamud.Game.Text;

namespace Dalamud.DiscordBridge
{
    /// <summary>
    /// The <c>setup</c> command: one sane configuration for a Discord channel (pure, unit tested). Turns on all player
    /// chat both ways, turns off notifications (retainers, system, loot, duty pops…), and resets display options to
    /// their defaults. Never touches the token, owner, prefix, your per-kind customizations, other channels, or the
    /// send-as-me opt-in.
    /// </summary>
    internal static class SanePreset
    {
        internal const XivChatType InboundDefault = XivChatType.Echo;

        /// <summary>Apply the preset to <paramref name="channelId"/> and the global display options. Returns a summary.</summary>
        internal static IReadOnlyList<string> Apply(Configuration config, ulong channelId)
        {
            if (!config.ChannelConfigs.TryGetValue(channelId, out var channel))
                channel = new DiscordChannelConfig();

            // Game → Discord: exactly player chat (replaces notifications such as retainer sales and system messages).
            channel.ChatTypes = new List<XivChatType>(ChatTypeGroups.PlayerChat);
            channel.IsContentFinder = false;

            // Discord → game: silent Echo default, owner only. Replies and !overrides reach every player channel.
            channel.InboundChatType = InboundDefault;
            channel.InboundPermission = InboundPermission.OwnerOnly;
            channel.InboundRoleId = null;

            config.SetChannel(channelId, channel);

            // Display options back to their defaults.
            config.InboundDisplayEnabled = true;
            config.InboundDisplayAsEcho = false;
            config.InboundReactions = true;
            config.MarkSentInGame = true;
            config.SenderInMessage = false;
            config.ForceEmbedFallbackMode = false;
            config.ForceDefaultNameAvatar = false;
            config.DuplicateCheckMS = 5000;

            return
            [
                $"**Game → Discord:** all player chat ({ChatTypeGroups.PlayerChat.Count} kinds: say, shout, yell, tells, party, alliance, PvP team, FC, LS1–8, CWLS1–8, Novice Network, emotes). GM messages in those channels come along.",
                "**Turned off here:** retainer sales, system, loot, battle and NPC messages, Echo relays, Duty Finder pops.",
                "**Discord → game:** default Echo (only you see it), owner only. Replies go back to the message's channel or person; start with !s, !fc, !t Name@World… to pick one.",
                "**Display:** messages shown in game, 👁️/✅ reactions, [S] tags on; Echo mode, sender-in-message, embed fallback and bot-name override off; duplicate window 5000 ms.",
                config.AllowSendAsCharacter
                    ? "**Send as me:** ON (set in game). Your messages are really sent as your character ([S])."
                    : "**Send as me:** off (set in game). To really send as your character, tick \"Allow sending as my character\" in /dcp.",
                "**Not changed:** token, owner, command prefix, your prefixes/names/avatars per chat kind, other channels.",
            ];
        }
    }
}
