using IdempotencyKit;

var builder = WebApplication.CreateBuilder(args);

// Kütüphanemizi servis konteynerine kaydediyoruz.
// Depo ve parmak izi üreticisi burada hazırlanır.
builder.Services.AddIdempotencyKit();

var app = builder.Build();

// Middleware'in endpoint'i tanıyabilmesi için önce routing çalışmalı.
// Routing, isteğin hangi endpoint'e gideceğini belirler; bizim middleware
// endpoint'in üzerindeki işareti okuyacağı için sıralama önemlidir.
app.UseRouting();

// Kendi middleware'imiz: korunmak isteyen endpoint'lerde çift işlemeyi engeller.
app.UseIdempotency();

// Siparişleri bellekte tutuyoruz. Gerçek uygulamada veritabanı olurdu.
// Bu örnekte amaç, kaç sipariş oluştuğunu gözle görebilmek.
var orders = new List<Order>();

app.MapGet("/orders", () => Results.Ok(orders));

app.MapPost("/orders", async (OrderRequest request) =>
{
    // İşlemi bilerek yavaşlatıyoruz: "aynı anda gelen iki istek" durumunu
    // gözle görebilmek için 1 saniye bekletiyoruz.
    await Task.Delay(TimeSpan.FromSeconds(1));

    var order = new Order(Guid.NewGuid(), request.ProductName, request.Quantity);
    orders.Add(order);

    return Results.Created($"/orders/{order.Id}", order);
})
.RequireIdempotency();   // Bu endpoint çift işlemeye karşı korunacak.

app.Run();

internal sealed record OrderRequest(string ProductName, int Quantity);
internal sealed record Order(Guid Id, string ProductName, int Quantity);
