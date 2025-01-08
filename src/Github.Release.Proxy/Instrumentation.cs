using System.Diagnostics.Metrics;

namespace Github.Release.Proxy;

public class Instrumentation
{
    public static string Prefix = "github_release_proxy";
    public Counter<long> ReleasesDownloaded { get; private set; }

    public Instrumentation(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Prefix);

        ReleasesDownloaded = meter.CreateCounter<long>(Prefix + "_release_downloaded", description: "Number of releases downloaded.");
    }
}
