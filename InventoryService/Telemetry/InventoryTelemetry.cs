using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace InventoryService.Telemetry;

public static class InventoryTelemetry
{
    public const string ServiceName    = "inventory-service";
    public const string ServiceVersion = "1.0.0";
    public const string SourceName     = "InventoryService";
    public const string MeterName      = "InventoryService";

    // ActivitySource is the .NET API for creating trace spans.
    // It returns null from StartActivity() when no TracerProvider is listening,
    // making it safe to use in test environments without the OTel SDK.
    public static readonly ActivitySource Source = new(SourceName, ServiceVersion);

    // Meter and instruments use System.Diagnostics.Metrics.
    // These types are part of the .NET runtime — no OTel package reference required here.
    private static readonly Meter _meter = new(MeterName, ServiceVersion);

    public static readonly Counter<long> StockChecks =
        _meter.CreateCounter<long>(
            name:        "inventory.stock.checks",
            unit:        "checks",
            description: "Total number of stock availability checks performed");

    public static readonly Histogram<double> StockCheckDuration =
        _meter.CreateHistogram<double>(
            name:        "inventory.stock.check.duration",
            unit:        "ms",
            description: "Duration of a single stock check in milliseconds");

    public static readonly UpDownCounter<long> ReservedItems =
        _meter.CreateUpDownCounter<long>(
            name:        "inventory.reserved.items",
            unit:        "items",
            description: "Net number of items currently reserved across all orders");
}
