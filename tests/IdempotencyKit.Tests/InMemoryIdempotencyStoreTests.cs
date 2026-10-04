using IdempotencyKit.Stores;    
using Microsoft.Extensions.Options;

namespace IdempotencyKit.Tests;

public class InMemoryIdempotencyStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static InMemoryIdempotencyStore CreateStore()
       => new(Options.Create(new IdempotencyOptions()));

    [Fact]
    public async Task TryAcquireAsync_WhenKeyIsNew_ReturnsAcquired()
    {
        var store = CreateStore();

        var result = await store.TryAcquireAsync("key-1", "fingerprint-1", Now);

        Assert.Equal(IdempotencyAcquireStatus.Acquired, result.Status);
    }

    [Fact]
    public async Task TryAcquireAsync_WhenKeyIsAlreadyInProgress_ReturnsInProgress()
    {
        var store = CreateStore();
        await store.TryAcquireAsync("key-1", "fingerprint-1", Now);
        var second = await store.TryAcquireAsync("key-1", "fingerprint-1", Now.AddSeconds(1));

        Assert.Equal(IdempotencyAcquireStatus.InProgress, second.Status);

    }

    [Fact]
    public async Task TryAcquireAsync_WhenKeyIsCompleted_ReturnsStoredResponse()
    {
        var store = CreateStore();
        await store.TryAcquireAsync("key-1", "fingerprint-1", Now);

        var response = new StoredResponse(201, "application/json", new byte[] { 1, 2, 3 });

        await store.CompleteAsync("key-1", response, Now.AddHours(24));

        var replay = await store.TryAcquireAsync("key-1", "fingerprint-1", Now.AddSeconds(5));

        Assert.Equal(IdempotencyAcquireStatus.Completed, replay.Status);
        Assert.Equal(201, replay.Response!.StatusCode);
        Assert.Equal("application/json", replay.Response.ContentType);
        Assert.Equal(new byte[] { 1, 2, 3 }, replay.Response.Body);
    }

    [Fact]
    public async Task TryAcquireAsync_WhenManyRequestArriveAtOnce_OnlyOneIsAcquired()
    {
        var store = CreateStore();
        const int requestCount = 100;

        var gate = new TaskCompletionSource<bool>();

        var tasks = Enumerable.Range(0, requestCount)
            .Select(_ => Task.Run(async () =>
            {
                await gate.Task;
                return await store.TryAcquireAsync("key-1", "fingerprint-1", Now);
            }))
            .ToArray();

        gate.SetResult(true);

        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(r => r.Status == IdempotencyAcquireStatus.Acquired));
        Assert.Equal(requestCount - 1, results.Count(r => r.Status == IdempotencyAcquireStatus.InProgress));
    }

    [Fact]
    public async Task TryAcquireAsync_WhenProcessingTimeoutElapsed_ReturnsAcquired()
    {
        var store = CreateStore();

        await store.TryAcquireAsync("key-1", "fingerprint-1", Now);

        var later = await store.TryAcquireAsync("key-1", "fingerprint-1", Now.AddMinutes(2));

        Assert.Equal(IdempotencyAcquireStatus.Acquired, later.Status);
    }

    [Fact]
    public async Task TryAcquireAsync_AfterRetentionPeriodElapsed_ReturnsAcquired()
    {
        var store = CreateStore();
        await store.TryAcquireAsync("key-1", "fingerprint-1", Now);

        var response = new StoredResponse(200, "application/json", new byte[] { 1 });
        await store.CompleteAsync("key-1", response, Now.AddHours(24));

        var stillValid = await store.TryAcquireAsync("key-1", "fingerprint-1", Now.AddHours(1));
        Assert.Equal(IdempotencyAcquireStatus.Completed, stillValid.Status);

        var afterExpiry = await store.TryAcquireAsync("key-1", "fingerprint-1", Now.AddHours(25));
        Assert.Equal(IdempotencyAcquireStatus.Acquired, afterExpiry.Status);
    }

    [Fact]
    public async Task ReleaseAsync_WhenKeyIsReleased_AllowsReacquisition()
    {
        var store = CreateStore();
        await store.TryAcquireAsync("key-1", "fingerprint-1", Now);

        await store.ReleaseAsync("key-1");

        var retry = await store.TryAcquireAsync("key-1", "fingerprint-1", Now.AddSeconds(1));

        Assert.Equal(IdempotencyAcquireStatus.Acquired, retry.Status);
    }
}
