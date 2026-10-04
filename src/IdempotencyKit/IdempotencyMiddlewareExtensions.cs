using Microsoft.AspNetCore.Builder;

namespace IdempotencyKit;

public static class IdempotencyMiddlewareExtensions
{
    public static IApplicationBuilder UseIdempotency(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<IdempotencyMiddleware>();
    }
}
