using System;
using System.Collections.Generic;

namespace Dalamud.DiscordBridge
{
    /// <summary>
    /// One reusable client per key (#40): webhook clients per Discord channel. A webhook client owns an HttpClient, so
    /// creating one per message leaked them. Evicted and remaining clients are disposed. Thread-safe; pure (unit tested).
    /// </summary>
    internal sealed class ClientCache<TClient> : IDisposable where TClient : class, IDisposable
    {
        private readonly Dictionary<ulong, TClient> clients = [];
        private readonly object gate = new();
        private bool disposed;

        internal int Count
        {
            get
            {
                lock (this.gate)
                    return this.clients.Count;
            }
        }

        internal bool TryGet(ulong key, out TClient? client)
        {
            lock (this.gate)
                return this.clients.TryGetValue(key, out client);
        }

        /// <summary>
        /// Store a newly created client. If another caller stored one first, the new one is disposed and the existing
        /// one returned, so there's never more than one per key. After <see cref="Dispose"/> (the handler is shutting
        /// down), the client is disposed and null returned.
        /// </summary>
        internal TClient? Add(ulong key, TClient client)
        {
            lock (this.gate)
            {
                if (!this.disposed)
                {
                    if (this.clients.TryGetValue(key, out var existing))
                    {
                        client.Dispose();
                        return existing;
                    }

                    this.clients[key] = client;
                    return client;
                }
            }

            client.Dispose();
            return null;
        }

        /// <summary>Forget and dispose the client for <paramref name="key"/> (e.g. its webhook was deleted).</summary>
        internal void Evict(ulong key)
        {
            TClient? removed;
            lock (this.gate)
            {
                if (!this.clients.Remove(key, out removed))
                    return;
            }

            removed.Dispose();
        }

        public void Dispose()
        {
            List<TClient> all;
            lock (this.gate)
            {
                this.disposed = true;
                all = [.. this.clients.Values];
                this.clients.Clear();
            }

            foreach (var client in all)
                client.Dispose();
        }
    }
}
