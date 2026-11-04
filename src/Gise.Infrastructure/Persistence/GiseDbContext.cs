using Gise.Core.Events;
using Gise.Core.Organizations;
using Gise.Core.Users;
using Gise.Core.Venues;
using Microsoft.EntityFrameworkCore;

namespace Gise.Infrastructure.Persistence;

/// <summary>Accounts, organizers and their teams, venues, events and the places they sell.</summary>
public sealed class GiseDbContext(DbContextOptions<GiseDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<Membership> Memberships => Set<Membership>();

    public DbSet<Venue> Venues => Set<Venue>();

    public DbSet<Event> Events => Set<Event>();

    public DbSet<Place> Places => Set<Place>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GiseDbContext).Assembly);
}
