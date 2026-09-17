namespace MurMur.Server.Models;

public class User
{
    public int Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public bool MustChangePassword { get; set; }

    public bool FirstStartup { get; set; }

    public string? ProfilePicture { get; set; }
}