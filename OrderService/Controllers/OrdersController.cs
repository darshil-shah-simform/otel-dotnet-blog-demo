using System.Diagnostics;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using OrderService.Telemetry;

namespace OrderService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(
        IHttpClientFactory httpClientFactory,
        ILogger<OrdersController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger            = logger;
    }

    [HttpPost]
    public async Task<IActionResult> CreateOrder(
        [FromBody] CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        // The parent span is the HTTP request span AddAspNetCoreInstrumentation() created.
        // This business-logic span is recorded as its child.
        using var activity = OrderTelemetry.Source.StartActivity("CreateOrder");
        activity?.SetTag("order.product_id",  request.ProductId);
        activity?.SetTag("order.quantity",    request.Quantity);
        activity?.SetTag("order.customer_id", request.CustomerId);

        if (request.Quantity <= 0)
        {
            _logger.LogWarning(
                "Rejected order with non-positive quantity {Quantity} for {ProductId}",
                request.Quantity, request.ProductId);
            activity?.SetStatus(ActivityStatusCode.Error, "Invalid quantity");
            OrderTelemetry.OrdersFailed.Add(1,
                new KeyValuePair<string, object?>("failure.reason", "invalid_quantity"));
            return BadRequest(new { Error = "Quantity must be greater than zero" });
        }

        _logger.LogInformation(
            "Processing order for customer {CustomerId}: {Quantity}x {ProductId}",
            request.CustomerId, request.Quantity, request.ProductId);

        var client = _httpClientFactory.CreateClient("InventoryClient");

        // Check stock — AddHttpClientInstrumentation() creates a child span here
        // and injects the W3C traceparent header automatically.
        HttpResponseMessage stockResponse;
        try
        {
            stockResponse = await client.GetAsync(
                $"/api/inventory/{request.ProductId}/stock",
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to reach InventoryService for product {ProductId}",
                request.ProductId);
            activity?.SetStatus(ActivityStatusCode.Error, "InventoryService unreachable");
            OrderTelemetry.OrdersFailed.Add(1,
                new KeyValuePair<string, object?>("failure.reason", "inventory_unreachable"));
            return StatusCode(503, new { Error = "Inventory service unavailable" });
        }

        if (!stockResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Inventory check failed for {ProductId}: HTTP {StatusCode}",
                request.ProductId, (int)stockResponse.StatusCode);
            activity?.SetStatus(ActivityStatusCode.Error, "Inventory check failed");
            OrderTelemetry.OrdersFailed.Add(1,
                new KeyValuePair<string, object?>("failure.reason", "inventory_unavailable"));
            return Conflict(new { Error = "Product unavailable or not found" });
        }

        var stockInfo = await stockResponse.Content
            .ReadFromJsonAsync<StockInfo>(cancellationToken: cancellationToken);

        if (stockInfo is null || !stockInfo.Available || stockInfo.Quantity < request.Quantity)
        {
            _logger.LogWarning(
                "Insufficient stock for {ProductId}: available {Available}, requested {Requested}",
                request.ProductId, stockInfo?.Quantity ?? 0, request.Quantity);
            activity?.SetStatus(ActivityStatusCode.Error, "Insufficient stock");
            OrderTelemetry.OrdersFailed.Add(1,
                new KeyValuePair<string, object?>("failure.reason", "insufficient_stock"));
            return Conflict(new { Error = "Insufficient stock", Available = stockInfo?.Quantity ?? 0 });
        }

        // Reserve the stock — another instrumented HttpClient call, another child span.
        HttpResponseMessage reserveResponse;
        try
        {
            reserveResponse = await client.PostAsJsonAsync(
                $"/api/inventory/{request.ProductId}/reserve",
                new { Quantity = request.Quantity },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to reach InventoryService while reserving {ProductId}",
                request.ProductId);
            activity?.SetStatus(ActivityStatusCode.Error, "InventoryService unreachable");
            OrderTelemetry.OrdersFailed.Add(1,
                new KeyValuePair<string, object?>("failure.reason", "inventory_unreachable"));
            return StatusCode(503, new { Error = "Inventory service unavailable" });
        }

        if (!reserveResponse.IsSuccessStatusCode)
        {
            _logger.LogError("Reservation failed for {ProductId}", request.ProductId);
            activity?.SetStatus(ActivityStatusCode.Error, "Reservation failed");
            OrderTelemetry.OrdersFailed.Add(1,
                new KeyValuePair<string, object?>("failure.reason", "reservation_failed"));
            return StatusCode(500, new { Error = "Failed to reserve inventory" });
        }

        var orderId = Guid.NewGuid();
        sw.Stop();

        // Record success metrics with dimensional attributes.
        OrderTelemetry.OrdersCreated.Add(1,
            new KeyValuePair<string, object?>("product.id", request.ProductId));
        OrderTelemetry.FulfillmentDuration.Record(
            sw.Elapsed.TotalMilliseconds,
            new KeyValuePair<string, object?>("product.id", request.ProductId));

        activity?.SetTag("order.id",             orderId);
        activity?.SetTag("order.fulfillment_ms", sw.Elapsed.TotalMilliseconds);

        _logger.LogInformation(
            "Order {OrderId} created for customer {CustomerId}: {Quantity}x {ProductId} in {DurationMs:F1}ms",
            orderId, request.CustomerId, request.Quantity, request.ProductId,
            sw.Elapsed.TotalMilliseconds);

        return Created($"/api/orders/{orderId}", new
        {
            OrderId    = orderId,
            ProductId  = request.ProductId,
            Quantity   = request.Quantity,
            CustomerId = request.CustomerId,
            CreatedAt  = DateTime.UtcNow
        });
    }
}

public record CreateOrderRequest(string ProductId, int Quantity, string CustomerId);

// Matches the JSON shape returned by InventoryController.GetStock.
internal record StockInfo(string ProductId, int Quantity, bool Available);
