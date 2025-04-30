using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;
using OpenTelemetry.Metrics;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Context.Propagation;

var builder = WebApplication.CreateBuilder(args);

// Configure OpenTelemetry
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddOpenTelemetry(options =>
{
    options.IncludeFormattedMessage = true;
    options.IncludeScopes = true;
    options.ParseStateValues = true;
    options.AddOtlpExporter(exporterOptions =>
    {
        exporterOptions.Protocol = OtlpExportProtocol.HttpProtobuf;
    });
});

// Set resource info
var resourceBuilder = ResourceBuilder.CreateDefault()
    .AddService("HttpClientMvcApp");

// Tracing
builder.Services.AddOpenTelemetry()
    .WithTracing(tracerProviderBuilder => tracerProviderBuilder
        .SetResourceBuilder(resourceBuilder)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(exporterOptions =>
        {
            exporterOptions.Protocol = OtlpExportProtocol.HttpProtobuf;
        })
        .AddConsoleExporter()
    )
    .WithMetrics(metricsBuilder => metricsBuilder
        .SetResourceBuilder(resourceBuilder)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddProcessInstrumentation()
        .AddOtlpExporter(exporterOptions =>
        {
            exporterOptions.Protocol = OtlpExportProtocol.HttpProtobuf;
        })
    );

// MVC and polling service
builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient();
builder.Services.AddHostedService<RandomPollingService>();

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();