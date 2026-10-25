using MarkdownViewer.Core.Contracts;
using Windows.System;

namespace MarkdownViewer.App.Helpers;

/// <summary>
/// Typed constants for x:Bind (OneTime). Enum values passed as CommandParameter must be real enum
/// instances (RelayCommand&lt;T&gt; rejects strings), and OEM keys have no named VirtualKey member.
/// </summary>
public static class XamlValues
{
    public static DensityPreset Comfortable => DensityPreset.Comfortable;

    public static DensityPreset Compact => DensityPreset.Compact;

    public static DensityPreset Ultra => DensityPreset.Ultra;

    public static ColumnMode ColumnsAuto => ColumnMode.Auto;

    public static ColumnMode ColumnsOne => ColumnMode.One;

    public static ColumnMode ColumnsTwo => ColumnMode.Two;

    /// <summary>VK_OEM_PLUS ("=" / "+" key).</summary>
    public static VirtualKey OemPlus => (VirtualKey)187;

    /// <summary>VK_OEM_COMMA.</summary>
    public static VirtualKey OemComma => (VirtualKey)188;

    /// <summary>VK_OEM_MINUS.</summary>
    public static VirtualKey OemMinus => (VirtualKey)189;

    /// <summary>VK_OEM_2 ("/" key on US layouts).</summary>
    public static VirtualKey OemSlash => (VirtualKey)191;

    /// <summary>VK_OEM_4 ("[").</summary>
    public static VirtualKey OemOpenBracket => (VirtualKey)219;

    /// <summary>VK_OEM_6 ("]").</summary>
    public static VirtualKey OemCloseBracket => (VirtualKey)221;
}
