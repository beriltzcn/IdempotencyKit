using System;
using System.Collections.Generic;
using System.Text;

namespace IdempotencyKit
{
    public interface IIdempotencyStore
    {
        Task<IdempotencyAcquireResult> TryAcquireAsync(
            string key,
            string fingerprint,
            DateTimeOffset now,
            CancellationToken cancellationToken = default);

        Task CompleteAsync(
            string key,
            StoredResponse response,
            DateTimeOffset now,
            CancellationToken cancellationToken = default);

        Task ReleaseAsync(
            string key,
            CancellationToken cancellationToken = default);
    }

}
