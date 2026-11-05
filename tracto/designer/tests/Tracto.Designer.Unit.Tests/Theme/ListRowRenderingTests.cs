namespace Tracto.Designer.Unit.Tests.Theme;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using FluentAssertions;

/// <summary>
/// The step list and the template list are ListViews with a plain ItemTemplate and no GridView.
/// A ListViewItem template that only presents GridView columns draws an empty row for them, so
/// recorded steps land in the editor but nothing appears on screen.
/// </summary>
public class ListRowRenderingTests
{
    [Fact]
    public void A_List_Row_With_No_Columns_Draws_Its_Item_Template()
    {
        var (height, text) = OnUiThread(() =>
        {
            var list = BuildThemedList("first step", "second step");
            var row = (ListViewItem)list.ItemContainerGenerator.ContainerFromIndex(0);
            return (row.ActualHeight, FindText(row));
        });

        height.Should().BeGreaterThan(0, "a row that renders nothing collapses to no height");
        text.Should().Be("first step", "the row has to present the item template's content");
    }

    [Fact]
    public void A_List_Row_With_Columns_Draws_Them()
    {
        var text = OnUiThread(() =>
        {
            var theme = FluentTheme();
            var list = new ListView
            {
                ItemsSource = new[] { "alpha", "beta" },
                ItemContainerStyle = (Style)theme["GridViewRow"],
                View = new GridView
                {
                    Columns = { new GridViewColumn { Header = "Value", DisplayMemberBinding = new System.Windows.Data.Binding(".") } }
                }
            };
            list.Resources.MergedDictionaries.Add(theme);
            Layout(list);
            var row = (ListViewItem)list.ItemContainerGenerator.ContainerFromIndex(0);
            return FindText(row);
        });

        text.Should().Be("alpha", "a column layout has to be presented by its columns");
    }

    private static ListView BuildThemedList(params string[] items)
    {
        var list = new ListView { ItemsSource = items };
        list.Resources.MergedDictionaries.Add(FluentTheme());
        list.ItemTemplate = (DataTemplate)XamlReader.Parse(
            """
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <TextBlock Text="{Binding}"/>
            </DataTemplate>
            """);
        Layout(list);
        return list;
    }

    private static ResourceDictionary FluentTheme() =>
        (ResourceDictionary)Application.LoadComponent(
            new Uri("/Tracto.Designer.App;component/Themes/Fluent.xaml", UriKind.Relative));

    private static void Layout(FrameworkElement element)
    {
        element.Measure(new Size(600, 400));
        element.Arrange(new Rect(0, 0, 600, 400));
        element.UpdateLayout();
        // Rows are generated and their templates expanded by queued dispatcher work; without
        // draining the queue the visual tree is still empty however often layout is forced.
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
            () => { }, System.Windows.Threading.DispatcherPriority.SystemIdle);
        element.UpdateLayout();
    }

    /// <summary>The first piece of text actually in the row's visual tree, or null if it is empty.</summary>
    private static string? FindText(DependencyObject root)
    {
        if (root is TextBlock { Text.Length: > 0 } t) return t.Text;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var found = FindText(VisualTreeHelper.GetChild(root, i));
            if (found is not null) return found;
        }
        return null;
    }

    /// <summary>WPF controls can only be built on an STA thread; xunit's is not one.</summary>
    private static T OnUiThread<T>(Func<T> work)
    {
        T result = default!;
        System.Runtime.ExceptionServices.ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try { result = work(); }
            catch (Exception ex) { error = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
        return result;
    }
}
