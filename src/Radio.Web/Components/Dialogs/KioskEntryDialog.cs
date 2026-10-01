namespace Radio.Web.Components.Dialogs;

/// <summary>
/// UI-24: the class every Radzen <c>DialogService</c> dialog holding a text field passes as
/// <c>DialogOptions.CssClass</c>. design-system.css §20a anchors such a dialog at y = 24, stacked above
/// the fixed top bar, instead of centring it, clear of the in-app keyboard (js/virtual-keyboard.js).
/// That keyboard covers everything below y = 348 of the 720 px panel while it is up (QWERTY; the
/// numpad, below y = 360 — measured in Chromium at 1920×720) and does not resize the page.
/// Hand-built overlays get the same top anchoring from <c>.kiosk-entry-overlay</c>; where each kind
/// lands is in the §20a comment.
/// </summary>
public static class KioskEntryDialog
{
  /// <summary>The CSS class; one definition so a call site cannot misspell it.</summary>
  public const string CssClass = "kiosk-entry-dialog";
}
