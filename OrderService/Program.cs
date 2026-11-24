using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OrderService.Telemetry;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// Register a named HttpClient. AddHttpClientInstrumentation() wraps factory-created
// clients automatically, creating a child span and injecting traceparent for each call.
builder.Services.AddHttpClient("InventoryClient", client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["Services:InventoryService"] ?? "http://localhost:5002");
    client.Timeout = TimeSpan.FromSeconds(5);
});

var resource = ResourceBuilder.CreateDefault()
    .AddService(
        serviceName:    OrderTelemetry.ServiceName,
        serviceVersion: OrderTelemetry.ServiceVersion);

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .SetResourceBuilder(resource)
        .AddAspNetCoreInstrumentation(opts => opts.RecordException = true)
        .AddHttpClientInstrumentation()
        .AddSource(OrderTelemetry.SourceName)
        .AddConsoleExporter())
    .WithMetrics(metrics => metrics
        .SetResourceBuilder(resource)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddMeter(OrderTelemetry.MeterName)
        .AddConsoleExporter((exporterOptions, metricReaderOptions) =>
        {
            metricReaderOptions.PeriodicExportingMetricReaderOptions
                .ExportIntervalMilliseconds = 5000;
        }));

builder.Logging.AddOpenTelemetry(logging =>
{
    logging.SetResourceBuilder(resource);
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes           = true;
    logging.AddConsoleExporter();
});

var app = builder.Build();
app.MapControllers();
app.Run();
