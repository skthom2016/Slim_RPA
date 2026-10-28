namespace Tracto.Designer.App.Services;

using System.Windows;

/// <summary>
/// Production implementation of IDialogService that delegates to System.Windows.MessageBox.
/// </summary>
public sealed class MessageBoxDialogService : IDialogService
{
    public bool Confirm(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo) == MessageBoxResult.Yes;
}
