using FluentValidation;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using Scalar.AspNetCore;

namespace Github.Release.Proxy;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        var settings = builder.Configuration.GetSection("Settings").Get<Settings>()!;

        new SettingsValidator().ValidateAndThrow(settings);

        builder.Services.AddSingleton(settings);

        builder.Services.AddOpenApi();

        builder.Services.AddSingleton<Instrumentation>();

        if (settings.LogFormat == LogFormat.JSON)
        {
            builder.Logging.AddJsonConsole(options =>
            {
                options.IncludeScopes = false;
                options.TimestampFormat = "HH:mm:ss";
            });
        }

        builder.Logging.AddFilter("Default", settings.LogLevel);
        builder.Logging.AddFilter("Github", settings.LogLevel);
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.Extensions.Diagnostics.HealthChecks", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.AspNetCore.DataProtection", LogLevel.Error);

        builder.Services
            .AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics
                    .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(serviceName: "github-release-proxy"))
                    .AddRuntimeInstrumentation()
                    .AddAspNetCoreInstrumentation()
                    .AddEventCountersInstrumentation(c =>
                    {
                        c.AddEventSources(
                            "Microsoft.AspNetCore.Hosting",
                            "Microsoft-AspNetCore-Server-Kestrel",
                            "System.Net.Http",
                            "System.Net.Sockets");
                    })
                    .AddView("request-duration", new ExplicitBucketHistogramConfiguration
                    {
                        Boundaries = [0, 0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10]
                    })
                    .AddMeter(
                        "Microsoft.AspNetCore.Hosting",
                        "Microsoft.AspNetCore.Server.Kestrel",
                        Instrumentation.Prefix
                    )
                    .AddPrometheusExporter();
            });

        builder.Services.AddMetrics();
        builder.Services.AddHealthChecks();
        builder.Services.Configure<ForwardedHeadersOptions>(options => options.ForwardedHeaders = ForwardedHeaders.All);
        builder.Services.AddHttpClient(string.Empty).AddStandardResilienceHandler();

        var app = builder.Build();
        app.Logger.LogInformation("Starting Application");
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference();
        }
        app.UseForwardedHeaders();
        app.MapPrometheusScrapingEndpoint();
        app.MapHealthChecks("/health");

        app.MapGet("/release/{version}/{filename}", async (string version, string filename, [FromServices] Settings settings, [FromServices] Instrumentation instrumentation, [FromServices] HttpClient client) =>
        {
            instrumentation.ReleasesDownloaded.Add(1);

            var stream = await client.GetStreamAsync($"https://github.com/{settings.Organization}/{settings.Project}/releases/download/{version}/{filename}");

            return Results.File(stream, fileDownloadName: filename);
        });

        app.Run();
    }
}
