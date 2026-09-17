namespace MurMur.Server.Models;

public class SendFriendRequestRequest
{
    public string SenderUsername { get; set; } = string.Empty;

    public string ReceiverUsername { get; set; } = string.Empty;
}