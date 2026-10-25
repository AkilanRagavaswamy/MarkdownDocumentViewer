using System;

namespace MarkdownViewer.Core.Contracts;

// ViewModel -> View messages, sent with WeakReferenceMessenger.Default.
// Views ignore messages whose TabId differs from their own DocumentTabViewModel.Id.

public sealed record ScrollToAnchorMessage(Guid TabId, string AnchorId);
public sealed record FindMessage(Guid TabId, string Query, bool Backwards);
public sealed record ClearFindMessage(Guid TabId);
public sealed record CollapseSectionsMessage(Guid TabId, bool Collapse);
public sealed record PrintMessage(Guid TabId);
public sealed record ExportPdfMessage(Guid TabId, string TargetPath);
public sealed record FocusFindBoxMessage(Guid TabId);

/// <summary>Broadcast whenever theme, accent, density, columns or wrap change. Views call mdv.setAppearance.</summary>
public sealed record AppearanceChangedMessage(ViewerAppearance Appearance);

/// <summary>Request the shell to show/hide the Settings page.</summary>
public sealed record SettingsVisibilityMessage(bool IsOpen);
