using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;

namespace Dalamud.DiscordBridge
{
    /// <summary>
    /// In-order work queue drained by a single background task that sleeps on a semaphore when idle.
    /// One item is processed at a time; an exception from one item is logged and never stops the loop.
    /// </summary>
    public class SerialWorkQueue<T>
    {
        private static IPluginLog? Logger => Service.Logger;

        private readonly ConcurrentQueue<T> items = new();
        private readonly SemaphoreSlim signal = new(0);
        private readonly object stateLock = new();
        private readonly string name;

        private Func<T, Task> processor;
        private CancellationTokenSource? cancellation;
        private Task? runner;

        /// <summary>
        /// How long <see cref="Stop"/> waits for an in-flight item before giving up.
        /// </summary>
        private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

        public SerialWorkQueue(string name, Func<T, Task> processor)
        {
            this.name = name;
            this.processor = processor;
        }

        /// <summary>
        /// Replace the processor (takes effect from the next item).
        /// </summary>
        public Func<T, Task> Processor
        {
            get => this.processor;
            set => this.processor = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// Start draining the queue. Does nothing if it's already running.
        /// </summary>
        public void Start()
        {
            lock (this.stateLock)
            {
                if (this.runner is { IsCompleted: false })
                    return;

                this.cancellation?.Dispose();
                this.cancellation = new CancellationTokenSource();
                var token = this.cancellation.Token;
                this.runner = Task.Run(() => this.RunAsync(token));
            }
        }

        /// <summary>
        /// Stop draining the queue. Pending items are kept until <see cref="ClearQueue"/> or the next <see cref="Start"/>.
        /// </summary>
        public void Stop()
        {
            Task? toWait;
            lock (this.stateLock)
            {
                if (this.cancellation == null)
                    return;

                this.cancellation.Cancel();
                toWait = this.runner;
            }

            try
            {
                if (toWait != null && !toWait.Wait(StopTimeout))
                    Logger?.Warning("{0} did not stop within {1}; continuing.", this.name, StopTimeout);
            }
            catch (AggregateException ex)
            {
                Logger?.Error(ex, "{0} faulted while stopping.", this.name);
            }

            lock (this.stateLock)
            {
                this.cancellation?.Dispose();
                this.cancellation = null;
                this.runner = null;
            }
        }

        public void ClearQueue()
        {
            this.items.Clear();

            // Drop the matching wake-ups so the runner doesn't spin through empty dequeues.
            while (this.signal.Wait(0))
            {
            }
        }

        public bool isStopped()
        {
            lock (this.stateLock)
            {
                return this.runner == null || this.runner.IsCompleted;
            }
        }

        /// <summary>
        /// Number of items waiting to be processed.
        /// </summary>
        public int Count => this.items.Count;

        public void Enqueue(T item)
        {
            this.items.Enqueue(item);
            this.signal.Release();
        }

        private async Task RunAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await this.signal.WaitAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (!this.items.TryDequeue(out var item))
                    continue;

                try
                {
                    await this.processor(item).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    // Never let one item end the loop.
                    Logger?.Error(e, "{0}: could not process item.", this.name);
                }
            }
        }
    }
}
