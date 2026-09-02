# UI Localization Contract

Coordinator owns `LocalizationService.cs`, JSON dictionaries, language selection/restart, and audit/test infrastructure. The following hooks are in parent-owned UI ranges and are needed for runtime coverage, not just dictionary coverage.

## Popup/Dialog Entry

In `MainWindow.DialogMotion.cs`, `ConfigureDialogEntrance(ContentDialog)` must localize the dialog before the `MotionEnabled()` early return. Repeat localization on `Opened` (or `Loaded`) to cover deferred content. Every `ContentDialog.ShowAsync()` call currently passes through this helper; the audit gate will enforce that. Do not depend on the main window's visual tree to find a popup.

## Additional Properties

`MainWindow.Localization.cs` `LocalizeElement` handles these logical properties before walking template children:

- `ContentDialog.Title`, `PrimaryButtonText`, `SecondaryButtonText`, `CloseButtonText`, plus logical title/content objects.
- `ComboBox.PlaceholderText`, string `Header`, string item labels (never `ComboChoice.Key`, IDs or `Tag`).
- Attached `ToolTipService.GetToolTip` string/object, `ToolTip.Content`, `MenuFlyoutItem.Text`, `MenuFlyoutSubItem.Text` and its items, `AppBarButton.Label`, and context flyout items.
- `Expander.Header` objects, `HeaderedContentControl` headers where used, and `RichTextBlock`/`TextBlock.Inlines` `Run.Text` when the containing subtree is not protected.
- Logical `Panel.Children` already covers `Grid`, even collapsed/lazy/unloaded content; retain it. Cover `Popup.Child` and `Flyout.Content`/menu items explicitly when available, without walking unrelated windows.

Property localization needs separate source-key/last-rendered-text storage per object AND per property. A single `ConditionalWeakTable<DependencyObject,string>` cannot hold a dialog title and three button keys. Re-localizing an unchanged translated value must use its original key; when dynamic code supplies new source text it must replace the saved source, not restore an old status/title. Preserve explicitly supplied `_localizationKeys` for existing button helpers.

Native QA additionally demonstrated that a managed-wrapper `ConditionalWeakTable` loses state when XAML retains the native object but its managed wrapper is replaced after collection. Persist localization state in a private attached dependency property on the native control, including inline `Run` state. Do not retain owners in a global dictionary. Register property callbacks once per native state entry, and protect `noloc` in callback paths as well as traversal.

## Construction and Lazy Views

`IconHeader`, `Body`, `SectionTitle`, `GroupTitle`, `RegisterButton` and tooltip helpers capture original source keys at construction, then localize. `ShowPage` localizes the visible page. This covers views constructed after the two startup `ApplyLanguage()` calls. Raw text captured after translation is too late to recover an unambiguous original key; in particular, the pre-settings-load OS/default language can differ from the saved language.

Do not translate external plugin README/changelog bodies, user-provided titles/descriptions, or original legal/license text. Existing `Tag = "noloc"` protection must remain effective for the whole subtree. Static brand names and technical values remain unchanged.

## Dynamic Text

`LocalizationService.Format(language, template, params object?[] arguments)` now translates a format template before formatting. Use it for newly touched interpolation call sites. `Translate` also has cached template matching for existing preformatted service/status text, including the variable CSS Loader version, and supplementary Italian translations are checked before Italian passthrough.

Do not feed an entire mixed user-content/README paragraph into formatted matching. Keep only known interface messages in `T`/`TranslateMessage`; interpolate names/paths as arguments. Repair progress/notes already call `T` and will gain coverage automatically.

## Review State

These hooks are parent-owned UI edits. The coordinator's source contracts cover their wiring. Native parent review validates headings, controls, popup/dialog content, tooltips, dynamic text, late-built views, and forced collection between locale changes. Dictionary coverage alone does not establish runtime rendering correctness.
