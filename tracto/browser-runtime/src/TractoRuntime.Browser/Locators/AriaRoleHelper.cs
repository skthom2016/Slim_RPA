using Microsoft.Playwright;

namespace TractoRuntime.Browser.Locators;

/// <summary>
/// Shared conversion helper for ARIA role strings → Playwright <see cref="AriaRole"/> enum.
/// Used by <see cref="FrameChainResolver"/> and <see cref="ShadowDomLocatorService"/> so the
/// mapping is maintained in exactly one place.
/// </summary>
internal static class AriaRoleHelper
{
    /// <summary>
    /// Converts a WAI-ARIA role string (case-insensitive) to the corresponding
    /// Playwright <see cref="AriaRole"/> value.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="role"/> is not a recognised standard WAI-ARIA role.
    /// </exception>
    internal static AriaRole FromString(string role) => role.ToLowerInvariant() switch
    {
        "alert"            => AriaRole.Alert,
        "alertdialog"      => AriaRole.Alertdialog,
        "application"      => AriaRole.Application,
        "article"          => AriaRole.Article,
        "banner"           => AriaRole.Banner,
        "blockquote"       => AriaRole.Blockquote,
        "button"           => AriaRole.Button,
        "caption"          => AriaRole.Caption,
        "cell"             => AriaRole.Cell,
        "checkbox"         => AriaRole.Checkbox,
        "code"             => AriaRole.Code,
        "columnheader"     => AriaRole.Columnheader,
        "combobox"         => AriaRole.Combobox,
        "complementary"    => AriaRole.Complementary,
        "contentinfo"      => AriaRole.Contentinfo,
        "definition"       => AriaRole.Definition,
        "deletion"         => AriaRole.Deletion,
        "dialog"           => AriaRole.Dialog,
        "directory"        => AriaRole.Directory,
        "document"         => AriaRole.Document,
        "emphasis"         => AriaRole.Emphasis,
        "feed"             => AriaRole.Feed,
        "figure"           => AriaRole.Figure,
        "form"             => AriaRole.Form,
        "generic"          => AriaRole.Generic,
        "grid"             => AriaRole.Grid,
        "gridcell"         => AriaRole.Gridcell,
        "group"            => AriaRole.Group,
        "heading"          => AriaRole.Heading,
        "img"              => AriaRole.Img,
        "insertion"        => AriaRole.Insertion,
        "link"             => AriaRole.Link,
        "list"             => AriaRole.List,
        "listbox"          => AriaRole.Listbox,
        "listitem"         => AriaRole.Listitem,
        "log"              => AriaRole.Log,
        "main"             => AriaRole.Main,
        "marquee"          => AriaRole.Marquee,
        "math"             => AriaRole.Math,
        "meter"            => AriaRole.Meter,
        "menu"             => AriaRole.Menu,
        "menubar"          => AriaRole.Menubar,
        "menuitem"         => AriaRole.Menuitem,
        "menuitemcheckbox" => AriaRole.Menuitemcheckbox,
        "menuitemradio"    => AriaRole.Menuitemradio,
        "navigation"       => AriaRole.Navigation,
        "none"             => AriaRole.None,
        "note"             => AriaRole.Note,
        "option"           => AriaRole.Option,
        "paragraph"        => AriaRole.Paragraph,
        "presentation"     => AriaRole.Presentation,
        "progressbar"      => AriaRole.Progressbar,
        "radio"            => AriaRole.Radio,
        "radiogroup"       => AriaRole.Radiogroup,
        "region"           => AriaRole.Region,
        "row"              => AriaRole.Row,
        "rowgroup"         => AriaRole.Rowgroup,
        "rowheader"        => AriaRole.Rowheader,
        "scrollbar"        => AriaRole.Scrollbar,
        "search"           => AriaRole.Search,
        "searchbox"        => AriaRole.Searchbox,
        "separator"        => AriaRole.Separator,
        "slider"           => AriaRole.Slider,
        "spinbutton"       => AriaRole.Spinbutton,
        "status"           => AriaRole.Status,
        "strong"           => AriaRole.Strong,
        "subscript"        => AriaRole.Subscript,
        "superscript"      => AriaRole.Superscript,
        "switch"           => AriaRole.Switch,
        "tab"              => AriaRole.Tab,
        "table"            => AriaRole.Table,
        "tablist"          => AriaRole.Tablist,
        "tabpanel"         => AriaRole.Tabpanel,
        "term"             => AriaRole.Term,
        "textbox"          => AriaRole.Textbox,
        "time"             => AriaRole.Time,
        "timer"            => AriaRole.Timer,
        "toolbar"          => AriaRole.Toolbar,
        "tooltip"          => AriaRole.Tooltip,
        "tree"             => AriaRole.Tree,
        "treegrid"         => AriaRole.Treegrid,
        "treeitem"         => AriaRole.Treeitem,
        _ => throw new ArgumentException(
                 $"Unrecognised ARIA role '{role}'. Use a standard WAI-ARIA role string.",
                 nameof(role))
    };
}
