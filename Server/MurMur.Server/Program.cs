using Microsoft.EntityFrameworkCore;
using MurMur.Server.Database;
using MurMur.Server.Services;
using Microsoft.AspNetCore.Identity;
using MurMur.Server.Models;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.SignalR;
using MurMur.Server.Hubs;

if (args.Length > 0 &&
    args[0].Equals("create-user", StringComparison.OrdinalIgnoreCase))
{
    var options = new DbContextOptionsBuilder<MurMurDbContext>()
        .UseSqlite("Data Source=Database/murmur.db")
        .Options;

    using var db = new MurMurDbContext(options);

    var accountManager = new AccountManager(db);

    Console.WriteLine();
    Console.WriteLine("=== MurMur Account Creator ===");
    Console.WriteLine();

    Console.Write("Username: ");
    var username = Console.ReadLine()?.Trim();

    if (string.IsNullOrWhiteSpace(username))
    {
        Console.WriteLine("Username cannot be empty.");
        return;
    }

    Console.Write("Temporary password: ");
    var password = ReadPassword();

    Console.WriteLine();
    Console.Write("Confirm temporary password: ");
    var confirmPassword = ReadPassword();

    Console.WriteLine();

    if (password != confirmPassword)
    {
        Console.WriteLine("Passwords do not match.");
        return;
    }

    if (password.Length < 8)
    {
        Console.WriteLine("Password must be at least 8 characters.");
        return;
    }

    try
    {
        accountManager.CreateUser(username, password);

        Console.WriteLine();
        Console.WriteLine($"Account '{username}' created successfully.");
        Console.WriteLine("The user will be required to change their password on first login.");
    }
    catch (InvalidOperationException ex)
    {
        Console.WriteLine();
        Console.WriteLine(ex.Message);
    }

    return;
}


if (args.Length >= 3 &&
    args[0].Equals("set-first-startup", StringComparison.OrdinalIgnoreCase))
{
    var username = args[1];

    if (!bool.TryParse(args[2], out var firstStartup))
    {
        Console.WriteLine(
            "Usage: dotnet run -- set-first-startup <username> <true|false>");

        return;
    }

    var options = new DbContextOptionsBuilder<MurMurDbContext>()
        .UseSqlite("Data Source=Database/murmur.db")
        .Options;

    using var db = new MurMurDbContext(options);

    var user = db.Users.FirstOrDefault(u =>
        u.Username.ToLower() == username.ToLower());

    if (user == null)
    {
        Console.WriteLine(
            $"User '{username}' was not found.");

        return;
    }

    user.FirstStartup = firstStartup;

    db.SaveChanges();

    Console.WriteLine(
        $"FirstStartup for '{user.Username}' set to {firstStartup}.");

    return;
}


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<MurMurDbContext>(options =>
{
    options.UseSqlite("Data Source=Database/murmur.db");
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("MurMurClient", policy =>
    {
        policy
            .WithOrigins("http://localhost:5500")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddSignalR();

var app = builder.Build();

var uploadsPath =
    Path.Combine(
        app.Environment.ContentRootPath,
        "uploads"
    );

Directory.CreateDirectory(
    uploadsPath
);

app.UseStaticFiles(
    new StaticFileOptions
    {
        FileProvider =
            new PhysicalFileProvider(
                uploadsPath
            ),

        RequestPath =
            "/uploads"
    }
);

app.UseStaticFiles();

app.UseCors("MurMurClient");

app.MapGet("/api/status", () =>
{
    return Results.Ok(new
    {
        status = "online"
    });
});

app.MapHub<PssPssHub>("/psspssHub");


/* =========================
   Login
   ========================= */

app.MapPost("/api/login", async (
    LoginRequest request,
    MurMurDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(request.Username) ||
        string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "Username and password are required."
        });
    }

    var user = await db.Users
        .FirstOrDefaultAsync(u =>
            u.Username.ToLower() == request.Username.ToLower());

    if (user == null)
    {
        return Results.Json(
            new
            {
                success = false,
                message = "Invalid username or password."
            },
            statusCode: 401);
    }

    var passwordHasher = new PasswordHasher<User>();

    var result = passwordHasher.VerifyHashedPassword(
        user,
        user.PasswordHash,
        request.Password);

    if (result == PasswordVerificationResult.Failed)
    {
        return Results.Json(
            new
            {
                success = false,
                message = "Invalid username or password."
            },
            statusCode: 401);
    }

    return Results.Ok(new
    {
        success = true,
        username = user.Username,
        displayName = user.DisplayName,
        mustChangePassword = user.MustChangePassword,
        firstStartup = user.FirstStartup
    });
});

app.MapGet("/api/users/search", async (
    string username,
    string? currentUsername,
    MurMurDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(username))
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "Username is required."
        });
    }

    var user = await db.Users
        .FirstOrDefaultAsync(u =>
            u.Username.ToLower() ==
            username.Trim().ToLower());

    if (user == null)
    {
        return Results.NotFound(new
        {
            success = false,
            message = "No user found with that username."
        });
    }

    var alreadyFriends = false;
    var friendRequestPending = false;

    if (!string.IsNullOrWhiteSpace(currentUsername))
    {
        var currentUser = await db.Users
            .FirstOrDefaultAsync(u =>
                u.Username.ToLower() ==
                currentUsername.Trim().ToLower());

        if (currentUser != null)
        {
            // Check if already friends
            alreadyFriends = await db.Friendships
                .AnyAsync(f =>
                    (f.UserId == currentUser.Id &&
                     f.FriendUserId == user.Id)
                    ||
                    (f.UserId == user.Id &&
                     f.FriendUserId == currentUser.Id));

            // Check if YOU have already sent them a pending request
            friendRequestPending = await db.FriendRequests
                .AnyAsync(r =>
                    r.SenderUserId == currentUser.Id &&
                    r.ReceiverUserId == user.Id &&
                    r.Status == "Pending");
        }
    }

    return Results.Ok(new
    {
        success = true,
        username = user.Username,
        displayName = user.DisplayName,

        profilePicture =
            string.IsNullOrWhiteSpace(user.ProfilePicture)
                ? null
                : $"/uploads/profile-pictures/{user.ProfilePicture}",

        alreadyFriends,
        friendRequestPending
    });
});

