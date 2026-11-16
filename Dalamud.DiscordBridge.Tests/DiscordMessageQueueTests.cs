using System.Collections.Concurrent;
using Dalamud.DiscordBridge.Model;

namespace Dalamud.DiscordBridge.Tests;

public class DiscordMessageQueueTests
{
    private sealed class TestEvent(int id) : QueuedXivEvent
    {
        public int Id { get; } = id;
    }

    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition not met in time.");
            await Task.Delay(5);
        }
    }

    [Fact]
    public async Task Enqueue_ProcessesEventsInOrder()
    {
        var seen = new ConcurrentQueue<int>();
        var queue = new DiscordMessageQueue(e => { seen.Enqueue(((TestEvent)e).Id); return Task.CompletedTask; });
        queue.Start();

        for (var i = 0; i < 50; i++)
            queue.Enqueue(new TestEvent(i));

        await WaitUntil(() => seen.Count == 50);
        Assert.Equal(Enumerable.Range(0, 50), seen);
        queue.Stop();
    }

    [Fact]
    public async Task ProcessorThrows_LoopKeepsRunning()
    {
        var seen = new ConcurrentQueue<int>();
        var queue = new DiscordMessageQueue(e =>
        {
            var id = ((TestEvent)e).Id;
            if (id == 1)
                throw new InvalidOperationException("malformed event");
            seen.Enqueue(id);
            return Task.CompletedTask;
        });
        queue.Start();

        queue.Enqueue(new TestEvent(0));
        queue.Enqueue(new TestEvent(1));
        queue.Enqueue(new TestEvent(2));

        await WaitUntil(() => seen.Count == 2);
        Assert.Equal([0, 2], seen);
        Assert.False(queue.isStopped());
        queue.Stop();
    }

    [Fact]
    public void NewQueue_IsStopped()
    {
        var queue = new DiscordMessageQueue(_ => Task.CompletedTask);

        Assert.True(queue.isStopped());
    }

    [Fact]
    public async Task Stop_ThenStart_ResumesWithPendingEvents()
    {
        var seen = new ConcurrentQueue<int>();
        var queue = new DiscordMessageQueue(e => { seen.Enqueue(((TestEvent)e).Id); return Task.CompletedTask; });
        queue.Start();
        queue.Stop();
        Assert.True(queue.isStopped());

        queue.Enqueue(new TestEvent(7));
        await Task.Delay(50);
        Assert.Empty(seen);

        queue.Start();
        await WaitUntil(() => seen.Count == 1);
        Assert.Equal([7], seen);
        queue.Stop();
    }

    [Fact]
    public async Task ClearQueue_DropsPendingEvents()
    {
        var seen = new ConcurrentQueue<int>();
        var queue = new DiscordMessageQueue(e => { seen.Enqueue(((TestEvent)e).Id); return Task.CompletedTask; });

        queue.Enqueue(new TestEvent(1));
        queue.Enqueue(new TestEvent(2));
        queue.ClearQueue();
        Assert.Equal(0, queue.Count);

        queue.Start();
        queue.Enqueue(new TestEvent(3));
        await WaitUntil(() => seen.Count == 1);
        await Task.Delay(50);
        Assert.Equal([3], seen);
        queue.Stop();
    }

    [Fact]
    public void Start_Twice_IsIdempotent()
    {
        var queue = new DiscordMessageQueue(_ => Task.CompletedTask);

        queue.Start();
        queue.Start();

        Assert.False(queue.isStopped());
        queue.Stop();
        Assert.True(queue.isStopped());
    }
}
