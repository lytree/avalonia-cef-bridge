using System.Security.Cryptography;
using System.Text;
using Tarui.Contracts;

namespace Tarui.Shell;

/// <summary>
/// Raised when an update manifest fails schema, shape or signature verification. Carries a
/// Web-facing, non-sensitive reason so callers can distinguish "tampered/misconfigured" from "no
/// update available".
/// </summary>
public sealed class UpdateVerificationException(string message) : Exception(message);

/// <summary>
/// Verifies an <see cref="UpdateManifest"/> against an injected public key. The manifest's
/// <see cref="UpdateManifest.Signature"/> is an ECDSA (P-384 / SHA-384) signature over the
/// deterministic canonical form of the signed fields; any mismatch — a bad schema version, a file
/// missing from the hash table, an unusable signature or a key mismatch — fails verification so a
/// tampered update is never treated as available.
/// </summary>
public sealed class UpdateVerifier : IDisposable
{
    private readonly ECDsa _publicKey;

    /// <summary>Creates a verifier from a base64 DER SubjectPublicKeyInfo (see <c>ECDsa.ExportSubjectPublicKeyInfo</c>).</summary>
    public UpdateVerifier(string publicKeyB64)
    {
        var spki = Convert.FromBase64String(publicKeyB64);
        _publicKey = ECDsa.Create();
        _publicKey.ImportSubjectPublicKeyInfo(spki, out _);
    }

    /// <summary>Verifies the manifest schema, shape and signature; throws <see cref="UpdateVerificationException"/> on failure.</summary>
    public void Verify(UpdateManifest manifest)
    {
        if (manifest.SchemaVersion != UpdateContracts.SchemaVersion)
        {
            throw new UpdateVerificationException($"unsupported-schema:{manifest.SchemaVersion}");
        }

        if (string.IsNullOrWhiteSpace(manifest.Version) ||
            manifest.Files is null or { Length: 0 } ||
            manifest.Sha256 is null or { Count: 0 } ||
            string.IsNullOrWhiteSpace(manifest.Signature))
        {
            throw new UpdateVerificationException("malformed-manifest");
        }

        foreach (var file in manifest.Files)
        {
            if (!manifest.Sha256.TryGetValue(file, out var hash) ||
                string.IsNullOrWhiteSpace(hash))
            {
                throw new UpdateVerificationException($"missing-hash:{file}");
            }
        }

        var canonical = Canonicalize(manifest);
        var signature = Convert.FromBase64String(manifest.Signature);
        if (!_publicKey.VerifyData(canonical, signature, HashAlgorithmName.SHA384))
        {
            throw new UpdateVerificationException("invalid-signature");
        }
    }

    /// <summary>
    /// Produces the deterministic byte stream a release producer signs and this verifier checks: the
    /// schema version, version, the files in declared order, and the hash table sorted by path. Both
    /// producer and consumer must agree on this exact ordering.
    /// </summary>
    internal static byte[] Canonicalize(UpdateManifest manifest)
    {
        var builder = new StringBuilder();
        builder.Append(manifest.SchemaVersion).Append('\n');
        builder.Append(manifest.Version).Append('\n');
        foreach (var file in manifest.Files)
        {
            builder.Append(file).Append('\n');
        }
        foreach (var pair in manifest.Sha256.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            builder.Append(pair.Key).Append('=').Append(pair.Value).Append('\n');
        }
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    public void Dispose() => _publicKey.Dispose();
}