app.MapGet("/api/users/profile", async (
    string username,
    MurMurDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(username))
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "Username is required."
        });
    }

    var user = await db.Users
        .FirstOrDefaultAsync(u =>
            u.Username.ToLower() ==
            username.Trim().ToLower());

    if (user == null)
    {
        return Results.NotFound(new
        {
            success = false,
            message = "User not found."
        });
    }

    return Results.Ok(new
    {
        success = true,
        username = user.Username,
        displayName = user.DisplayName,
        profilePicture =
            string.IsNullOrWhiteSpace(user.ProfilePicture)
                ? null
                : $"/uploads/profile-pictures/{user.ProfilePicture}"
    });
});

app.MapPost("/api/friends/request", async (
    SendFriendRequestRequest request,
    MurMurDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(request.SenderUsername) ||
        string.IsNullOrWhiteSpace(request.ReceiverUsername))
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "Sender and receiver usernames are required."
        });
    }

    var sender = await db.Users
        .FirstOrDefaultAsync(u =>
            u.Username.ToLower() == request.SenderUsername.ToLower());

    if (sender == null)
    {
        return Results.NotFound(new
        {
            success = false,
            message = "Sender account not found."
        });
    }

    var receiver = await db.Users
        .FirstOrDefaultAsync(u =>
            u.Username.ToLower() == request.ReceiverUsername.ToLower());

    if (receiver == null)
    {
        return Results.NotFound(new
        {
            success = false,
            message = "That user does not exist."
        });
    }

    if (sender.Id == receiver.Id)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "You cannot send a friend request to yourself."
        });
    }

    var existingRequest = await db.FriendRequests
        .FirstOrDefaultAsync(r =>
            r.SenderUserId == sender.Id &&
            r.ReceiverUserId == receiver.Id &&
            r.Status == "Pending");

    if (existingRequest != null)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "A friend request is already pending."
        });
    }

    var friendRequest = new FriendRequest
    {
        SenderUserId = sender.Id,
        ReceiverUserId = receiver.Id,
        Status = "Pending",
        CreatedAt = DateTime.UtcNow
    };

    db.FriendRequests.Add(friendRequest);
    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        success = true,
        message = "Friend request sent."
    });
});

