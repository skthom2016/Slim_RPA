using Microsoft.Playwright;
using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Locators;

public interface ISelectorStrategy
{
    Task<ILocator> ResolveAsync(IPage page, SelectorRef selectorRef, CancellationToken ct);
}
