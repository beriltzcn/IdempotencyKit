using Microsoft.AspNetCore.Builder;   // IEndpointConventionBuilder

namespace IdempotencyKit;

/// <summary>
/// Endpoint'e "ben korunmak istiyorum" demeyi sağlayan uzantı.
/// </summary>
public static class IdempotencyEndpointExtensions
{
    // IEndpointConventionBuilder kullanıyoruz çünkü hem minimal API'ler
    // (app.MapPost) hem de MVC controller'ları bu arayüzü uygular.
    public static IEndpointConventionBuilder RequireIdempotency(this IEndpointConventionBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Endpoint'in üzerine işaretimizi iliştiriyoruz.
        // Middleware çalışma anında bu işareti arayacak.
        return builder.WithMetadata(IdempotencyMetadata.Instance);
    }
}