app.MapGet("/api/friends/requests", async (
    string username,
    MurMurDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(username))
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "Username is required."
        });
    }

    var user = await db.Users
        .FirstOrDefaultAsync(u =>
            u.Username.ToLower() == username.Trim().ToLower());

    if (user == null)
    {
        return Results.NotFound(new
        {
            success = false,
            message = "User not found."
        });
    }

    var requests = await db.FriendRequests
        .Where(r =>
            r.ReceiverUserId == user.Id &&
            r.Status == "Pending")
        .Join(
            db.Users,
            request => request.SenderUserId,
            sender => sender.Id,
            (request, sender) => new
            {
                id = request.Id,
                username = sender.Username,
                displayName = sender.DisplayName,

                profilePicture =
                    string.IsNullOrWhiteSpace(sender.ProfilePicture)
                        ? null
                        : $"/uploads/profile-pictures/{sender.ProfilePicture}",

                createdAt = request.CreatedAt
            })
        .ToListAsync();

    return Results.Ok(new
    {
        success = true,
        requests
    });
});

app.MapPost("/api/messages", async (
    SendMessageRequest request,
    MurMurDbContext db,
    IHubContext<PssPssHub> hubContext) =>
{
    if (string.IsNullOrWhiteSpace(request.SenderUsername) ||
        string.IsNullOrWhiteSpace(request.ReceiverUsername))
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "Sender and receiver usernames are required."
        });
    }

    if (string.IsNullOrWhiteSpace(request.Content))
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "Message cannot be empty."
        });
    }

    var sender = await db.Users
        .FirstOrDefaultAsync(u =>
            u.Username.ToLower() ==
            request.SenderUsername.Trim().ToLower());

    if (sender == null)
    {
        return Results.NotFound(new
        {
            success = false,
            message = "Sender account not found."
        });
    }

    var receiver = await db.Users
        .FirstOrDefaultAsync(u =>
            u.Username.ToLower() ==
            request.ReceiverUsername.Trim().ToLower());

    if (receiver == null)
    {
        return Results.NotFound(new
        {
            success = false,
            message = "Receiver account not found."
        });
    }

    if (sender.Id == receiver.Id)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "You cannot message yourself."
        });
    }

    // Make sure the two users are actually friends.
    var areFriends = await db.Friendships
        .AnyAsync(f =>
            (f.UserId == sender.Id &&
             f.FriendUserId == receiver.Id)
            ||
            (f.UserId == receiver.Id &&
             f.FriendUserId == sender.Id));

    if (!areFriends)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "You can only message your friends."
        });
    }

    var message = new Message
    {
        SenderUserId = sender.Id,
        ReceiverUserId = receiver.Id,
        Content = request.Content.Trim(),
        SentAt = DateTime.UtcNow,
        IsRead = false
    };

    db.Messages.Add(message);

    await db.SaveChangesAsync();

    // Tell connected MurMur clients that a new message exists.
    await hubContext.Clients
        .All
        .SendAsync(
            "ReceivePssPssMessage",
            new
            {
                id = message.Id,

                senderUsername =
                    sender.Username,

                receiverUsername =
                    receiver.Username,

                content =
                    message.Content,

                sentAt =
                    message.SentAt,

                isRead =
                    message.IsRead
            }
        );

    return Results.Ok(new
    {
        success = true,

        message = new
        {
            id = message.Id,

            senderUsername =
                sender.Username,

            receiverUsername =
                receiver.Username,

            content =
                message.Content,

            sentAt =
                message.SentAt,

            isRead =
                message.IsRead
        }
    });
});

