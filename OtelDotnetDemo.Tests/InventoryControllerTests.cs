using InventoryService.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace OtelDotnetDemo.Tests;

public class InventoryControllerTests
{
    private readonly InventoryController _sut = new(NullLogger<InventoryController>.Instance);

    [Fact]
    public void GetStock_KnownProduct_ReturnsAvailableQuantity()
    {
        var result = _sut.GetStock("PROD-001") as OkObjectResult;

        Assert.NotNull(result);
        Assert.Equal(200, result.StatusCode);
        var body = Assert.IsAssignableFrom<object>(result.Value);
        var json = System.Text.Json.JsonSerializer.Serialize(body);
        Assert.Contains("\"Quantity\":150", json);
        Assert.Contains("\"Available\":true", json);
    }

    [Fact]
    public void ReserveStock_InsufficientQuantity_ReturnsConflict()
    {
        var result = _sut.ReserveStock(
            "PROD-003",
            new ReserveRequest(1)) as ConflictObjectResult;

        Assert.NotNull(result);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public void ReserveStock_NonPositiveQuantity_ReturnsBadRequest()
    {
        var result = _sut.ReserveStock(
            "PROD-001",
            new ReserveRequest(0)) as BadRequestObjectResult;

        Assert.NotNull(result);
        Assert.Equal(400, result.StatusCode);
    }
}
