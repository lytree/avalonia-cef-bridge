namespace Tarui.Contracts;

/// <summary>
/// The current launch deep-link URL, i.e. the registered-custom-protocol URL that started the
/// running primary instance. <see cref="Url"/> is <see langword="null"/> when the instance was not
/// activated through a registered scheme (e.g. it was launched normally or the URL was invalid).
/// </summary>
public sealed record DeepLinkCurrentResult(string? Url);

/// <summary>
/// Simulation hook used by the example app to demonstrate the deep-link pipeline (received /
/// rejected / not-applicable states) without performing a real OS protocol activation. The URL is
/// validated exactly like a real cold- or warm-start URL. Production shells can omit this command.
/// </summary>
public sealed record DeepLinkFeedOptions(string? Url);