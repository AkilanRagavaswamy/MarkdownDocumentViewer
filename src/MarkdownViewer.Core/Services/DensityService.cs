using MarkdownViewer.Core.Contracts;

namespace MarkdownViewer.Core.Services;

/// <summary>Reading metrics for each density preset (values from the approved design).</summary>
public sealed class DensityService : IDensityService
{
    /// <summary>Viewport width (CSS px) at which Auto columns switches to two columns.</summary>
    public const double TwoColumnMinWidth = 1400;

    private static readonly DensityMetrics Comfortable = new(
        FontSize: 16, LineHeight: 1.6, Gap: 12, HeadingGap: 24,
        H1: 30, H2: 22, H3: 17, CodeSize: 14,
        PadVertical: 32, PadHorizontal: 48,
        MaxColumns: 1, TwoColumnMinWidth: TwoColumnMinWidth);

    private static readonly DensityMetrics Compact = new(
        FontSize: 14, LineHeight: 1.42, Gap: 7, HeadingGap: 14,
        H1: 21, H2: 16.5, H3: 14.5, CodeSize: 12.5,
        PadVertical: 18, PadHorizontal: 28,
        MaxColumns: 2, TwoColumnMinWidth: TwoColumnMinWidth);

    private static readonly DensityMetrics Ultra = new(
        FontSize: 12.5, LineHeight: 1.32, Gap: 5, HeadingGap: 10,
        H1: 18, H2: 14.5, H3: 13, CodeSize: 11.5,
        PadVertical: 12, PadHorizontal: 20,
        MaxColumns: 2, TwoColumnMinWidth: TwoColumnMinWidth);

    /// <inheritdoc />
    public DensityMetrics Get(DensityPreset preset) => preset switch
    {
        DensityPreset.Comfortable => Comfortable,
        DensityPreset.Ultra => Ultra,
        _ => Compact,
    };
}
