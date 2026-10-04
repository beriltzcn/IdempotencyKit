using System.Text.Json;               // Hata cevabını JSON yazmak için
using Microsoft.AspNetCore.Http;      // HttpContext, RequestDelegate, EnableBuffering
using Microsoft.Extensions.Logging;   // ILogger
using Microsoft.Extensions.Options;   // IOptions

namespace IdempotencyKit;

/// <summary>
/// Çift işlemeyi engelleyen kontrol noktası.
/// </summary>
public sealed class IdempotencyMiddleware
{
    private readonly RequestDelegate _next;                                   // Sıradaki adım (endpoint)
    private readonly IIdempotencyStore _store;                                // Anahtar deposu
    private readonly IIdempotencyFingerprintGenerator _fingerprintGenerator;  // Parmak izi üretici
    private readonly IdempotencyOptions _options;                             // Ayarlar
    private readonly ILogger<IdempotencyMiddleware> _logger;                   // Loglama

    // Middleware yapıcısı uygulama başlarken BİR KEZ çağrılır.
    // Bu yüzden buraya enjekte edilen servisler Singleton olmalıdır;
    // Scoped bir servis isteseydik uygulama açılışta hata verirdi.
    public IdempotencyMiddleware(
        RequestDelegate next,
        IIdempotencyStore store,
        IIdempotencyFingerprintGenerator fingerprintGenerator,
        IOptions<IdempotencyOptions> options,
        ILogger<IdempotencyMiddleware> logger)
    {
        _next = next;
        _store = store;
        _fingerprintGenerator = fingerprintGenerator;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // 1) Bu endpoint korunmak istiyor mu? İşaret yoksa hiç karışmayız.
        var isProtected = context.GetEndpoint()?.Metadata.GetMetadata<IdempotencyMetadata>() is not null;
        if (!isProtected)
        {
            await _next(context);
            return;
        }

        // 2) Anahtarı başlıktan oku.
        var key = context.Request.Headers[_options.HeaderName].ToString();

        if (string.IsNullOrWhiteSpace(key))
        {
            if (_options.RequireKey)
            {
                await WriteErrorAsync(context, StatusCodes.Status400BadRequest,
                    $"'{_options.HeaderName}' başlığı zorunludur.");
                return;
            }

            // Anahtar istemiyoruz: koruma uygulanamaz, isteği normal işle.
            await _next(context);
            return;
        }

        if (key.Length > _options.MaxKeyLength)
        {
            await WriteErrorAsync(context, StatusCodes.Status400BadRequest,
                $"Anahtar en fazla {_options.MaxKeyLength} karakter olabilir.");
            return;
        }

        // 3) Parmak izini hesapla. Bunun için gövdeyi okumamız gerekiyor.
        var fingerprint = await ComputeFingerprintAsync(context);

        // 4) Depoya sor: bu anahtarı alabilir miyim?
        var now = DateTimeOffset.UtcNow;
        var acquire = await _store.TryAcquireAsync(key, fingerprint, now, context.RequestAborted);

        switch (acquire.Status)
        {
            case IdempotencyAcquireStatus.InProgress:
                // Aynı anda başka bir istek işliyor.
                await WriteErrorAsync(context, StatusCodes.Status409Conflict,
                    "Bu anahtarla bir istek şu an işleniyor. Lütfen birazdan tekrar deneyin.");
                return;

            case IdempotencyAcquireStatus.Completed:
                // Kayıt var. Ama istek gerçekten aynı istek mi?
                if (!string.Equals(acquire.Fingerprint, fingerprint, StringComparison.Ordinal))
                {
                    // Hayır: aynı makbuz numarası farklı bir iş için kullanılmış.
                    await WriteErrorAsync(context, StatusCodes.Status422UnprocessableEntity,
                        "Bu anahtar daha önce farklı bir istek için kullanılmış.");
                    return;
                }

                // Evet: saklanan cevabı aynen geri oynat.
                await ReplayAsync(context, acquire.Response!);
                return;
        }

        // 5) Anahtarı biz aldık. İşi yaptır, cevabı yakala, kaydet.
        await ExecuteAndStoreAsync(context, key, now);
    }

