namespace Tracto.Designer.App;

using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using Tracto.Designer.App.ViewModels;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<RecordingViewModel>();
    }
}