app.MapGet("/api/messages/conversation", async (
    string username,
    string with,
    MurMurDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(username) ||
        string.IsNullOrWhiteSpace(with))
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "Both usernames are required."
        });
    }

    var currentUser = await db.Users
        .FirstOrDefaultAsync(u =>
            u.Username.ToLower() ==
            username.Trim().ToLower());

    if (currentUser == null)
    {
        return Results.NotFound(new
        {
            success = false,
            message = "Your account was not found."
        });
    }

    var otherUser = await db.Users
        .FirstOrDefaultAsync(u =>
            u.Username.ToLower() ==
            with.Trim().ToLower());

    if (otherUser == null)
    {
        return Results.NotFound(new
        {
            success = false,
            message = "That user was not found."
        });
    }

    // Make sure they are friends.
    var areFriends = await db.Friendships
        .AnyAsync(f =>
            (f.UserId == currentUser.Id &&
             f.FriendUserId == otherUser.Id)
            ||
            (f.UserId == otherUser.Id &&
             f.FriendUserId == currentUser.Id));

    if (!areFriends)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "You can only view conversations with friends."
        });
    }

    var messages = await db.Messages
        .Where(m =>
            (m.SenderUserId == currentUser.Id &&
             m.ReceiverUserId == otherUser.Id)
            ||
            (m.SenderUserId == otherUser.Id &&
             m.ReceiverUserId == currentUser.Id))
        .OrderBy(m => m.SentAt)
        .Select(m => new
        {
            id = m.Id,
            senderUsername =
                m.SenderUserId == currentUser.Id
                    ? currentUser.Username
                    : otherUser.Username,

            receiverUsername =
                m.ReceiverUserId == currentUser.Id
                    ? currentUser.Username
                    : otherUser.Username,

            content = m.Content,
            sentAt = m.SentAt,
            isRead = m.IsRead
        })
        .ToListAsync();

    return Results.Ok(new
    {
        success = true,
        messages
    });
});

app.MapGet("/api/messages/conversations", async (
    string username,
    MurMurDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(username))
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "Username is required."
        });
    }

    var currentUser = await db.Users
        .FirstOrDefaultAsync(u =>
            u.Username.ToLower() ==
            username.Trim().ToLower());

    if (currentUser == null)
    {
        return Results.NotFound(new
        {
            success = false,
            message = "Your account was not found."
        });
    }

    // Get every message involving the current user.
    var messages = await db.Messages
        .Where(m =>
            m.SenderUserId == currentUser.Id ||
            m.ReceiverUserId == currentUser.Id)
        .OrderByDescending(m => m.SentAt)
        .ToListAsync();

    // Get the other user from each conversation.
    var conversationUserIds = messages
        .Select(m =>
            m.SenderUserId == currentUser.Id
                ? m.ReceiverUserId
                : m.SenderUserId)
        .Distinct()
        .ToList();

    var users = await db.Users
        .Where(u => conversationUserIds.Contains(u.Id))
        .ToListAsync();

    var conversations = messages
        .GroupBy(m =>
            m.SenderUserId == currentUser.Id
                ? m.ReceiverUserId
                : m.SenderUserId)
        .Select(group =>
        {
            var otherUser = users
                .FirstOrDefault(u => u.Id == group.Key);

            var latestMessage = group
                .OrderByDescending(m => m.SentAt)
                .First();

            if (otherUser == null)
            {
                return null;
            }

            return new
            {
                username = otherUser.Username,
                displayName = otherUser.DisplayName,

                profilePicture =
                    string.IsNullOrWhiteSpace(
                        otherUser.ProfilePicture)
                        ? null
                        : $"/uploads/profile-pictures/{otherUser.ProfilePicture}",

                lastMessage = latestMessage.Content,
                lastMessageAt = latestMessage.SentAt,

                unreadCount = group.Count(m =>
                    m.ReceiverUserId == currentUser.Id &&
                    !m.IsRead)
            };
        })
        .Where(c => c != null)
        .OrderByDescending(c => c!.lastMessageAt)
        .ToList();

    return Results.Ok(new
    {
        success = true,
        conversations
    });
});

