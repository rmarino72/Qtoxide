using CommunityToolkit.Mvvm.Input;

namespace Qtoxide.ViewModels;

public sealed partial class FriendRequestViewModel : ViewModelBase
{
    private readonly Action<FriendRequestViewModel, bool> _answer;

    public FriendRequestViewModel(string publicKey, string message, Action<FriendRequestViewModel, bool> answer)
    {
        PublicKey = publicKey;
        Message = message;
        _answer = answer;
    }

    public string PublicKey { get; }
    public string ShortKey => PublicKey[..16] + "…";
    public string Message { get; }

    [RelayCommand] private void Accept() => _answer(this, true);
    [RelayCommand] private void Reject() => _answer(this, false);
}
