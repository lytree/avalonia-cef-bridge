namespace Tarui.Contracts;

/// <summary>
/// Request for <c>plugin:positioner|set-position</c>. <see cref="Anchor"/> names one of the supported
/// anchor slots (see <c>PositionerAnchors</c>); <see cref="Label"/> defaults to the calling window when
/// omitted and <see cref="X"/>/<see cref="Y"/> are margin offsets in physical pixels added to the
/// anchored position.
/// </summary>
public sealed record PositionerOptions(
    string Anchor,
    string? Label = null,
    int? X = null,
    int? Y = null);
