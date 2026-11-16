using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace Dalamud.DiscordBridge.API
{
    /// <summary>
    /// Registers the <c>DiscordBridge.*</c> IPC endpoints. The labels are kept from the official Discord Chat Bridge so
    /// existing plugins keep working. If both plugins are loaded, whichever registers second can't own the endpoints;
    /// that's logged as a warning and this provider then leaves the other plugin's registration alone.
    /// </summary>
    public class DiscordBridgeProvider
    {
        private static IPluginLog Logger => Service.Logger;

        private const string LabelProviderApiVersion = "DiscordBridge.APIVersion";
        private const string LabelProviderSendMessage = "DiscordBridge.SendMessage";

        private readonly ICallGateProvider<int>? providerAPIVersion;
        private readonly ICallGateProvider<string, string, string, object>? providerSendMessage;
        private readonly bool registeredApiVersion;
        private readonly bool registeredSendMessage;

        public DiscordBridgeProvider(IDalamudPluginInterface pluginInterface, IDiscordBridgeAPI api)
        {
            try
            {
                this.providerAPIVersion = pluginInterface.GetIpcProvider<int>(LabelProviderApiVersion);
                this.providerAPIVersion.RegisterFunc(() => api.APIVersion);
                this.registeredApiVersion = true;

                this.providerSendMessage =
                    pluginInterface.GetIpcProvider<string, string, string, object>(LabelProviderSendMessage);
                this.providerSendMessage.RegisterAction(api.SendMessage);
                this.registeredSendMessage = true;
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Could not register the DiscordBridge.* IPC endpoints. Another plugin (probably the " +
                                   "official Discord Chat Bridge) already provides them. Disable it to let other plugins send through " +
                                   Constant.PluginName + ".");
            }
        }

        /// <summary>Whether this plugin registered both IPC endpoints (false if another plugin owns them).</summary>
        public bool OwnsIpc => this.registeredApiVersion && this.registeredSendMessage;

        public void Dispose()
        {
            if (this.registeredApiVersion)
                this.providerAPIVersion?.UnregisterFunc();
            if (this.registeredSendMessage)
                this.providerSendMessage?.UnregisterAction();
        }
    }
}
