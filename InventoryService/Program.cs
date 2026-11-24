using InventoryService.Telemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// A single ResourceBuilder instance is shared across all three signal pipelines
// so every span, metric data point and log record identifies the same service.
var resource = ResourceBuilder.CreateDefault()
    .AddService(
        serviceName:    InventoryTelemetry.ServiceName,
        serviceVersion: InventoryTelemetry.ServiceVersion);

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .SetResourceBuilder(resource)
        .AddAspNetCoreInstrumentation(opts => opts.RecordException = true)
        .AddHttpClientInstrumentation()
        .AddSource(InventoryTelemetry.SourceName)
        .AddConsoleExporter())
    .WithMetrics(metrics => metrics
        .SetResourceBuilder(resource)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddMeter(InventoryTelemetry.MeterName)
        .AddConsoleExporter((exporterOptions, metricReaderOptions) =>
        {
            // Shorter interval so demo traffic appears in the terminal quickly.
            metricReaderOptions.PeriodicExportingMetricReaderOptions
                .ExportIntervalMilliseconds = 5000;
        }));

// Logging wires through the standard ILoggingBuilder.
// IncludeScopes injects TraceId and SpanId into every structured log record
// that fires inside an active span.
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
