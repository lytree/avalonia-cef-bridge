using System.Runtime.InteropServices;

namespace Tarui.Cli;

/// <summary>Runtime identifier helpers for the current platform.</summary>
internal static class RuntimeIdentifier
{
    public static string ForCurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return RuntimeInformation.OSArchitecture switch
            {
                Architecture.Arm64 => "win-arm64",
                _ => "win-x64"
            };
        }

        if (OperatingSystem.IsMacOS())
        {
            return RuntimeInformation.OSArchitecture switch
            {
                Architecture.Arm64 => "osx-arm64",
                _ => "osx-x64"
            };
        }

        if (OperatingSystem.IsLinux())
        {
            return RuntimeInformation.OSArchitecture switch
            {
                Architecture.Arm64 => "linux-arm64",
                _ => "linux-x64"
            };
        }

        return "win-x64";
    }
}
