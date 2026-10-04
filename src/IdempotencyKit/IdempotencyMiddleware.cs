using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IdempotencyKit;

public sealed class IdempotencyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IIdempotencyStore _store;
    private readonly IIdempotencyFingerprintGenerator _fingerprintGenerator;
    private readonly IdempotencyOptions _options;
    private readonly ILogger<IdempotencyMiddleware> _logger;

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
        var isProtected = context.GetEndpoint()?.Metadata.GetMetadata<IdempotencyMetadata>() is not null;
        if (!isProtected)
        {
            await _next(context);
            return;
        }

        var key = context.Request.Headers[_options.HeaderName].ToString();

        if (string.IsNullOrWhiteSpace(key))
        {
            if (_options.RequireKey)
            {
                await WriteErrorAsync(context, StatusCodes.Status400BadRequest,
                    $"'{_options.HeaderName}' başlığı zorunludur.");
                return;
            }

            await _next(context);
            return;
        }

        if (key.Length > _options.MaxKeyLength)
        {
            await WriteErrorAsync(context, StatusCodes.Status400BadRequest,
                $"Anahtar en fazla {_options.MaxKeyLength} karakter olabilir.");
            return;
        }

        var fingerprint = await ComputeFingerprintAsync(context);

        var now = DateTimeOffset.UtcNow;
        var acquire = await _store.TryAcquireAsync(key, fingerprint, now, context.RequestAborted);

        switch (acquire.Status)
        {
            case IdempotencyAcquireStatus.InProgress:
                await WriteErrorAsync(context, StatusCodes.Status409Conflict,
                    "Bu anahtarla bir istek şu an işleniyor. Lütfen birazdan tekrar deneyin.");
                return;

            case IdempotencyAcquireStatus.Completed:
                if (!string.Equals(acquire.Fingerprint, fingerprint, StringComparison.Ordinal))
                {
                    await WriteErrorAsync(context, StatusCodes.Status422UnprocessableEntity,
                        "Bu anahtar daha önce farklı bir istek için kullanılmış.");
                    return;
                }

                await ReplayAsync(context, acquire.Response!);
                return;
        }

        await ExecuteAndStoreAsync(context, key, now);
    }

    private async Task<string> ComputeFingerprintAsync(HttpContext context)
    {
        var request = context.Request;

        request.EnableBuffering();

        request.Body.Position = 0;

        using var memory = new MemoryStream();
        await request.Body.CopyToAsync(memory, context.RequestAborted);

        request.Body.Position = 0;

        return _fingerprintGenerator.Generate(
            request.Method,
            request.Path.Value ?? string.Empty,
            memory.ToArray());
    }

    private async Task ExecuteAndStoreAsync(HttpContext context, string key, DateTimeOffset now)
    {
        var originalBody = context.Response.Body;
        var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await _next(context);
        }
        catch
        {
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
                await _store.ReleaseAsync(key, context.RequestAborted);
            }
            else if (buffer.Length > _options.MaxResponseBodyBytes)
            {
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

            buffer.Position = 0;
            await buffer.CopyToAsync(originalBody, context.RequestAborted);
        }
        finally
        {
            context.Response.Body = originalBody;
            await buffer.DisposeAsync();
        }
    }

    private static async Task ReplayAsync(HttpContext context, StoredResponse response)
    {
        context.Response.StatusCode = response.StatusCode;

        if (!string.IsNullOrEmpty(response.ContentType))
        {
            context.Response.ContentType = response.ContentType;
        }

        context.Response.Headers["Idempotency-Replayed"] = "true";

        await context.Response.Body.WriteAsync(response.Body, context.RequestAborted);
    }

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
