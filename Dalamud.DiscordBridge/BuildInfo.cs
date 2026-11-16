using System;
using System.IO;

namespace Dalamud.DiscordBridge
{
    /// <summary>
    /// Describes the running build, so the load message in /xllog shows which DLL the game picked up.
    /// </summary>
    internal static class BuildInfo
    {
#if DEBUG
        internal const string Configuration = "Debug";
#else
        internal const string Configuration = "Release";
#endif

        /// <summary>
        /// E.g. "Debug build, built 2026-11-16 10:36:02, from C:\...\bin\x64\Debug\Plugin.dll".
        /// </summary>
        internal static string Describe(string assemblyPath)
        {
            var built = File.Exists(assemblyPath)
                ? File.GetLastWriteTime(assemblyPath).ToString("yyyy-MM-dd HH:mm:ss")
                : "unknown time";

            return Describe(Configuration, built, assemblyPath);
        }

        internal static string Describe(string configuration, string builtAt, string assemblyPath)
            => $"{configuration} build, built {builtAt}, from {assemblyPath}";
    }
}
