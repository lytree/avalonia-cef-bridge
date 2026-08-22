namespace Tarui.Cli;

/// <summary>Minimal console facade so commands stay testable and output stays uniform.</summary>
internal sealed class CliConsole
{
    public TextWriter Out { get; }

    public TextWriter ErrorWriter { get; }

    public CliConsole(TextWriter? output = null, TextWriter? error = null)
    {
        Out = output ?? Console.Out;
        ErrorWriter = error ?? Console.Error;
    }

    public void WriteLine(string message = "") => Out.WriteLine(message);

    public void Info(string message) => Out.WriteLine(message);

    public void Command(string message) => Out.WriteLine($"  $ {message}");

    public void Warn(string message) => Out.WriteLine($"warning: {message}");

    public void Error(string message) => ErrorWriter.WriteLine($"error: {message}");

    public void Section(string message = "") => Out.WriteLine(message);
}
