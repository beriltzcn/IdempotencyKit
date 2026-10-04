using IdempotencyKit.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IdempotencyKit;

public static class IdempotencyServiceCollectionExtensions
{
    public static IServiceCollection AddIdempotencyKit(
        this IServiceCollection services,
        Action<IdempotencyOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();

        services.TryAddSingleton<IIdempotencyFingerprintGenerator, Sha256FingerprintGenerator>();

        return services;
    }
}
