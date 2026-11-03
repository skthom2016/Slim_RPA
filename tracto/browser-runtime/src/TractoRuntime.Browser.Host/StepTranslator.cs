using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Host;

/// <summary>A flat step converted to what the runtime executes.</summary>
public sealed record TranslatedStep(
    string StepId,
    string Action,
    BrowserStep? Browser,
    int? DelayMs,
    string? StoreAs);

/// <summary>
/// Converts flat Designer steps (string parameters) into the runtime's typed
/// <see cref="BrowserStep"/>. Every structural problem is reported as a
/// <see cref="ScriptValidationException"/> so the whole script can be rejected before a browser opens.
/// </summary>
public static class StepTranslator
{
    public const string DelayAction = "delay";
    private const int MaxDelayMs = 60_000;

    /// <summary>Runtime action names, advertised to the Orchestrator at registration.</summary>
    public static readonly IReadOnlyList<string> RuntimeActionNames =
    [
        "browser.page.navigate",
        "browser.element.wait",
        "browser.element.find",
        "browser.element.fill",
        "browser.element.type",
        "browser.element.select",
        "browser.element.check",
        "browser.element.uncheck",
        "browser.element.click",
        "browser.element.dblclick",
        "browser.element.hover",
        "browser.element.scroll",
        "browser.keyboard.shortcut",
        "browser.page.assert",
        "browser.element.getText",
        "browser.element.getAttr",
        "browser.element.getValue",
        DelayAction
    ];

