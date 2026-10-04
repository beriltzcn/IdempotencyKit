using System;
using System.Collections.Generic;
using System.Text;

namespace IdempotencyKit;

public sealed record IdempotencyAcquireResult(IdempotencyAcquireStatus Status, string? FingerPrint = null, StoredResponse? Response = null)
{
    public static IdempotencyAcquireResult Acquired { get; } = new(IdempotencyAcquireStatus.Acquired);
    public static IdempotencyAcquireResult InProgress { get; } = new(IdempotencyAcquireStatus.InProgress);
    public static IdempotencyAcquireResult Completed(string fingerprint, StoredResponse response) => new(IdempotencyAcquireStatus.Completed, fingerprint, response);

}
