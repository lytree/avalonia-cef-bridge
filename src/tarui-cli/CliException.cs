namespace Tarui.Cli;

/// <summary>Fatal, user-facing error with a non-zero exit code (1).</summary>
internal sealed class CliException : Exception
{
    public CliException(string message)
        : base(message)
    {
    }

    public CliException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Command-line usage error with exit code 2.</summary>
internal sealed class CliUsageException : Exception
{
    public CliUsageException(string message)
        : base(message)
    {
    }
}
