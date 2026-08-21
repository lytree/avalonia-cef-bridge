namespace Tarui.Contracts;

/// <summary>
/// Options for <c>plugin:autostart|enable</c>. Only the current application may be registered for
/// autostart -- never an arbitrary executable. <see cref="Args"/> are pre-configured fixed arguments
/// forwarded on launch; they are validated and quoted by the shell, not taken as a bare command line.
/// </summary>
public sealed record AutostartEnableOptions(string[]? Args = null);

/// <summary>Result of <c>plugin:autostart|is-enabled</c>.</summary>
public sealed record AutostartState(bool Enabled);