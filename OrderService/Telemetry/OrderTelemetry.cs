using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace OrderService.Telemetry;

public static class OrderTelemetry
{
    public const string ServiceName    = "order-service";
    public const string ServiceVersion = "1.0.0";
    public const string SourceName     = "OrderService";
    public const string MeterName      = "OrderService";

    public static readonly ActivitySource Source = new(SourceName, ServiceVersion);

    private static readonly Meter _meter = new(MeterName, ServiceVersion);

    public static readonly Counter<long> OrdersCreated =
        _meter.CreateCounter<long>(
            name:        "orders.created",
            unit:        "orders",
            description: "Total number of successfully created and fulfilled orders");

    public static readonly Counter<long> OrdersFailed =
        _meter.CreateCounter<long>(
            name:        "orders.failed",
            unit:        "orders",
            description: "Total number of orders that could not be fulfilled");

    public static readonly Histogram<double> FulfillmentDuration =
        _meter.CreateHistogram<double>(
            name:        "orders.fulfillment.duration",
            unit:        "ms",
            description: "End-to-end duration from order receipt to response in milliseconds");
}
