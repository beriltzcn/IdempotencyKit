using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace IdempotencyKit.Stores;

public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    private readonly TimeSpan _processingTimeout;

    public InMemoryIdempotencyStore(IOptions<IdempotencyOptions> options)
    {
        _processingTimeout = options.Value.ProcessingTimeout;
    }
    public Task<IdempotencyAcquireResult> TryAcquireAsync(
        string key,
        string fingerprint,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_entries.TryGetValue(key, out var existing))
            { 
                if(existing.ExpiresAt <= now)
                {
                    var replacement = Entry.InProgress(fingerprint, now + _processingTimeout);

                    if(_entries.TryUpdate(key, replacement, existing))
                        {
                        return Task.FromResult(IdempotencyAcquireResult.Acquired);
                    }
                    continue;
                }

                if(existing.State == EntryState.InProgress)
                {
                    return Task.FromResult(IdempotencyAcquireResult.InProgress);
                }

                return Task.FromResult(
                    IdempotencyAcquireResult.Completed(existing.Fingerprint, existing.Response!));
            }

            var created = Entry.InProgress(fingerprint, now + _processingTimeout);

            if (_entries.TryAdd(key, created))
            {
                return Task.FromResult(IdempotencyAcquireResult.Acquired);
            }
        }
        
    }

    public Task CompleteAsync(
        string key,
        StoredResponse response,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        while(true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_entries.TryGetValue(key, out var existing))
            {
                return Task.CompletedTask;
            }

            var completed = existing.AsCompleted(response, expiresAt);

            if (_entries.TryUpdate(key, completed, existing))
            {
                return Task.CompletedTask;
            }
        }
    }

    public Task ReleaseAsync(string key, CancellationToken cancellationToken = default)
    {
        _entries.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    private enum EntryState
    {
        InProgress,
        Completed
    }

    private sealed class Entry
    {
        private Entry(string fingerprint, DateTimeOffset expiresAt, EntryState state, StoredResponse? response)
        {
            Fingerprint = fingerprint;
            ExpiresAt = expiresAt;
            State = state;
            Response = response;
        }

        public string Fingerprint { get; }
        public DateTimeOffset ExpiresAt { get; }
        public EntryState State { get; }
        public StoredResponse? Response { get; }

        public static Entry InProgress(string fingerprint, DateTimeOffset expiresAt)
            => new(fingerprint, expiresAt, EntryState.InProgress, null);

        public Entry AsCompleted(StoredResponse response, DateTimeOffset expiresAt)
            => new(Fingerprint, expiresAt, EntryState.Completed, response);
    }

}
