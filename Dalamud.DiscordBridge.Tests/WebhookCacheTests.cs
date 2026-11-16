using System.Net;

namespace Dalamud.DiscordBridge.Tests;

public class WebhookCacheTests
{
    private sealed class FakeClient : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => this.Disposed = true;
    }

    [Fact]
    public void Add_ThenTryGet_ReturnsSameClient()
    {
        using var cache = new ClientCache<FakeClient>();
        var client = new FakeClient();

        Assert.Same(client, cache.Add(1, client));
        Assert.True(cache.TryGet(1, out var found));
        Assert.Same(client, found);
    }

    [Fact]
    public void Add_SecondForSameKey_KeepsFirstAndDisposesSecond()
    {
        using var cache = new ClientCache<FakeClient>();
        var first = new FakeClient();
        var second = new FakeClient();
        cache.Add(1, first);

        Assert.Same(first, cache.Add(1, second));
        Assert.True(second.Disposed);
        Assert.False(first.Disposed);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void Evict_DisposesAndForgets()
    {
        using var cache = new ClientCache<FakeClient>();
        var client = new FakeClient();
        cache.Add(1, client);

        cache.Evict(1);

        Assert.True(client.Disposed);
        Assert.False(cache.TryGet(1, out _));
        cache.Evict(1); // unknown key: no-op
    }

    [Fact]
    public void Dispose_DisposesEveryClient_AndLaterAddsAreDisposed()
    {
        var cache = new ClientCache<FakeClient>();
        var a = new FakeClient();
        var b = new FakeClient();
        cache.Add(1, a);
        cache.Add(2, b);

        cache.Dispose();
        var late = new FakeClient();

        Assert.True(a.Disposed);
        Assert.True(b.Disposed);
        Assert.Null(cache.Add(3, late));
        Assert.True(late.Disposed);
    }

    [Fact]
    public void ParallelAdds_LeaveOneClientPerKey_AndDisposeTheRest()
    {
        using var cache = new ClientCache<FakeClient>();
        var clients = Enumerable.Range(0, 200).Select(_ => new FakeClient()).ToArray();

        Parallel.ForEach(clients, c => cache.Add(7, c));

        Assert.Equal(1, cache.Count);
        Assert.Equal(199, clients.Count(c => c.Disposed));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, null, true)]
    [InlineData(HttpStatusCode.BadRequest, WebhookRules.UnknownWebhookCode, true)]
    [InlineData(HttpStatusCode.Forbidden, 50013, false)]
    [InlineData(HttpStatusCode.TooManyRequests, null, false)]
    [InlineData(HttpStatusCode.InternalServerError, null, false)]
    public void IsGone_OnlyForDeletedWebhooks(HttpStatusCode http, int? code, bool expected)
    {
        Assert.Equal(expected, WebhookRules.IsGone(http, code));
    }

    [Theory]
    [InlineData(0UL, 5UL, true)]
    [InlineData(5UL, 6UL, true)]
    [InlineData(5UL, 5UL, false)]
    public void ShouldSaveId_OnlyWhenChanged(ulong stored, ulong current, bool expected)
    {
        Assert.Equal(expected, WebhookRules.ShouldSaveId(stored, current));
    }

    [Fact]
    public void Handler_NeverCreatesWebhookClientsOutsideTheCache()
    {
        // Regression (#40): a new DiscordWebhookClient per message leaked an HttpClient each time.
        var source = File.ReadAllText(Path.Combine(DocumentationTests.Root, "Dalamud.DiscordBridge", "DiscordHandler.cs"));

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(source, @"new DiscordWebhookClient\("));
        Assert.Contains("this.webhooks.Add(channel.Id, new DiscordWebhookClient(hook))", source);
        Assert.Contains("this.webhooks.Dispose()", source);
    }
}