app.MapPost("/api/friends/respond", async (
    int requestId,
    string username,
    string action,
    MurMurDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(username) ||
        string.IsNullOrWhiteSpace(action))
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "Username and action are required."
        });
    }

    var user = await db.Users
        .FirstOrDefaultAsync(u =>
            u.Username.ToLower() == username.Trim().ToLower());

    if (user == null)
    {
        return Results.NotFound(new
        {
            success = false,
            message = "User not found."
        });
    }

    var request = await db.FriendRequests
        .FirstOrDefaultAsync(r =>
            r.Id == requestId &&
            r.ReceiverUserId == user.Id &&
            r.Status == "Pending");

    if (request == null)
    {
        return Results.NotFound(new
        {
            success = false,
            message = "Friend request not found."
        });
    }

    if (action.Equals("decline", StringComparison.OrdinalIgnoreCase))
    {
        request.Status = "Declined";

        await db.SaveChangesAsync();

        return Results.Ok(new
        {
            success = true,
            message = "Friend request declined."
        });
    }

    if (action.Equals("accept", StringComparison.OrdinalIgnoreCase))
    {
        var alreadyFriends = await db.Friendships
            .AnyAsync(f =>
                (f.UserId == user.Id &&
                 f.FriendUserId == request.SenderUserId) ||
                (f.UserId == request.SenderUserId &&
                 f.FriendUserId == user.Id));

        if (!alreadyFriends)
        {
            db.Friendships.Add(new Friendship
            {
                UserId = user.Id,
                FriendUserId = request.SenderUserId,
                CreatedAt = DateTime.UtcNow
            });

            db.Friendships.Add(new Friendship
            {
                UserId = request.SenderUserId,
                FriendUserId = user.Id,
                CreatedAt = DateTime.UtcNow
            });
        }

        request.Status = "Accepted";

        await db.SaveChangesAsync();

        return Results.Ok(new
        {
            success = true,
            message = "Friend request accepted."
        });
    }

    return Results.BadRequest(new
    {
        success = false,
        message = "Action must be 'accept' or 'decline'."
    });
});

/* =========================
   Get Friends
   ========================= */

app.MapGet("/api/friends", async (
    string username,
    MurMurDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(username))
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "Username is required."
        });
    }

    var user = await db.Users
        .FirstOrDefaultAsync(u =>
            u.Username.ToLower() == username.Trim().ToLower());

    if (user == null)
    {
        return Results.NotFound(new
        {
            success = false,
            message = "User not found."
        });
    }

    var friends = await db.Friendships
        .Where(f => f.UserId == user.Id)
        .Join(
            db.Users,
            friendship => friendship.FriendUserId,
            friend => friend.Id,
            (friendship, friend) => new
            {
                username = friend.Username,
                displayName = friend.DisplayName,
                profilePicture =
                    string.IsNullOrWhiteSpace(friend.ProfilePicture)
                        ? null
                        : $"/uploads/profile-pictures/{friend.ProfilePicture}"
            })
        .ToListAsync();

    return Results.Ok(new
    {
        success = true,
        friends
    });
});

/* =========================
   Change Password
   ========================= */

app.MapPost("/api/change-password", async (
    ChangePasswordRequest request,
    MurMurDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(request.Username) ||
        string.IsNullOrWhiteSpace(request.NewPassword))
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "Username and new password are required."
        });
    }

    if (request.NewPassword.Length < 8)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "Password must be at least 8 characters."
        });
    }

    var user = await db.Users
        .FirstOrDefaultAsync(u =>
            u.Username.ToLower() == request.Username.ToLower());

    if (user == null)
    {
        return Results.Json(
            new
            {
                success = false,
                message = "Unable to change password."
            },
            statusCode: 404);
    }

    if (!user.MustChangePassword)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "This account does not require a password change."
        });
    }

    var passwordHasher = new PasswordHasher<User>();

    user.PasswordHash =
        passwordHasher.HashPassword(
            user,
            request.NewPassword);

    user.MustChangePassword = false;

    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        success = true
    });
});

