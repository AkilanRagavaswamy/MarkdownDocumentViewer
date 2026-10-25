using CommunityToolkit.Mvvm.ComponentModel;
using MarkdownViewer.Core.Contracts;

namespace MarkdownViewer.Core.ViewModels;

/// <summary>A swatch in the Settings accent picker.</summary>
public sealed partial class AccentOptionViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public AccentOptionViewModel(AccentColor accent)
    {
        Name = accent.Name;
        Hex = HexColor.TryNormalize(accent.Hex, out var n) ? n : accent.Hex;
        CheckGlyphColorHex = HexColor.ContrastingGlyph(Hex);
    }

    public string Name { get; }

    public string Hex { get; }

    /// <summary>"#FFFFFF" or "#000000" — whichever contrasts more with the swatch.</summary>
    public string CheckGlyphColorHex { get; }

    /// <summary>e.g. "Blue #546DE5".</summary>
    public string AutomationName => $"{Name} {Hex}";
}
