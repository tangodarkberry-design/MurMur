namespace MurMur.Server.Models;

public class Friendship
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public int FriendUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}