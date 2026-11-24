using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using OrderService.Controllers;

namespace OtelDotnetDemo.Tests;

public class OrdersControllerTests
{
    [Fact]
    public async Task CreateOrder_Success_ReturnsCreated()
    {
        var handler = new StubHandler(async (request, _) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return Json(HttpStatusCode.OK, new
                {
                    productId = "PROD-001",
                    quantity = 150,
                    available = true
                });
            }

            return Json(HttpStatusCode.OK, new
            {
                productId = "PROD-001",
                reserved = 5,
                remaining = 145
            });
        });

        var sut = CreateSut(handler);
        var result = await sut.CreateOrder(
            new CreateOrderRequest("PROD-001", 5, "CUST-42"),
            CancellationToken.None) as CreatedResult;

        Assert.NotNull(result);
        Assert.Equal(201, result.StatusCode);
    }

    [Fact]
    public async Task CreateOrder_InsufficientStock_ReturnsConflict()
    {
        var handler = new StubHandler((_, _) =>
            Task.FromResult(Json(HttpStatusCode.OK, new
            {
                productId = "PROD-003",
                quantity = 0,
                available = false
            })));

        var sut = CreateSut(handler);
        var result = await sut.CreateOrder(
            new CreateOrderRequest("PROD-003", 1, "CUST-07"),
            CancellationToken.None) as ConflictObjectResult;

        Assert.NotNull(result);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task CreateOrder_NonPositiveQuantity_ReturnsBadRequest()
    {
        var sut = CreateSut(new StubHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));

        var result = await sut.CreateOrder(
            new CreateOrderRequest("PROD-001", 0, "CUST-42"),
            CancellationToken.None) as BadRequestObjectResult;

        Assert.NotNull(result);
        Assert.Equal(400, result.StatusCode);
    }

    private static OrdersController CreateSut(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:5002")
        };
        var factory = new FixedHttpClientFactory(client);
        return new OrdersController(factory, NullLogger<OrdersController>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object body) =>
        new(status)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json")
        };

    private sealed class FixedHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            responder(request, cancellationToken);
    }
}
