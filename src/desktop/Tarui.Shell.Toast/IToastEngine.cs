namespace Tarui.Shell.Toast;

/// <summary>
/// Platform toast delivery backend behind <see cref="WindowsToastNotificationService"/>. Kept free
/// of WinRT types so the non-Windows asset of this assembly compiles it unchanged; the real engine
/// lives under <c>Windows/</c> and is only compiled into the windows flavor.
/// </summary>
internal interface IToastEngine : IDisposable
{
    /// <summary>
    /// Displays <paramref name="xml"/> as a toast attributed to <paramref name="id"/>. Throws when
    /// the OS refuses delivery; the owning service then degrades that show to its fallback backend.
    /// </summary>
    void Show(string id, string xml);

    /// <summary>Removes the toast previously shown under <paramref name="id"/>. Best-effort.</summary>
    void Hide(string id);
}