    /// <summary>
    /// İsteğin gövdesini okuyup parmak izini hesaplar, sonra gövdeyi başa sarar.
    /// </summary>
    private async Task<string> ComputeFingerprintAsync(HttpContext context)
    {
        var request = context.Request;

        // HTTP gövdesi normalde BİR KEZ okunabilir bir akıştır.
        // Biz okursak endpoint boş gövde görür. EnableBuffering bunu çözer:
        // gövdeyi tampona alır ve tekrar okunabilir hale getirir.
        request.EnableBuffering();

        request.Body.Position = 0;

        using var memory = new MemoryStream();
        await request.Body.CopyToAsync(memory, context.RequestAborted);

        // Endpoint de okuyabilsin diye imleci başa al.
        request.Body.Position = 0;

        return _fingerprintGenerator.Generate(
            request.Method,
            request.Path.Value ?? string.Empty,
            memory.ToArray());
    }

    /// <summary>
    /// Endpoint'i çalıştırır, cevabı bir tampona yazar, sonra hem istemciye gönderir
    /// hem de depoya kaydeder.
    /// </summary>
    private async Task ExecuteAndStoreAsync(HttpContext context, string key, DateTimeOffset now)
    {
        // Cevap şu an doğrudan istemciye akıyor. Biz araya bir tampon koyuyoruz:
        // endpoint tampona yazacak, biz onu okuyup hem kaydedecek hem de
        // asıl akışa kopyalayacağız.
        var originalBody = context.Response.Body;
        var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await _next(context);
        }
        catch
        {
            // Endpoint patladı. Kilidi bırak, tamponu at, hatayı yukarı taşı.
            context.Response.Body = originalBody;
            await _store.ReleaseAsync(key, context.RequestAborted);
            await buffer.DisposeAsync();
            throw;
        }

        try
        {
            var isSuccess = context.Response.StatusCode is >= 200 and < 300;

            if (!isSuccess)
            {
                // Hata cevabını saklamıyoruz. Kilidi bırakıyoruz ki
                // istemci aynı anahtarla tekrar deneyebilsin.
                await _store.ReleaseAsync(key, context.RequestAborted);
            }
            else if (buffer.Length > _options.MaxResponseBodyBytes)
            {
                // Cevap çok büyük: saklamak yerine kilidi bırakıyoruz.
                // (Büyük dosya indirmelerinde belleği şişirmemek için.)
                _logger.LogWarning(
                    "Cevap {Size} bayt, sınır {Limit} bayt. Cevap saklanmadı.",
                    buffer.Length, _options.MaxResponseBodyBytes);

                await _store.ReleaseAsync(key, context.RequestAborted);
            }
            else
            {
                var stored = new StoredResponse(
                    context.Response.StatusCode,
                    context.Response.ContentType,
                    buffer.ToArray());

                await _store.CompleteAsync(key, stored, now.Add(_options.RetentionPeriod), context.RequestAborted);
            }

            // Tamponu asıl cevap akışına kopyala; istemci cevabını görsün.
            buffer.Position = 0;
            await buffer.CopyToAsync(originalBody, context.RequestAborted);
        }
        finally
        {
            context.Response.Body = originalBody;
            await buffer.DisposeAsync();
        }
    }

    /// <summary>
    /// Saklanan cevabı istemciye aynen geri oynatır.
    /// </summary>
    private static async Task ReplayAsync(HttpContext context, StoredResponse response)
    {
        context.Response.StatusCode = response.StatusCode;

        if (!string.IsNullOrEmpty(response.ContentType))
        {
            context.Response.ContentType = response.ContentType;
        }

        // Bu başlık sadece bilgilendirme amaçlı: cevabın gerçekten yapılmadığını,
        // saklanan cevabın oynatıldığını gösterir. Hata ayıklarken altın değerinde.
        context.Response.Headers["Idempotency-Replayed"] = "true";

        await context.Response.Body.WriteAsync(response.Body, context.RequestAborted);
    }

    /// <summary>
    /// Standart biçimde JSON hata cevabı yazar.
    /// </summary>
    private static async Task WriteErrorAsync(HttpContext context, int statusCode, string detail)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        var payload = JsonSerializer.Serialize(new
        {
            title = "Idempotency error",
            status = statusCode,
            detail
        });

        await context.Response.WriteAsync(payload, context.RequestAborted);
    }
}
