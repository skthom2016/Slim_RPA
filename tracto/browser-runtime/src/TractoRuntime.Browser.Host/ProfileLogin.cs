using Microsoft.Playwright;

namespace TractoRuntime.Browser.Host;

/// <summary>
/// The one-time sign-in step for a named profile: opens a visible browser on that profile and
/// waits while a person signs in, then saves and closes. Later runs that name the same profile
/// start already signed in.
///
/// <para>Run it with <c>dotnet run -- --login &lt;profile&gt; [url]</c>.</para>
/// </summary>
public static class ProfileLogin
{
    public static async Task<int> RunAsync(BrowserHostOptions options, string profileName, string? url)
    {
        if (!BrowserProfile.IsValidName(profileName))
        {
            Console.Error.WriteLine(
                $"'{profileName}' is not a valid profile name. Use letters, digits, dash, underscore or dot.");
            return 2;
        }

        var directory = BrowserProfile.ResolveDirectory(options.ProfileRoot, profileName);
        Directory.CreateDirectory(directory);

        Console.WriteLine($"Profile : {profileName}");
        Console.WriteLine($"Folder  : {directory}");
        Console.WriteLine($"Browser : {(string.IsNullOrEmpty(options.Channel) ? "bundled Chromium" : options.Channel)}");
        Console.WriteLine();
        Console.WriteLine("A browser window is opening. Sign in to the sites this profile should stay signed into,");
        Console.WriteLine("then close the browser window. What you signed into is kept for later runs.");
        Console.WriteLine();

        using var playwright = await Playwright.CreateAsync();

        IBrowserContext context;
        try
        {
            context = await playwright.Chromium.LaunchPersistentContextAsync(directory,
                new BrowserTypeLaunchPersistentContextOptions
                {
                    Headless          = false,   // a person has to see it to sign in
                    Channel           = string.IsNullOrWhiteSpace(options.Channel) ? null : options.Channel,
                    IgnoreHTTPSErrors = false,
                    ViewportSize      = new ViewportSize { Width = options.ViewportWidth, Height = options.ViewportHeight }
                }).ConfigureAwait(false);
        }
        catch (PlaywrightException ex)
        {
            Console.Error.WriteLine(
                $"Could not open profile '{profileName}'. It is probably already open in another window. " +
                $"Close it and try again.");
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        var page = context.Pages.Count > 0 ? context.Pages[0] : await context.NewPageAsync().ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(url))
        {
            try
            {
                await page.GotoAsync(url).ConfigureAwait(false);
            }
            catch (PlaywrightException ex)
            {
                Console.Error.WriteLine($"Could not open {url}: {ex.Message}");
            }
        }

        // Wait for the person to finish and close the window.
        var closed = new TaskCompletionSource();
        context.Close += (_, _) => closed.TrySetResult();
        await closed.Task.ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine($"Saved. Scripts can now use \"profile\": \"{profileName}\" on a browser segment.");
        return 0;
    }
}
