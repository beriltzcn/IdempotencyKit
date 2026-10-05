# IdempotencyKit

Idempotency middleware for ASP.NET Core. It stops the same request from being processed twice, and replays the original response when a duplicate arrives.

## The problem

A client sends `POST /orders`. The network is slow, the user clicks the button again, or the client retries on timeout. The server processes the request twice. Two orders are created and the card is charged twice.

This kind of bug is quiet. Nothing crashes, no exception is logged, and nobody notices until a customer complains.

## How it works

The client attaches a unique key to each logical operation:

```
POST /orders
Idempotency-Key: 9f2c1a7b-4d3e-4c1a-9b2f-8e7d6c5b4a39
```

The first request is processed normally and its response is stored under that key. When the same key arrives again, the endpoint is **not** executed. The stored response is replayed instead, with the same status code, content type and body.

Each key lives in one of three states:

| State | Meaning | What happens next |
| --- | --- | --- |
| `Acquired` | The key was free and this request owns it | The endpoint runs |
| `InProgress` | Another request with the same key is running right now | The caller is rejected |
| `Completed` | The work finished and the response was stored | The stored response is replayed |

## Requirements

- .NET 10
- ASP.NET Core

## Installation

> Not published to NuGet yet. Until then, reference the project directly:

```xml
<ItemGroup>
  <ProjectReference Include="..\IdempotencyKit\IdempotencyKit.csproj" />
</ItemGroup>
```

## Quick start

```csharp
using IdempotencyKit;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddIdempotencyKit();

var app = builder.Build();

app.UseRouting();
app.UseIdempotency();

app.MapPost("/orders", (OrderRequest request) =>
{
    var order = CreateOrder(request);
    return Results.Created($"/orders/{order.Id}", order);
})
.RequireIdempotency();

app.Run();
```

Three things are happening here:

`AddIdempotencyKit()` registers the store and the fingerprint generator in the service container.

`UseIdempotency()` adds the middleware to the pipeline. It must come **after** `UseRouting()`, because the middleware needs to know which endpoint is being called before it can decide whether that endpoint is protected.

`RequireIdempotency()` marks a single endpoint as protected. Endpoints without this call are left completely alone.

## Behaviour

| Situation | Response |
| --- | --- |
| `Idempotency-Key` header missing, `RequireKey` is `true` | `400 Bad Request` |
| Key longer than `MaxKeyLength` | `400 Bad Request` |
| First request for a key | Endpoint runs, response stored |
| Duplicate arrives while the first request is still running | `409 Conflict` |
| Duplicate arrives after the first request finished | Stored response replayed, `Idempotency-Replayed: true` header added |
| Same key, different method, path or body | `422 Unprocessable Entity` |
| Endpoint returns a non-2xx status | Nothing stored, key released so the client can retry |
| Endpoint response larger than `MaxResponseBodyBytes` | Nothing stored, key released, warning logged |
| Endpoint throws an exception | Key released, exception rethrown |

The `Idempotency-Replayed: true` header is set only on replayed responses. It makes it easy to tell in logs and during debugging whether the work actually ran.

## Configuration

| Option | Default | Description |
| --- | --- | --- |
| `HeaderName` | `Idempotency-Key` | Name of the request header that carries the key |
| `RetentionPeriod` | 24 hours | How long a stored response stays valid |
| `ProcessingTimeout` | 1 minute | How long a key stays locked while a request is in flight |
| `MaxKeyLength` | 255 | Maximum accepted key length |
| `RequireKey` | `true` | When `false`, requests without a key pass through unprotected |
| `MaxResponseBodyBytes` | 1 MB | Responses larger than this are not stored |

```csharp
builder.Services.AddIdempotencyKit(options =>
{
    options.HeaderName = "X-Request-Key";
    options.RetentionPeriod = TimeSpan.FromHours(6);
    options.ProcessingTimeout = TimeSpan.FromSeconds(30);
});
```

`ProcessingTimeout` exists to protect against stuck keys. If a request takes the key and then the process crashes, the key would otherwise stay locked forever and the client could never retry. Once the timeout passes, the key is treated as abandoned and can be acquired again.

## Storage

The default store keeps keys in the memory of the running process:

```csharp
services.TryAddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
```

That is fine for a single instance and for development. It has two consequences worth knowing: restarting the app forgets every key, and running several instances means each one has its own separate memory, so protection does not apply across them.

For a real multi-instance deployment, implement `IIdempotencyStore` against a shared backend such as Redis or a database, and register it before calling `AddIdempotencyKit()`:

```csharp
builder.Services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();
builder.Services.AddIdempotencyKit();
```

```csharp
public interface IIdempotencyStore
{
    Task<IdempotencyAcquireResult> TryAcquireAsync(
        string key, string fingerprint, DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task CompleteAsync(
        string key, StoredResponse response, DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default);

    Task ReleaseAsync(string key, CancellationToken cancellationToken = default);
}
```

The store is deliberately dumb: it does not know about HTTP, it does not decide policy, and it takes the current time from the caller instead of reading the clock. That last part is what makes the store testable without waiting for real time to pass.

## Design notes

**Fingerprints.** Alongside the key, every request gets a fingerprint: a SHA-256 hash of the HTTP method, the path and the body. This is how the library tells a genuine retry apart from a client reusing the same key for a different operation. Using a hash instead of storing the raw body keeps memory usage fixed and avoids keeping potentially sensitive payloads around.

**Compare-and-swap.** The in-memory store never does "check whether the key exists, then add it". That pattern looks correct but breaks under concurrency: two requests can both see an empty slot and both claim it. Instead it relies on the atomic `TryAdd` and `TryUpdate` operations and retries in a loop when another request wins the race.

**Response capture.** To replay a response later, the middleware swaps the response body stream for a buffer while the endpoint runs, then copies the buffer to the real stream. The response reaches the client unchanged and a copy is kept for storage.

**Failure keeps the key free.** If the endpoint fails, the key is released rather than stored. Storing an error response would mean the client could never retry, and a retry after a transient failure is exactly what you want.

## Running the sample

The repository contains a small API that demonstrates the behaviour:

```bash
dotnet run --project samples/IdempotencyKit.SampleApi
```

Send the same request twice with the same key:

```bash
curl -i -X POST http://localhost:5266/orders \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: demo-1" \
  -d '{"productName":"Pencil","quantity":2}'
```

The first call returns `201 Created`. The second returns `201 Created` as well, with the same order id and the header `Idempotency-Replayed: true`. `GET /orders` shows a single order.

Send two requests with the same key at the same time and one of them receives `409 Conflict` while the other succeeds.

## Building and testing

```bash
dotnet build
dotnet test
```

## Roadmap

- Integration tests covering the middleware (store and fingerprint generator are covered today)
- Redis implementation of `IIdempotencyStore`
- Publishing to NuGet
- Store selected response headers alongside the response body
- Support for MVC controllers by convention

## Contributing

Issues and pull requests are welcome. Please run `dotnet test` before opening a pull request and keep the change focused on a single topic.

## License

MIT
