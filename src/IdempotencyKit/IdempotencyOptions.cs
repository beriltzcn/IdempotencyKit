using System;
using System.Collections.Generic;
using System.Text;

namespace IdempotencyKit;

public sealed class IdempotencyOptions
{
    public string HeaderName { get; set; } = "Idempotency-Key";
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromHours(24);
    public int MaxKeyLength { get; set; } = 255;
    public bool RequireKey { get; set; } = true;
    public TimeSpan ProcessingTimeout { get; set; } = TimeSpan.FromMinutes(1);
    public long MaxResponseBodyBytes { get; set; } = 1_048_576;

}
