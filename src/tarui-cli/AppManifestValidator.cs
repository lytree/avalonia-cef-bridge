namespace Tarui.Cli;

/// <summary>Semantic validation of a loaded manifest against the repository layout.</summary>
internal static class AppManifestValidator
{
    public static IReadOnlyList<string> Validate(AppManifest manifest, string manifestDirectory)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(manifest.Product.Name))
        {
            errors.Add("product.name is required.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Product.Version) ||
            !Version.TryParse(manifest.Product.Version, out _))
        {
            errors.Add("product.version must be a semantic version, e.g. 0.1.0.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Product.Identifier))
        {
            errors.Add("product.identifier is required.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Build.FrontendDist))
        {
            errors.Add("build.frontendDist is required.");
        }

        if (!string.IsNullOrWhiteSpace(manifest.Build.DevUrl) &&
            (!Uri.TryCreate(manifest.Build.DevUrl, UriKind.Absolute, out var devUri) ||
             devUri.Scheme is not ("http" or "https")))
        {
            errors.Add($"build.devUrl must be an absolute http(s) URL, got '{manifest.Build.DevUrl}'.");
        }

        if (manifest.Bundle.Targets.Count == 0)
        {
            errors.Add("bundle.targets must not be empty.");
        }
        else
        {
            foreach (var target in manifest.Bundle.Targets)
            {
                if (target is not ("zip" or "msix"))
                {
                    errors.Add($"bundle.targets contains unsupported target '{target}'.");
                }
            }
        }

        if (manifest.App is not null)
        {
            var capabilitiesDirectory = Path.Combine(manifestDirectory, "capabilities");
            foreach (var id in manifest.App.Capabilities)
            {
                var file = Path.Combine(capabilitiesDirectory, $"{id}.json");
                if (!File.Exists(file))
                {
                    errors.Add($"app.capabilities references '{id}' but capabilities/{id}.json was not found.");
                }
            }
        }

        return errors;
    }
}
