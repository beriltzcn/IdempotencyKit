namespace IdempotencyKit;

/// <summary>
/// "Bu endpoint çift işlemeye karşı korunmalı" anlamına gelen işaret.
/// Middleware, endpoint'in üzerinde bu işareti arayarak çalışıp çalışmayacağına karar verir.
/// </summary>
public sealed class IdempotencyMetadata
{
    // Yapıcıyı private yaptık: dışarıdan yeni örnek üretilemesin.
    // Tek bir ortak örnek yeter, hep aynı nesneyi kullanacağız.
    private IdempotencyMetadata()
    {
    }

    public static IdempotencyMetadata Instance { get; } = new();
}
