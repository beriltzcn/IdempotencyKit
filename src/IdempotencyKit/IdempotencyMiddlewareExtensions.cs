using Microsoft.AspNetCore.Builder;   // IApplicationBuilder, UseMiddleware

namespace IdempotencyKit;

/// <summary>
/// Middleware'i boru hattına eklemeyi sağlayan uzantı.
/// </summary>
public static class IdempotencyMiddlewareExtensions
{
    public static IApplicationBuilder UseIdempotency(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // UseMiddleware, sınıfı bulup yapıcısındaki bağımlılıkları DI'dan çözer.
        return app.UseMiddleware<IdempotencyMiddleware>();
    }
}
