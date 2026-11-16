using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Dalamud.DiscordBridge
{
    /// <summary>
    /// One-time import of settings from the official Discord Chat Bridge plugin (InternalName <c>Dalamud.DiscordBridge</c>),
    /// so switching to Discord Chat Pears keeps the bot token, channels, prefixes and avatars.
    /// </summary>
    internal static class ConfigImport
    {
        /// <summary>
        /// Path of the legacy config file next to ours in Dalamud's pluginConfigs folder.
        /// </summary>
        internal static string LegacyConfigPath(string ourConfigPath)
            => Path.Combine(Path.GetDirectoryName(ourConfigPath) ?? ".", Constant.LegacyInternalName + ".json");

        /// <summary>
        /// Read the legacy config file (read-only) if it exists. Returns null if it doesn't exist or can't be parsed.
        /// </summary>
        internal static Configuration? TryReadLegacy(string legacyPath, Action<Exception>? onError = null)
        {
            try
            {
                return File.Exists(legacyPath) ? FromLegacyJson(File.ReadAllText(legacyPath)) : null;
            }
            catch (Exception ex)
            {
                onError?.Invoke(ex);
                return null;
            }
        }

        /// <summary>
        /// Parse a legacy config. Dalamud writes <c>"$type"</c> metadata naming the old assembly
        /// (<c>Dalamud.DiscordBridge.Configuration, Dalamud.DiscordBridge</c>), which no longer exists under our
        /// assembly name, so it's stripped before deserializing.
        /// </summary>
        internal static Configuration FromLegacyJson(string json)
        {
            var token = JToken.Parse(json);
            StripTypeMetadata(token);

            var serializer = JsonSerializer.Create(new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.None,
                MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
            });

            return token.ToObject<Configuration>(serializer) ?? new Configuration();
        }

        private static void StripTypeMetadata(JToken token)
        {
            if (token is JObject obj)
            {
                obj.Remove("$type");

                // A serialized collection may be wrapped as { "$type": ..., "$values": [...] }.
                foreach (var property in obj.Properties().ToList())
                {
                    if (property.Value is JObject inner && inner["$values"] is JArray values && inner.Count <= 2)
                        property.Value = values;
                }
            }

            foreach (var child in token.Children())
                StripTypeMetadata(child);
        }
    }
}
