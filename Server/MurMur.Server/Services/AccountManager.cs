using Microsoft.AspNetCore.Identity;
using MurMur.Server.Database;
using MurMur.Server.Models;

namespace MurMur.Server.Services;

public class AccountManager
{
    private readonly MurMurDbContext _db;
    private readonly PasswordHasher<User> _passwordHasher;

    public AccountManager(MurMurDbContext db)
    {
        _db = db;
        _passwordHasher = new PasswordHasher<User>();
    }

    public bool UsernameExists(string username)
    {
        return _db.Users.Any(user =>
            user.Username.ToLower() == username.ToLower());
    }

    public User CreateUser(string username, string temporaryPassword)
    {
        if (UsernameExists(username))
        {
            throw new InvalidOperationException(
                "That username already exists.");
        }

        var user = new User
        {
            Username = username,
            DisplayName = username,
            MustChangePassword = true,
            FirstStartup = true
        };

        user.PasswordHash =
            _passwordHasher.HashPassword(
                user,
                temporaryPassword);

        _db.Users.Add(user);
        _db.SaveChanges();

        return user;
    }
}