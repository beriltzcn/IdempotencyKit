using System;
using System.Collections.Generic;
using System.Text;

namespace IdempotencyKit;

public sealed record StoredResponse(int StatusCode, string? ContentType, byte[] Body);
