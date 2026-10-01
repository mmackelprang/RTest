namespace Radio.Web.Components.Dialogs;

/// <summary>
/// UI-24: the class every Radzen <c>DialogService</c> dialog holding a text field passes as
/// <c>DialogOptions.CssClass</c>. design-system.css §20a anchors such a dialog to the top of the
/// screen, clear of the in-app keyboard (js/virtual-keyboard.js), which covers the bottom 364 px of
/// the 720 px panel while it is up and does not resize the page. Hand-built overlays get the same
/// placement from <c>.kiosk-entry-overlay</c>.
/// </summary>
public static class KioskEntryDialog
{
  /// <summary>The CSS class; one definition so a call site cannot misspell it.</summary>
  public const string CssClass = "kiosk-entry-dialog";
}
