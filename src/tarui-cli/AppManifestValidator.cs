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
                if (target is not ("zip" or "msix" or "app-bundle"))
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

        ValidateMsix(manifest, errors);
        ValidateMacOs(manifest, errors);
        ValidateFileAssociations(manifest, errors);

        return errors;
    }

    private static void ValidateFileAssociations(AppManifest manifest, List<string> errors)
    {
        var associations = manifest.Bundle.FileAssociations;
        if (associations.Count == 0)
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < associations.Count; index++)
        {
            var association = associations[index];
            var prefix = $"bundle.fileAssociations[{index}]";

            if (string.IsNullOrWhiteSpace(association.Name))
            {
                errors.Add($"{prefix}.name is required.");
            }

            if (string.IsNullOrWhiteSpace(association.Ext) ||
                !System.Text.RegularExpressions.Regex.IsMatch(association.Ext, "^\\.[A-Za-z0-9][A-Za-z0-9._-]*$"))
            {
                errors.Add(
                    $"{prefix}.ext must match '^\\.[A-Za-z0-9][A-Za-z0-9._-]*$', got '{association.Ext}'.");
            }
            else
            {
                if (association.Ext.Length > 64)
                {
                    errors.Add(
                        $"{prefix}.ext must be at most 64 characters, got '{association.Ext}'.");
                }

                if (!seen.Add(association.Ext))
                {
                    errors.Add(
                        $"{prefix}.ext duplicates an earlier file association '{association.Ext}'.");
                }
            }

            if (association.Description is not null && association.Description.Length > 512)
            {
                errors.Add($"{prefix}.description must be at most 512 characters.");
            }

            if (!string.IsNullOrWhiteSpace(association.MimeType) &&
                !System.Text.RegularExpressions.Regex.IsMatch(
                    association.MimeType,
                    "^[a-zA-Z0-9][a-zA-Z0-9!#$&^_.+-]*/[a-zA-Z0-9][a-zA-Z0-9!#$&^_.+-]*$"))
            {
                errors.Add($"{prefix}.mimeType must be a type/subtype pair, got '{association.MimeType}'.");
            }

            if (association.Role is not null &&
                association.Role is not ("Editor" or "Viewer" or "Shell" or "None"))
            {
                errors.Add(
                    $"{prefix}.role must be one of Editor, Viewer, Shell or None, got '{association.Role}'.");
            }
        }
    }

    private static void ValidateMsix(AppManifest manifest, List<string> errors)
    {
        var msix = manifest.Bundle.Msix;
        if (msix is null)
        {
            return;
        }

        var hasMsixTarget = manifest.Bundle.Targets.Contains("msix");
        if (!hasMsixTarget)
        {
            errors.Add("bundle.msix is configured but bundle.targets does not include 'msix'.");
        }

        if (!string.IsNullOrWhiteSpace(msix.CertificatePath) && !File.Exists(msix.CertificatePath))
        {
            errors.Add($"bundle.msix.certificate.path not found: {msix.CertificatePath}");
        }
    }

    private static void ValidateMacOs(AppManifest manifest, List<string> errors)
    {
        var macOs = manifest.Bundle.MacOs;
        if (macOs is null)
        {
            return;
        }

        var hasMacOsTarget = manifest.Bundle.Targets.Contains("app-bundle");
        if (!hasMacOsTarget)
        {
            errors.Add("bundle.macOS is configured but bundle.targets does not include 'app-bundle'.");
        }

        if (!string.IsNullOrWhiteSpace(macOs.BundleId) &&
            !System.Text.RegularExpressions.Regex.IsMatch(macOs.BundleId, "^[A-Za-z0-9\\-]+(\\.[A-Za-z0-9\\-]+)+$"))
        {
            errors.Add($"bundle.macOS.bundleId must be a reverse-DNS identifier, got '{macOs.BundleId}'.");
        }

        if (!string.IsNullOrWhiteSpace(macOs.ExecutableName) &&
            (macOs.ExecutableName.Contains(' ') || macOs.ExecutableName.EndsWith(".app", StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add($"bundle.macOS.executableName must not contain spaces or end with .app, got '{macOs.ExecutableName}'.");
        }

        if (!string.IsNullOrWhiteSpace(macOs.MinimumSystemVersion) &&
            !System.Text.RegularExpressions.Regex.IsMatch(macOs.MinimumSystemVersion, "^\\d+(\\.\\d+){0,2}$"))
        {
            errors.Add($"bundle.macOS.minimumSystemVersion must be a dotted version, got '{macOs.MinimumSystemVersion}'.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var scheme in macOs.Schemes)
        {
            if (!IsValidSchemeToken(scheme))
            {
                errors.Add($"bundle.macOS.schemes contains invalid token '{scheme}'.");
                continue;
            }

            if (!seen.Add(scheme))
            {
                errors.Add($"bundle.macOS.schemes contains duplicate '{scheme}'.");
            }
        }
    }

    /// <summary>
    /// RFC 3986 scheme grammar: <c>ALPHA *( ALPHA / DIGIT / "+" / "-" / "." )</c>. Shared with the
    /// runtime <c>DeepLinkUri.IsValidScheme</c> rule so the CLI can refuse tokens that the host
    /// would reject anyway.
    /// </summary>
    private static bool IsValidSchemeToken(string scheme)
    {
        if (string.IsNullOrEmpty(scheme))
        {
            return false;
        }

        if (!char.IsAsciiLetter(scheme[0]))
        {
            return false;
        }

        foreach (var c in scheme)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '+' or '-' or '.'))
            {
                return false;
            }
        }

        return true;
    }
}
