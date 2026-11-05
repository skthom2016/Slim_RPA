namespace Tracto.Designer.App.ViewModels;

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Tracto.Designer.App.Commands;
using Tracto.Designer.Core.Signing;

public sealed class SigningViewModel : INotifyPropertyChanged
{
    private readonly IScriptSigner _signer;
    private readonly ScriptEditorViewModel _editor;
    private readonly OutputLogViewModel? _output;
    private ScriptSignature? _signature;
    private bool? _verified;

    public SigningViewModel(IScriptSigner signer, ScriptEditorViewModel editor, OutputLogViewModel? output = null)
    {
        _signer = signer;
        _editor = editor;
        _output = output;
        SignCommand   = new RelayCommand(_ => Sign(),   _ => _editor.Script is not null && _signature is null);
        VerifyCommand = new RelayCommand(_ => Verify(), _ => _editor.Script is not null && _signature is not null);
    }

    public ICommand SignCommand   { get; }
    public ICommand VerifyCommand { get; }
    public string SignerName => _signer.SignerId;

    public ScriptSignature? CurrentSignature
    {
        get => _signature;
        private set
        {
            _signature = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSigned));
            RaiseAll();
        }
    }

    public bool? IsVerified
    {
        get => _verified;
        private set { _verified = value; OnPropertyChanged(); }
    }

    public bool IsSigned => _signature is not null;

    private void Sign()
    {
        if (_editor.Script is null) return;
        try
        {
            var signature = _signer.Sign(_editor.Script);
            CurrentSignature = signature;
            IsVerified = null;
            _output?.Success("Signing",
                $"Signed by {signature.SignerId} — {signature.Algorithm}, {signature.Timestamp:yyyy-MM-dd HH:mm:ss}, digest {Short(signature.Digest)}");
        }
        catch (InvalidOperationException ex)
        {
            CurrentSignature = null;
            IsVerified = null;
            _output?.Error("Signing", ex.Message);
            System.Windows.MessageBox.Show(ex.Message, "Signing Error");
        }
    }

    private void Verify()
    {
        if (_editor.Script is null || _signature is null) return;
        var verified = _signer.Verify(_editor.Script, _signature);
        IsVerified = verified;
        if (verified) _output?.Success("Signing", "Signature valid.");
        else          _output?.Error("Signing", "Signature invalid — the script changed after it was signed.");
    }

    private static string Short(string digest) => digest.Length > 16 ? digest[..16] + "..." : digest;

    private void RaiseAll()
    {
        ((RelayCommand)SignCommand).RaiseCanExecuteChanged();
        ((RelayCommand)VerifyCommand).RaiseCanExecuteChanged();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
