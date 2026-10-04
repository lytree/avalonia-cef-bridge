using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Tarui.Shell;

/// <summary>
/// Registers the configured file associations with the operating system when the host starts,
/// mirroring <see cref="DeepLinkRegistrarHostedService"/>. On Windows this writes per-user
/// <c>HKCU\Software\Classes</c> entries via <see cref="WindowsFileAssociationRegistrar"/>
/// (the portable zip distribution has no installer to do it); Linux and macOS treat file
/// associations as packaging concerns (desktop MIME entries / <c>CFBundleDocumentTypes</c>),
/// so registration is a no-op here. Registration is idempotent and runs only for the configured
/// association set.
/// </summary>
public sealed class FileAssociationRegistrarHostedService(IConfiguration configuration) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        WindowsFileAssociationRegistrar.Register(FileAssociationConfiguration.ReadAssociations(configuration));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Reads the configured file association set from <c>Tarui:FileAssociations</c> (an array of
/// objects with <c>Ext</c> / <c>Name</c> / <c>Description</c>, e.g.
/// <c>Tarui:FileAssociations:0:Ext</c>). Entries without an extension or a name are skipped and
/// duplicate extensions (case-insensitive) collapse to the first entry, mirroring the tolerant
/// <see cref="DeepLinkConfiguration"/> reading style.
/// </summary>
internal static class FileAssociationConfiguration
{
    public static IReadOnlyCollection<FileAssociationSpec> ReadAssociations(IConfiguration? configuration)
    {
        if (configuration is null)
        {
            return [];
        }

        var section = configuration.GetSection("Tarui:FileAssociations");
        var associations = new List<FileAssociationSpec>();
        foreach (var child in section.GetChildren())
        {
            var ext = child["Ext"];
            var name = child["Name"];
            var description = child["Description"];
            if (string.IsNullOrWhiteSpace(ext) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var trimmedExt = ext.Trim();
            if (!trimmedExt.StartsWith('.'))
            {
                trimmedExt = $".{trimmedExt}";
            }

            if (associations.Any(association =>
                    string.Equals(association.Ext, trimmedExt, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            associations.Add(new FileAssociationSpec(
                trimmedExt,
                name.Trim(),
                string.IsNullOrWhiteSpace(description) ? null : description.Trim()));
        }

        return associations;
    }
}
