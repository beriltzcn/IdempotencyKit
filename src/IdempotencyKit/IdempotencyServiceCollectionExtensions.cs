using IdempotencyKit.Stores;                               // InMemoryIdempotencyStore
using Microsoft.Extensions.DependencyInjection;            // IServiceCollection, Configure
using Microsoft.Extensions.DependencyInjection.Extensions; // TryAddSingleton

namespace IdempotencyKit;

/// <summary>
/// Kütüphaneyi servis konteynerine kaydeden uzantı metotları.
/// </summary>
public static class IdempotencyServiceCollectionExtensions
{
    public static IServiceCollection AddIdempotencyKit(
        this IServiceCollection services,
        Action<IdempotencyOptions>? configure = null)      // İsteğe bağlı ayar
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null)
        {
            services.Configure(configure);
        }

        // DİKKAT, KRİTİK KARAR: depo Singleton olmak zorunda.
        // Singleton = uygulama boyunca tek bir örnek. Anahtarları paylaşan tek bir
        // depo olmalı ki ikinci istek birincinin bıraktığını görebilsin.
        // Scoped yapsaydık her istek kendi deposunu alırdı ve koruma HİÇ çalışmazdı.
        // Bu, DI ömürlerinin en klasik tuzağıdır.
        services.TryAddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();

        // Parmak izi üreticisi durum tutmaz, tek örnek yeter.
        services.TryAddSingleton<IIdempotencyFingerprintGenerator, Sha256FingerprintGenerator>();

        // TryAdd kullandık, Add değil: kullanıcı kendi deposunu kaydetmişse
        // bizimki onun üzerine yazmasın.
        return services;
    }
}