    // Short names accepted in scripts, mapped to the canonical runtime action.
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["navigate"]         = "browser.page.navigate",
        ["goto"]             = "browser.page.navigate",
        ["browser.navigate"] = "browser.page.navigate",
        ["wait"]             = "browser.element.wait",
        ["browser.wait"]     = "browser.element.wait",
        ["find"]             = "browser.element.find",
        ["browser.find"]     = "browser.element.find",
        ["fill"]             = "browser.element.fill",
        ["browser.fill"]     = "browser.element.fill",
        ["type"]             = "browser.element.type",
        ["browser.type"]     = "browser.element.type",
        ["select"]           = "browser.element.select",
        ["browser.select"]   = "browser.element.select",
        ["check"]            = "browser.element.check",
        ["browser.check"]    = "browser.element.check",
        ["uncheck"]          = "browser.element.uncheck",
        ["browser.uncheck"]  = "browser.element.uncheck",
        ["click"]            = "browser.element.click",
        ["browser.click"]    = "browser.element.click",
        ["dblclick"]         = "browser.element.dblclick",
        ["browser.dblclick"] = "browser.element.dblclick",
        ["hover"]            = "browser.element.hover",
        ["browser.hover"]    = "browser.element.hover",
        ["scroll"]           = "browser.element.scroll",
        ["browser.scroll"]   = "browser.element.scroll",
        ["press"]            = "browser.keyboard.shortcut",
        ["key"]              = "browser.keyboard.shortcut",
        ["browser.press"]    = "browser.keyboard.shortcut",
        ["browser.key"]      = "browser.keyboard.shortcut",
        ["assert"]           = "browser.page.assert",
        ["browser.assert"]   = "browser.page.assert",
        ["getText"]          = "browser.element.getText",
        ["browser.getText"]  = "browser.element.getText",
        ["getAttr"]          = "browser.element.getAttr",
        ["browser.getAttr"]  = "browser.element.getAttr",
        ["getValue"]         = "browser.element.getValue",
        ["browser.getValue"] = "browser.element.getValue",
        ["sleep"]            = DelayAction,
        ["browser.delay"]    = DelayAction
    };

    private static readonly HashSet<string> SelectorRequired = new(StringComparer.Ordinal)
    {
        "browser.element.wait", "browser.element.find", "browser.element.fill",
        "browser.element.type", "browser.element.select", "browser.element.check",
        "browser.element.uncheck", "browser.element.click", "browser.element.dblclick",
        "browser.element.hover", "browser.element.scroll", "browser.element.getText",
        "browser.element.getAttr", "browser.element.getValue"
    };

    public static string Canonical(string action)
    {
        if (Aliases.TryGetValue(action, out var canon)) return canon;
        return RuntimeActionNames.FirstOrDefault(a => a.Equals(action, StringComparison.OrdinalIgnoreCase))
               ?? action;
    }

    public static TranslatedStep Translate(
        string stepId,
        string rawAction,
        IReadOnlyDictionary<string, string?> p,
        bool allowCss)
    {
        if (string.IsNullOrWhiteSpace(rawAction))
            throw new ScriptValidationException($"{stepId}: action is missing.");

        var action = Canonical(rawAction);

        if (action.StartsWith("desktop.", StringComparison.OrdinalIgnoreCase))
            throw new ScriptValidationException(
                $"{stepId}: '{rawAction}' is a desktop action; browser scripts cannot contain desktop steps.");

        if (!RuntimeActionNames.Contains(action, StringComparer.Ordinal))
            throw new ScriptValidationException($"{stepId}: unknown action '{rawAction}'.");

        var storeAs = Get(p, "storeAs");

        if (action == DelayAction)
        {
            var msText = Get(p, "ms") ?? throw new ScriptValidationException($"{stepId}: delay needs 'ms'.");
            if (!int.TryParse(msText, out var ms) || ms < 0)
                throw new ScriptValidationException($"{stepId}: delay 'ms' must be a non-negative integer.");
            return new TranslatedStep(stepId, action, null, Math.Min(ms, MaxDelayMs), storeAs);
        }

        var selector = BuildSelector(stepId, p, allowCss);
        if (selector is null && SelectorRequired.Contains(action))
            throw new ScriptValidationException(
                $"{stepId}: {action} needs a target. Give one of role (+name), label, placeholder, testId, text, css, xpath.");

        TimeSpan? timeout = null;
        if (Get(p, "timeoutMs") is { } t)
        {
            if (!int.TryParse(t, out var tms) || tms <= 0)
                throw new ScriptValidationException($"{stepId}: timeoutMs must be a positive integer.");
            timeout = TimeSpan.FromMilliseconds(tms);
        }

        string? value = null;
        AssertionSpec? assertion = null;
        ExtractionSpec? extraction = null;

        switch (action)
        {
            case "browser.page.navigate":
                value = Get(p, "url") ?? throw new ScriptValidationException($"{stepId}: navigate needs 'url'.");
                if (!value.Contains("${", StringComparison.Ordinal) &&
                    !Uri.TryCreate(value, UriKind.Absolute, out _))
                    throw new ScriptValidationException($"{stepId}: 'url' must be an absolute URL (https://...).");
                break;

            case "browser.element.fill":
            case "browser.element.type":
                value = Get(p, "value") ?? throw new ScriptValidationException($"{stepId}: {action} needs 'value'.");
                break;

            case "browser.element.select":
                value = Get(p, "value") ?? throw new ScriptValidationException($"{stepId}: select needs 'value' (the option value).");
                break;

            case "browser.keyboard.shortcut":
                value = Get(p, "key") ?? throw new ScriptValidationException($"{stepId}: press needs 'key' (e.g. Enter, Control+a).");
                break;

            case "browser.element.getText":
                extraction = new ExtractionSpec { Mode = ExtractionMode.DisplayedText, OutputKey = "value" };
                break;

            case "browser.element.getValue":
                extraction = new ExtractionSpec { Mode = ExtractionMode.FormValue, OutputKey = "value" };
                break;

            case "browser.element.getAttr":
                var attr = Get(p, "attribute") ?? throw new ScriptValidationException($"{stepId}: getAttr needs 'attribute'.");
                extraction = new ExtractionSpec { Mode = ExtractionMode.Attribute, AttributeName = attr, OutputKey = "value" };
                break;

            case "browser.page.assert":
                assertion = BuildAssertion(stepId, p, selector);
                break;
        }

        var step = new BrowserStep
        {
            StepId     = stepId,
            Action     = action,
            Selector   = selector,
            Value      = value,
            Timeout    = timeout,
            Assertion  = assertion,
            Extraction = extraction
        };

        return new TranslatedStep(stepId, action, step, null, storeAs);
    }

    private static SelectorRef? BuildSelector(string stepId, IReadOnlyDictionary<string, string?> p, bool allowCss)
    {
        var role        = Get(p, "role");
        var roleName    = Get(p, "name");
        var label       = Get(p, "label");
        var placeholder = Get(p, "placeholder");
        var testId      = Get(p, "testId");
        var text        = Get(p, "text");
        var css         = Get(p, "css");
        var xpath       = Get(p, "xpath");

        if (role is null && label is null && placeholder is null && testId is null &&
            text is null && css is null && xpath is null)
            return null;

        if (roleName is not null && role is null)
            throw new ScriptValidationException($"{stepId}: 'name' only applies together with 'role'.");

        if (css is not null && !allowCss)
            throw new ScriptValidationException($"{stepId}: css selectors are not allowed by the host policy (AllowCss=false).");

        return new SelectorRef
        {
            Role        = role,
            RoleName    = roleName,
            Label       = label,
            Placeholder = placeholder,
            TestId      = testId,
            Text        = text,
            Css         = css,
            XPath       = xpath
        };
    }

    private static AssertionSpec BuildAssertion(string stepId, IReadOnlyDictionary<string, string?> p, SelectorRef? selector)
    {
        var kindText = Get(p, "kind") ?? throw new ScriptValidationException(
            $"{stepId}: assert needs 'kind' (visible, enabled, checked, urlEquals, textEquals).");

        var kind = kindText.ToLowerInvariant() switch
        {
            "visible"              => AssertionKind.LocatorVisible,
            "enabled"              => AssertionKind.LocatorEnabled,
            "checked"              => AssertionKind.LocatorChecked,
            "urlequals" or "url"   => AssertionKind.UrlEquals,
            "textequals" or "text" => AssertionKind.TextEquals,
            _ => throw new ScriptValidationException($"{stepId}: unknown assert kind '{kindText}'.")
        };

        var expected = Get(p, "expected");

        if (kind is AssertionKind.UrlEquals or AssertionKind.TextEquals && expected is null)
            throw new ScriptValidationException($"{stepId}: assert kind '{kindText}' needs 'expected'.");

        if (kind is not AssertionKind.UrlEquals && selector is null)
            throw new ScriptValidationException($"{stepId}: assert kind '{kindText}' needs a target element.");

        return new AssertionSpec { Kind = kind, ExpectedValue = expected };
    }

    private static string? Get(IReadOnlyDictionary<string, string?> p, string key) =>
        p.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v) ? v : null;
}
