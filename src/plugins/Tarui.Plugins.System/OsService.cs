using System.Globalization;
using System.Runtime.InteropServices;
using Tarui.Contracts;

namespace Tarui.Plugins.System;

public interface IOsService
{
    OsInfo GetInfo();
}

public sealed class OsService : IOsService
{
    public OsInfo GetInfo()
    {
        var platform = OperatingSystem.IsWindows()
            ? "windows"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos" : "linux";
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x86_64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            Architecture.Arm => "arm",
            _ => "unknown",
        };
        var family = OperatingSystem.IsWindows() ? "windows" : "unix";

        return new OsInfo(
            platform,
            arch,
            Environment.OSVersion.Version.ToString(),
            family,
            CultureInfo.CurrentCulture.Name);
    }
}
