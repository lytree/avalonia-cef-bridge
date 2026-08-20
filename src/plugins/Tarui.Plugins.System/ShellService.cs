using System.Diagnostics;
using Tarui.Contracts;

namespace Tarui.Plugins.System;

public interface IShellService
{
    ShellOpenResult Open(string target);
}

public sealed class ShellService : IShellService
{
    public ShellOpenResult Open(string target)
    {
        if (!ShellTargetValidator.IsValid(target, out var error))
        {
            return new ShellOpenResult(false, error);
        }

        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return new ShellOpenResult(true, null);
        }
        catch (Exception exception)
        {
            return new ShellOpenResult(false, exception.Message);
        }
    }
}

public static class ShellTargetValidator
{
    public static bool IsValid(string target, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(target))
        {
            error = "The target is empty.";
            return false;
        }

        if (target.Contains('\0') || target.Any(char.IsControl))
        {
            error = "The target contains control characters.";
            return false;
        }

        if (IsAbsoluteFileSystemPath(target))
        {
            return true;
        }

        if (Uri.TryCreate(target, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme is "http" or "https" or "mailto")
            {
                return true;
            }

            error = $"The scheme '{uri.Scheme}' is not allowed.";
            return false;
        }

        error = "The target must be an absolute URL or an absolute file path.";
        return false;
    }

    private static bool IsAbsoluteFileSystemPath(string target) =>
        Path.IsPathRooted(target) &&
        (target.StartsWith(@"\\", StringComparison.Ordinal) ||
         (target.Length >= 2 && char.IsLetter(target[0]) && target[1] == ':'));
}
