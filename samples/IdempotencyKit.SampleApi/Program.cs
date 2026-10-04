using IdempotencyKit;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddIdempotencyKit();

var app = builder.Build();

app.UseRouting();

app.UseIdempotency();

var orders = new List<Order>();

app.MapGet("/orders", () => Results.Ok(orders));

app.MapPost("/orders", async (OrderRequest request) =>
{
    await Task.Delay(TimeSpan.FromSeconds(1));

    var order = new Order(Guid.NewGuid(), request.ProductName, request.Quantity);
    orders.Add(order);

    return Results.Created($"/orders/{order.Id}", order);
})
.RequireIdempotency();

app.Run();

internal sealed record OrderRequest(string ProductName, int Quantity);
internal sealed record Order(Guid Id, string ProductName, int Quantity);
