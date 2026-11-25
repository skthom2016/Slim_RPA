namespace Tracto.Designer.Unit.Tests.Settings;

using System.IO;
using FluentAssertions;
using Tracto.Designer.Infrastructure.Settings;

/// <summary>
/// The Import and Export dialogs open in the folder they were last used in, and in Downloads the
/// first time.
/// </summary>
public sealed class LastFolderStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "last-folder-" + Guid.NewGuid().ToString("N"));
    private readonly string _downloads;
    private readonly string _store;

    public LastFolderStoreTests()
    {
        _downloads = Directory.CreateDirectory(Path.Combine(_root, "Downloads")).FullName;
        _store = Path.Combine(_root, "settings", "last-folder.txt");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private LastFolderStore Store(string? fallback = "") =>
        new(_store, () => fallback == "" ? _downloads : fallback);

    private string Folder(string name) => Directory.CreateDirectory(Path.Combine(_root, name)).FullName;

    [Fact]
    public void Opens_In_Downloads_Before_Any_Folder_Has_Been_Used()
    {
        Store().Get().Should().Be(_downloads);
    }

    [Fact]
    public void Opens_In_The_Folder_Used_Last()
    {
        var store = Store();
        var work = Folder("my-scripts");

        store.RememberFolderOf(Path.Combine(work, "flow_1.0.0.json"));

        store.Get().Should().Be(work);
    }

    [Fact]
    public void The_Last_Folder_Survives_A_Restart_Of_The_Designer()
    {
        var work = Folder("kept");
        Store().Remember(work);

        var afterRestart = Store();   // a new object that reads the same file

        afterRestart.Get().Should().Be(work);
    }

    [Fact]
    public void The_Newest_Folder_Replaces_The_Older_One()
    {
        var store = Store();
        store.Remember(Folder("first"));
        var second = Folder("second");

        store.Remember(second);

        store.Get().Should().Be(second);
    }

    [Fact]
    public void Import_And_Export_Share_One_Remembered_Folder()
    {
        // Both dialogs use the same store object, so a folder chosen in one opens the other.
        var store = Store();
        var exported = Folder("exports");
        store.RememberFolderOf(Path.Combine(exported, "out.json"));
        store.Get().Should().Be(exported, "Import should now open where Export saved");
    }

    [Fact]
    public void A_Remembered_Folder_That_Was_Deleted_Falls_Back_To_Downloads()
    {
        var store = Store();
        var gone = Folder("temporary");
        store.Remember(gone);
        Directory.Delete(gone);

        store.Get().Should().Be(_downloads);
    }

    [Fact]
    public void Nothing_Is_Offered_When_Neither_The_Remembered_Folder_Nor_The_Fallback_Exists()
    {
        Store(fallback: Path.Combine(_root, "no-such-folder")).Get().Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_Names_Are_Ignored(string? value)
    {
        var store = Store();
        var work = Folder("good");
        store.Remember(work);

        store.Remember(value);
        store.RememberFolderOf(value);

        store.Get().Should().Be(work);
    }

    [Fact]
    public void A_Folder_That_Does_Not_Exist_Is_Not_Remembered()
    {
        var store = Store();
        store.Remember(Path.Combine(_root, "never-created"));
        store.Get().Should().Be(_downloads);
    }

    [Fact]
    public void A_Damaged_Settings_File_Is_Treated_As_Nothing_Remembered()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_store)!);
        File.WriteAllText(_store, "\0\0 not a folder ::: <>|");

        Store().Get().Should().Be(_downloads);
    }

    [Fact]
    public void A_Settings_File_That_Cannot_Be_Written_Never_Throws()
    {
        // The settings "folder" is really a file, so creating or writing under it fails.
        var blocker = Path.Combine(_root, "blocker");
        File.WriteAllText(blocker, "x");
        var store = new LastFolderStore(Path.Combine(blocker, "last-folder.txt"), () => _downloads);

        var act = () => store.Remember(Folder("anywhere"));

        act.Should().NotThrow();
        store.Get().Should().Be(_downloads);
    }

    [Fact]
    public void The_Default_Fallback_Is_The_Plain_Downloads_Folder_Not_A_Subfolder_Of_It()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        LastFolderStore.DefaultFallback().Should().Be(expected);
    }
}