app.MapPost("/api/first-startup", async (
    FirstStartupRequest request,
    MurMurDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(request.Username) ||
        string.IsNullOrWhiteSpace(request.DisplayName))
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "Username and display name are required."
        });
    }

    var displayName = request.DisplayName.Trim();

    if (displayName.Length < 1 || displayName.Length > 32)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "Display name must be between 1 and 32 characters."
        });
    }

    var user = await db.Users
        .FirstOrDefaultAsync(u =>
            u.Username.ToLower() == request.Username.ToLower());

    if (user == null)
    {
        return Results.Json(
            new
            {
                success = false,
                message = "Unable to find account."
            },
            statusCode: 404);
    }

    if (!user.FirstStartup)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "First startup has already been completed."
        });
    }

    user.DisplayName = displayName;
    user.FirstStartup = false;

    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        success = true,
        username = user.Username,
        displayName = user.DisplayName
    });
});

app.MapPost("/api/users/profile-picture", async (
    HttpRequest request,
    MurMurDbContext db,
    IWebHostEnvironment environment) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "Invalid upload request."
        });
    }

    var form = await request.ReadFormAsync();

    var username =
        form["username"].ToString();

    var file =
        form.Files["profilePicture"];

    if (string.IsNullOrWhiteSpace(username))
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "Username is required."
        });
    }

    if (file == null || file.Length == 0)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "No profile picture was provided."
        });
    }

    var user = await db.Users
        .FirstOrDefaultAsync(u =>
            u.Username.ToLower() ==
            username.Trim().ToLower());

    if (user == null)
    {
        return Results.NotFound(new
        {
            success = false,
            message = "User not found."
        });
    }

    const long maxFileSize =
        5 * 1024 * 1024;

    if (file.Length > maxFileSize)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "Profile pictures must be 5 MB or smaller."
        });
    }

    var allowedExtensions =
        new[]
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".webp"
        };

    var extension =
        Path.GetExtension(file.FileName)
            .ToLowerInvariant();

    if (!allowedExtensions.Contains(extension))
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "Only JPG, PNG, and WebP images are allowed."
        });
    }

    var uploadsDirectory =
        Path.Combine(
            environment.ContentRootPath,
            "uploads",
            "profile-pictures"
        );

    Directory.CreateDirectory(
        uploadsDirectory
    );

    var fileName =
        $"{user.Id}_{Guid.NewGuid():N}{extension}";

    var filePath =
        Path.Combine(
            uploadsDirectory,
            fileName
        );

    await using (
        var stream =
            new FileStream(
                filePath,
                FileMode.Create
            )
    )
    {
        await file.CopyToAsync(stream);
    }

    user.ProfilePicture =
        fileName;

    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        success = true,
        message = "Profile picture updated.",
        profilePicture = fileName
    });
});

app.MapPost("/api/messages/read", async (
    string username,
    string with,
    MurMurDbContext db) =>
{
    var user =
        await db.Users
            .FirstOrDefaultAsync(
                u => u.Username == username
            );

    var otherUser =
        await db.Users
            .FirstOrDefaultAsync(
                u => u.Username == with
            );

    if (user == null || otherUser == null)
    {
        return Results.NotFound(new
        {
            success = false,
            message = "User not found."
        });
    }

    var unreadMessages =
        await db.Messages
            .Where(m =>
                m.ReceiverUserId == user.Id &&
                m.SenderUserId == otherUser.Id &&
                !m.IsRead
            )
            .ToListAsync();

    foreach (var message in unreadMessages)
    {
        message.IsRead = true;
    }

    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        success = true
    });
});

app.Run();


static string ReadPassword()
{
    var password = new System.Text.StringBuilder();

    while (true)
    {
        var key = Console.ReadKey(intercept: true);

        if (key.Key == ConsoleKey.Enter)
        {
            break;
        }

        if (key.Key == ConsoleKey.Backspace)
        {
            if (password.Length > 0)
            {
                password.Length--;

                Console.Write("\b \b");
            }

            continue;
        }

        if (!char.IsControl(key.KeyChar))
        {
            password.Append(key.KeyChar);
            Console.Write("*");
        }
    }

    return password.ToString();
}
public class SendMessageRequest
{
    public string SenderUsername { get; set; } = "";

    public string ReceiverUsername { get; set; } = "";

    public string Content { get; set; } = "";
}