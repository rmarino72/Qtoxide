namespace Qtoxide.Models;

public enum MessageKind
{
    Text,
    Action,
    File,
}

public enum TransferState
{
    /// <summary>Incoming file waiting for the user to accept it.</summary>
    Pending,

    /// <summary>Outgoing file waiting for the friend to accept it.</summary>
    WaitingForFriend,
    Transferring,
    Paused,
    Completed,
    Cancelled,
}

/// <summary>One entry of a chat history, as persisted.</summary>
public sealed class StoredMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public MessageKind Kind { get; set; }
    public bool Outgoing { get; set; }
    public string Text { get; set; } = "";
    public DateTimeOffset Time { get; set; } = DateTimeOffset.Now;

    /// <summary>Outgoing text: the friend confirmed receiving it.</summary>
    public bool Delivered { get; set; }

    // File messages
    public string? FileName { get; set; }
    public long FileSize { get; set; }
    public string? FilePath { get; set; }
    public TransferState TransferState { get; set; }
}
