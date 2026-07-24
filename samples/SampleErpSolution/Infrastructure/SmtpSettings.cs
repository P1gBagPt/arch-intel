namespace SampleErp.Infrastructure;

public sealed class SmtpSettings
{
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; }
}
