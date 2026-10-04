using System;
using System.Collections.Generic;
using System.Text;

namespace IdempotencyKit.Tests;

public class IdempotencyOptionsTests
{
    [Fact]
    public void DefaultOptions_HaveExpectedValues()
    {
        var options = new IdempotencyOptions();

        Assert.Equal("Idempotency-Key", options.HeaderName);
        Assert.Equal(TimeSpan.FromHours(24), options.RetentionPeriod);
        Assert.True(options.RequireKey);
    }
}
