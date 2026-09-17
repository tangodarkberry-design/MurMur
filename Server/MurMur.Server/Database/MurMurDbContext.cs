using Microsoft.EntityFrameworkCore;
using MurMur.Server.Models;

namespace MurMur.Server.Database;

public class MurMurDbContext : DbContext
{
    public MurMurDbContext(DbContextOptions<MurMurDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<FriendRequest> FriendRequests => Set<FriendRequest>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<Message> Messages { get; set; }
}