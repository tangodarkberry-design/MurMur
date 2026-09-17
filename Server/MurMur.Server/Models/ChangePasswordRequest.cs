namespace MurMur.Server.Models;

public class ChangePasswordRequest
{
    public string Username { get; set; } = string.Empty;

    public string NewPassword { get; set; } = string.Empty;
}