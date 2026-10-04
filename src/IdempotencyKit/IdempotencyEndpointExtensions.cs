using Microsoft.AspNetCore.Builder;

namespace IdempotencyKit;

public static class IdempotencyEndpointExtensions
{
    public static IEndpointConventionBuilder RequireIdempotency(this IEndpointConventionBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithMetadata(IdempotencyMetadata.Instance);
    }
}
