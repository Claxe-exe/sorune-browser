using System.Windows.Input;
using Claxe.Core;
using Claxe.Services;
using Microsoft.Web.WebView2.Core;

namespace Claxe.ViewModels;

/// <summary>One pending "site wants X" question shown in Claxe's own prompt.</summary>
public sealed class PermissionRequestViewModel : ObservableObject
{
    private readonly CoreWebView2PermissionRequestedEventArgs _args;
    private readonly CoreWebView2Deferral _deferral;
    private readonly Action<PermissionRequestViewModel, bool, bool> _onDecided;
    private bool _remember = true;
    private bool _decided;

    public PermissionRequestViewModel(
        TabViewModel tab,
        CoreWebView2PermissionRequestedEventArgs args,
        Action<PermissionRequestViewModel, bool, bool> onDecided)
    {
        Tab = tab;
        _args = args;
        _deferral = args.GetDeferral();
        _onDecided = onDecided;
        Origin = PermissionStore.OriginOf(args.Uri);
        Kind = args.PermissionKind;

        AllowCommand = new RelayCommand(() => Decide(true));
        DenyCommand = new RelayCommand(() => Decide(false));
    }

    public TabViewModel Tab { get; }
    public string Origin { get; }
    public CoreWebView2PermissionKind Kind { get; }

    public string Host => Uri.TryCreate(Origin, UriKind.Absolute, out var uri) ? uri.Host : Origin;
    public string What => PermissionStore.Describe(Kind);
    public string Glyph => PermissionStore.Glyph(Kind);

    public bool Remember
    {
        get => _remember;
        set => SetProperty(ref _remember, value);
    }

    public ICommand AllowCommand { get; }
    public ICommand DenyCommand { get; }

    /// <summary>Answers the page. Safe to call more than once (later calls are ignored).</summary>
    public void Decide(bool allow, bool remember)
    {
        if (_decided)
            return;

        _decided = true;
        try
        {
            _args.State = allow ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
            // Claxe keeps its own list (shown in settings), so WebView2 must not remember separately.
            _args.SavesInProfile = false;
            _deferral.Complete();
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            // The page went away while the prompt was open.
            Log.Info($"İzin isteği artık geçerli değil: {ex.Message}");
        }

        _onDecided(this, allow, remember);
    }

    private void Decide(bool allow) => Decide(allow, Remember);
}
