using System.Formats.Tar;
using System.IO.Compression;

namespace Tarui.Cli;

/// <summary>
/// Minimal tar.gz writer that bundles a macOS <c>.app</c> directory into a gzipped tar archive.
/// System.Formats.Tar is net10 BCL; using it avoids an external tar/zip dependency and keeps the
/// Windows-first CLI portable. The on-disk archive preserves relative paths so a macOS host can
/// rehydrate with <c>tar -xzf</c> and end up with the original <c>*.app/Contents/...</c> tree.
/// </summary>
internal static class TarArchive
{
    public static void WriteGzip(string sourceDirectory, string destinationArchive)
    {
        using var fileStream = File.Create(destinationArchive);
        using var gzip = new GZipStream(fileStream, CompressionLevel.Optimal, leaveOpen: false);

        // includeBaseDirectory: false strips the parent directory so the bundle root sits at
        // the top of the archive, matching the on-disk layout after `tar -xzf`. macOS hosts can
        // therefore rehydrate with `tar -xzf *.app.tar.gz` and obtain *.app/Contents/... intact.
        TarFile.CreateFromDirectory(sourceDirectory, gzip, includeBaseDirectory: false);
    }
}