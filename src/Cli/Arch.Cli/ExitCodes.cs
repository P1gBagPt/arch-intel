namespace Arch.Cli;

/// <summary>Script-friendly exit codes shared across every command (03-cli.md Section 3.5).</summary>
public static class ExitCodes
{
    public const int Success = 0;
    public const int UnexpectedError = 1;
    public const int UserError = 2;
    public const int ConfigurationError = 3;
    public const int EnvironmentError = 4;
    public const int ScanFailed = 5;
}
