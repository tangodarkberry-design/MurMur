namespace MurMur.Server.Models;

public class Message
{
    public int Id { get; set; }

    public int SenderUserId { get; set; }

    public int ReceiverUserId { get; set; }

    public string Content { get; set; } = "";

    public DateTime SentAt { get; set; }

    public bool IsRead { get; set; } = false;
}