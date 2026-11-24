using System.Diagnostics;
using InventoryService.Telemetry;
using Microsoft.AspNetCore.Mvc;

namespace InventoryService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InventoryController : ControllerBase
{
    // DEMO: in-memory stock store — not thread-safe.
    // Drop-in upgrade: ConcurrentDictionary<string, int> (or a DB-backed repository) for production.
    private static readonly Dictionary<string, int> Stock = new()
    {
        { "PROD-001", 150 },
        { "PROD-002",  40 },
        { "PROD-003",   0 }
    };

    private readonly ILogger<InventoryController> _logger;

    public InventoryController(ILogger<InventoryController> logger)
    {
        _logger = logger;
    }

    [HttpGet("{productId}/stock")]
    public IActionResult GetStock(string productId)
    {
        var sw = Stopwatch.StartNew();

        // StartActivity creates a child span under the incoming HTTP request span
        // that AddAspNetCoreInstrumentation() already created.
        using var activity = InventoryTelemetry.Source.StartActivity("GetStock");
        activity?.SetTag("inventory.product_id", productId);

        InventoryTelemetry.StockChecks.Add(1,
            new KeyValuePair<string, object?>("product.id", productId));

        if (!Stock.TryGetValue(productId, out var qty))
        {
            _logger.LogWarning("Stock check for unknown product {ProductId}", productId);
            activity?.SetStatus(ActivityStatusCode.Error, "Product not found");
            return NotFound(new { ProductId = productId, Error = "Product not found" });
        }

        sw.Stop();
        InventoryTelemetry.StockCheckDuration.Record(
            sw.Elapsed.TotalMilliseconds,
            new KeyValuePair<string, object?>("product.id", productId));

        activity?.SetTag("inventory.quantity", qty);
        _logger.LogInformation(
            "Stock check: product {ProductId} has {Quantity} units available",
            productId, qty);

        return Ok(new { ProductId = productId, Quantity = qty, Available = qty > 0 });
    }

    [HttpPost("{productId}/reserve")]
    public IActionResult ReserveStock(string productId, [FromBody] ReserveRequest request)
    {
        using var activity = InventoryTelemetry.Source.StartActivity("ReserveStock");
        activity?.SetTag("inventory.product_id",          productId);
        activity?.SetTag("inventory.quantity_requested",  request.Quantity);

        if (request.Quantity <= 0)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Invalid quantity");
            return BadRequest(new { Error = "Quantity must be greater than zero" });
        }

        if (!Stock.TryGetValue(productId, out var available) || available < request.Quantity)
        {
            _logger.LogWarning(
                "Insufficient stock for {ProductId}: requested {Requested}, available {Available}",
                productId, request.Quantity, available);
            activity?.SetStatus(ActivityStatusCode.Error, "Insufficient stock");
            return Conflict(new { Error = "Insufficient stock", Available = available });
        }

        Stock[productId] -= request.Quantity;
        InventoryTelemetry.ReservedItems.Add(request.Quantity,
            new KeyValuePair<string, object?>("product.id", productId));

        _logger.LogInformation(
            "Reserved {Quantity} units of {ProductId}. Remaining stock: {Remaining}",
            request.Quantity, productId, Stock[productId]);

        return Ok(new { ProductId = productId, Reserved = request.Quantity, Remaining = Stock[productId] });
    }
}

public record ReserveRequest(int Quantity);
