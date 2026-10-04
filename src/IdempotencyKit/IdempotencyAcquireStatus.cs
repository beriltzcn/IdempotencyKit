using System;
using System.Collections.Generic;
using System.Text;

namespace IdempotencyKit;

public enum IdempotencyAcquireStatus
{
    Acquired,
    InProgress,
    Completed
}
