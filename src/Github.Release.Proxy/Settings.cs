namespace GitHub.Release.Proxy;

public class Settings
{
    public LogLevel LogLevel { get; set; }

    public LogFormat LogFormat { get; set; }

    public string Organization { get; set; } = null!;

    public string Project { get; set; } = null!;
}


public enum LogFormat
{
    Simple,
    JSON,
}
