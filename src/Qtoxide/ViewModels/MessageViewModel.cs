using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Qtoxide.Models;
using Qtoxide.Services;

namespace Qtoxide.ViewModels;

/// <summary>A chat entry: text, action ("/me") or file transfer.</summary>
public sealed partial class MessageViewModel : ViewModelBase
{
    private readonly Action<MessageViewModel, string>? _transferAction;

    public MessageViewModel(StoredMessage model, string author, Action<MessageViewModel, string>? transferAction = null)
    {
        Model = model;
        Author = author;
        _transferAction = transferAction;
        _delivered = model.Delivered;
        _state = model.TransferState;
        _progress = model.TransferState == TransferState.Completed ? 100 : 0;
    }

    public StoredMessage Model { get; }
    public string Author { get; }
    public string Text => Model.Text;
    public bool Outgoing => Model.Outgoing;
    public bool IsText => Model.Kind == MessageKind.Text;
    public bool IsAction => Model.Kind == MessageKind.Action;
    public bool IsFile => Model.Kind == MessageKind.File;
    public string TimeText => Model.Time.LocalDateTime.ToString(Model.Time.Date == DateTimeOffset.Now.Date ? "HH:mm" : "dd/MM HH:mm");
    public string ActionText => $"* {Author} {Text}";
    public string FileName => Model.FileName ?? "";
    public string SizeText => FormatSize(Model.FileSize);

    /// <summary>The live transfer behind a file message, while it lasts.</summary>
    public Transfer? Transfer { get; set; }

    [ObservableProperty] private bool _delivered;
    [ObservableProperty] private double _progress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText), nameof(CanAccept), nameof(CanCancel), nameof(CanPause), nameof(CanOpen),
        nameof(IsInProgress), nameof(PauseText))]
    private TransferState _state;

    public string StateText => State switch
    {
        TransferState.Pending => "Incoming file",
        TransferState.WaitingForFriend => "Waiting for the friend to accept",
        TransferState.Transferring => $"{Progress:F0}%",
        TransferState.Paused => "Paused",
        TransferState.Completed => Outgoing ? "Sent" : "Received",
        _ => "Cancelled",
    };

    public bool CanAccept => !Outgoing && State == TransferState.Pending && Transfer is not null;
    public bool CanCancel => Transfer is not null && State is TransferState.Pending or TransferState.WaitingForFriend
                                                              or TransferState.Transferring or TransferState.Paused;
    public bool CanPause => Transfer is not null && State is TransferState.Transferring or TransferState.Paused;
    public bool CanOpen => State == TransferState.Completed && Model.FilePath is not null && File.Exists(Model.FilePath);
    public bool IsInProgress => State is TransferState.Transferring or TransferState.Paused;
    public string PauseText => State == TransferState.Paused ? "Resume" : "Pause";

    partial void OnProgressChanged(double value) => OnPropertyChanged(nameof(StateText));

    /// <summary>Copies a transfer's live state into the message (UI thread).</summary>
    public void Update(Transfer transfer)
    {
        Transfer = transfer.State is TransferState.Completed or TransferState.Cancelled ? null : transfer;
        Model.TransferState = transfer.State;
        Model.FilePath = transfer.Path;
        Progress = transfer.Size > 0 ? 100.0 * transfer.Transferred / transfer.Size : transfer.State == TransferState.Completed ? 100 : 0;
        State = transfer.State;
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(CanAccept));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanOpen));
    }

    [RelayCommand] private void Accept() => _transferAction?.Invoke(this, "accept");
    [RelayCommand] private void Cancel() => _transferAction?.Invoke(this, "cancel");
    [RelayCommand] private void Pause() => _transferAction?.Invoke(this, "pause");
    [RelayCommand] private void Open() => _transferAction?.Invoke(this, "open");

    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB",
    };
}
