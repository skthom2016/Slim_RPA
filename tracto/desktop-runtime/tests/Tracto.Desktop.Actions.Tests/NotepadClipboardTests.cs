using System.Reflection;
using System.Windows.Forms;
using FluentAssertions;
using Tracto.Desktop.Actions.Handlers;
using Xunit;

namespace Tracto.Desktop.Actions.Tests;

public class NotepadClipboardTests
{
    // The handler runs on a thread-pool (MTA) thread, where Clipboard.SetText throws.
    // The text must still reach the clipboard.
    [Fact]
    public async Task Text_reaches_the_clipboard_when_called_from_a_thread_pool_thread()
    {
        var method = typeof(NotepadTypeHandler).GetMethod("SetClipboardTextAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        var text = "tracto-clipboard-test " + Guid.NewGuid();

        var ok = await Task.Run(() => (Task<bool>)method.Invoke(null, [text])!);

        ok.Should().BeTrue();
        string? read = null;
        var t = new Thread(() => read = Clipboard.GetText());
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        read.Should().Be(text);
    }
}
