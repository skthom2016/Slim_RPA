using System.Windows.Automation;

namespace Tracto.Desktop.Element;

/// <summary>
/// Implements <see cref="IElementFinder"/> using Windows UI Automation via <see cref="IUiaProvider"/>.
/// </summary>
public sealed class UiaElementFinder : IElementFinder
{
    private readonly IUiaProvider _provider;

    public UiaElementFinder(IUiaProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    /// <inheritdoc/>
    public async Task<ElementRef> FindAsync(ElementSelector sel, ElementFindOptions opts, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(opts.TimeoutMs);
        var delay = Math.Max(50, Math.Min(250, opts.TimeoutMs / 20));

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var result = TryResolve(sel);
            if (result is not null)
                return result;

            if (DateTime.UtcNow >= deadline)
                throw new DesktopActionError("TARGET_NOT_FOUND",
                    $"No element matched selector (AutomationId={sel.AutomationId}, Name={sel.Name}, ClassName={sel.ClassName}, ControlType={sel.ControlType})");

            await Task.Delay(delay, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<ElementRef> ReResolveAsync(ElementRef stale, ElementFindOptions opts, CancellationToken ct)
    {
        // Never use the cached RuntimeId — always run the selector from scratch.
        return await FindAsync(stale.Selector, opts, ct).ConfigureAwait(false);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Attempts to resolve <paramref name="sel"/> once. Returns null when not found yet;
    /// throws <see cref="DesktopActionError"/> on ambiguity.
    /// </summary>
    private ElementRef? TryResolve(ElementSelector sel)
    {
        // Resolve ancestry chain first (outermost ancestor → immediate parent).
        IUiaProvider scopedProvider = _provider;

        if (sel.Parent is not null)
        {
            var ancestorRef = TryResolveAncestor(sel.Parent);
            if (ancestorRef is null)
                return null;

            // Wrap the ancestor AutomationElement into a provider so descendants are searched within it.
            scopedProvider = new SubtreeUiaProvider(ancestorRef.AutomationElement);
        }

        var cond = BuildCondition(sel);
        var all = scopedProvider.FindAll(TreeScope.Descendants, cond);

        if (all.Length == 0)
            return null;

        if (all.Length == 1)
            return new ElementRef(all[0], sel);

        // Multiple matches — attempt IndexInParent disambiguation.
        if (sel.IndexInParent.HasValue)
        {
            var idx = sel.IndexInParent.Value;
            if (idx >= 0 && idx < all.Length)
                return new ElementRef(all[idx], sel);

            throw new DesktopActionError("TARGET_NOT_FOUND",
                $"IndexInParent={idx} is out of range ({all.Length} matches)");
        }

        throw new DesktopActionError("TARGET_AMBIGUOUS",
            $"{all.Length} elements matched selector (AutomationId={sel.AutomationId}, Name={sel.Name})");
    }

    private ElementRef? TryResolveAncestor(ElementSelector ancestor)
    {
        // Recursive: resolve the ancestor's own parent first, then the ancestor itself.
        IUiaProvider scopedProvider = _provider;

        if (ancestor.Parent is not null)
        {
            var grandparentRef = TryResolveAncestor(ancestor.Parent);
            if (grandparentRef is null)
                return null;
            scopedProvider = new SubtreeUiaProvider(grandparentRef.AutomationElement);
        }

        var cond = BuildCondition(ancestor);
        var first = scopedProvider.FindFirst(TreeScope.Descendants, cond);
        if (first is null)
            return null;

        return new ElementRef(first, ancestor);
    }

    /// <summary>
    /// Builds a UIA <see cref="Condition"/> from the non-null selector fields.
    /// Each non-null field becomes a <see cref="PropertyCondition"/>; multiple fields are combined with <see cref="AndCondition"/>.
    /// </summary>
    private static Condition BuildCondition(ElementSelector sel)
    {
        var conditions = new List<Condition>();

        if (sel.AutomationId is not null)
            conditions.Add(new PropertyCondition(AutomationElement.AutomationIdProperty, sel.AutomationId));

        if (sel.Name is not null)
            conditions.Add(new PropertyCondition(AutomationElement.NameProperty, sel.Name));

        if (sel.ClassName is not null)
            conditions.Add(new PropertyCondition(AutomationElement.ClassNameProperty, sel.ClassName));

        if (sel.ControlType is not null)
        {
            var ct = ResolveControlType(sel.ControlType);
            if (ct is not null)
                conditions.Add(new PropertyCondition(AutomationElement.ControlTypeProperty, ct));
        }

        return conditions.Count switch
        {
            0 => Condition.TrueCondition,
            1 => conditions[0],
            _ => new AndCondition(conditions.ToArray()),
        };
    }

    private static ControlType? ResolveControlType(string name)
    {
        // Map common control type names to ControlType instances.
        return name.ToUpperInvariant() switch
        {
            "BUTTON" => ControlType.Button,
            "CHECKBOX" => ControlType.CheckBox,
            "COMBOBOX" => ControlType.ComboBox,
            "EDIT" => ControlType.Edit,
            "HYPERLINK" => ControlType.Hyperlink,
            "IMAGE" => ControlType.Image,
            "LISTITEM" => ControlType.ListItem,
            "LIST" => ControlType.List,
            "MENU" => ControlType.Menu,
            "MENUBAR" => ControlType.MenuBar,
            "MENUITEM" => ControlType.MenuItem,
            "PROGRESSBAR" => ControlType.ProgressBar,
            "RADIOBUTTON" => ControlType.RadioButton,
            "SCROLLBAR" => ControlType.ScrollBar,
            "SLIDER" => ControlType.Slider,
            "SPINNER" => ControlType.Spinner,
            "STATUSBAR" => ControlType.StatusBar,
            "TAB" => ControlType.Tab,
            "TABITEM" => ControlType.TabItem,
            "TEXT" => ControlType.Text,
            "TOOLBAR" => ControlType.ToolBar,
            "TOOLTIP" => ControlType.ToolTip,
            "TREE" => ControlType.Tree,
            "TREEITEM" => ControlType.TreeItem,
            "CUSTOM" => ControlType.Custom,
            "GROUP" => ControlType.Group,
            "THUMB" => ControlType.Thumb,
            "DATAGRID" => ControlType.DataGrid,
            "DATAITEM" => ControlType.DataItem,
            "DOCUMENT" => ControlType.Document,
            "SPLITBUTTON" => ControlType.SplitButton,
            "WINDOW" => ControlType.Window,
            "PANE" => ControlType.Pane,
            "HEADER" => ControlType.Header,
            "HEADERITEM" => ControlType.HeaderItem,
            "TABLE" => ControlType.Table,
            "TITLEBAR" => ControlType.TitleBar,
            "SEPARATOR" => ControlType.Separator,
            _ => null,
        };
    }

    // -------------------------------------------------------------------------
    // Helper: wrap a found AutomationElement as a scoped IUiaProvider
    // -------------------------------------------------------------------------

    private sealed class SubtreeUiaProvider : IUiaProvider
    {
        private readonly AutomationElement _root;

        public SubtreeUiaProvider(AutomationElement root)
        {
            _root = root;
        }

        public AutomationElement? FindFirst(TreeScope scope, Condition cond)
        {
            return _root.FindFirst(scope, cond);
        }

        public AutomationElement[] FindAll(TreeScope scope, Condition cond)
        {
            var col = _root.FindAll(scope, cond);
            var result = new AutomationElement[col.Count];
            for (int i = 0; i < col.Count; i++)
                result[i] = col[i];
            return result;
        }
    }
}
