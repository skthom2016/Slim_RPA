namespace Tracto.Designer.App.Services;

/// <summary>
/// Abstracts modal confirmation dialogs so that ViewModels that need user
/// confirmation can be tested without a WPF message box.
/// </summary>
public interface IDialogService
{
    /// <summary>Returns true when the user clicked Yes/OK, false otherwise.</summary>
    bool Confirm(string message, string title);
}
