using FluentValidation;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using Scalar.AspNetCore;
using Microsoft.Extensions.Caching.Memory;
using System.IO;

namespace GitHub.Release.Proxy;

internal sealed record CachedRelease(byte[] Content, string ContentType, long? ContentLength, string FileName);

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
        builder.Logging.AddFilter("Microsoft.AspNetCore", settings.LogLevel);
        builder.Logging.AddFilter("Microsoft.Extensions.Diagnostics.HealthChecks", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning);

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
        builder.Services.AddMemoryCache();
        builder.Services.Configure<ForwardedHeadersOptions>(options => options.ForwardedHeaders = ForwardedHeaders.All);
        builder.Services.AddHttpClient(string.Empty).AddStandardResilienceHandler();

        var app = builder.Build();
        app.Logger.LogInformation("Starting Application");

        app.MapOpenApi();
        if (app.Environment.IsDevelopment())
        {
            app.MapScalarApiReference();
        }
        app.UseForwardedHeaders();
        app.MapPrometheusScrapingEndpoint();
        app.MapHealthChecks("/health");

        // Local helper to fetch and optionally cache release artifacts
        async Task ServeReleaseAsync(string cacheKey, string requestUrl, string fileName, HttpContext httpContext, Settings settings, Instrumentation instrumentation, HttpClient client, IMemoryCache cache)
        {
            instrumentation.ReleasesDownloaded.Add(1);

            if (cache.TryGetValue<CachedRelease>(cacheKey, out var cached) && cached is not null)
            {
                httpContext.Response.ContentType = cached.ContentType;
                if (cached.ContentLength.HasValue)
                {
                    httpContext.Response.ContentLength = cached.ContentLength.Value;
                }
                httpContext.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{cached.FileName}\"";

                await httpContext.Response.Body.WriteAsync(cached.Content, httpContext.RequestAborted);
                return;
            }

            using var response = await client.GetAsync(requestUrl, HttpCompletionOption.ResponseHeadersRead, httpContext.RequestAborted);
            response.EnsureSuccessStatusCode();

            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            var contentBytes = await response.Content.ReadAsByteArrayAsync(httpContext.RequestAborted);

            var toCache = new CachedRelease(contentBytes, contentType, response.Content.Headers.ContentLength, fileName);
            cache.Set(cacheKey, toCache, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(10) });

            httpContext.Response.ContentType = contentType;
            if (response.Content.Headers.ContentLength.HasValue)
            {
                httpContext.Response.ContentLength = response.Content.Headers.ContentLength.Value;
            }
            httpContext.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{fileName}\"";

            await httpContext.Response.Body.WriteAsync(contentBytes, httpContext.RequestAborted);
        }

        //[Obsolete]
        app.MapGet("/release/{version}/{filename}", (string version, string filename, HttpContext httpContext, [FromServices] Settings settings, [FromServices] Instrumentation instrumentation, [FromServices] HttpClient client, [FromServices] IMemoryCache cache) =>
        {
            var cacheKey = $"release:{settings.Organization}:{settings.Project}:{version}:{filename}";
            var requestUrl = $"https://github.com/{settings.Organization}/{settings.Project}/releases/download/{version}/{filename}";

            return ServeReleaseAsync(cacheKey, requestUrl, filename, httpContext, settings, instrumentation, client, cache);
        });

        app.MapGet("/releases/download/{version}/{artifactName}", (string version, string artifactName, HttpContext httpContext, [FromServices] Settings settings, [FromServices] Instrumentation instrumentation, [FromServices] HttpClient client, [FromServices] IMemoryCache cache) =>
        {
            var cacheKey = $"release:{settings.Organization}:{settings.Project}:{version}:{artifactName}";
            var requestUrl = $"https://github.com/{settings.Organization}/{settings.Project}/releases/download/{version}/{artifactName}";

            return ServeReleaseAsync(cacheKey, requestUrl, artifactName, httpContext, settings, instrumentation, client, cache);
        });

        app.Run();
    }
}
