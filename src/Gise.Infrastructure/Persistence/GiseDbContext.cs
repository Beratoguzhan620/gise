using Gise.Core.Events;
using Gise.Core.Organizations;
using Gise.Core.Queue;
using Gise.Core.Sales;
using Gise.Core.Tickets;
using Gise.Core.Users;
using Gise.Core.Venues;
using Gise.Infrastructure.Mail;
using Gise.Infrastructure.Payments;
using Gise.Infrastructure.Tickets;
using Microsoft.EntityFrameworkCore;

namespace Gise.Infrastructure.Persistence;

/// <summary>Accounts, organizers and their teams, venues, events, the places they sell, the sales, the tickets and the gate.</summary>
public sealed class GiseDbContext(DbContextOptions<GiseDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<Membership> Memberships => Set<Membership>();

    public DbSet<Venue> Venues => Set<Venue>();

    public DbSet<Event> Events => Set<Event>();

    public DbSet<Place> Places => Set<Place>();

    public DbSet<QueueEntry> QueueEntries => Set<QueueEntry>();

    public DbSet<Hold> Holds => Set<Hold>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<ProcessedNotice> ProcessedNotices => Set<ProcessedNotice>();

    public DbSet<Ticket> Tickets => Set<Ticket>();

    public DbSet<CheckIn> CheckIns => Set<CheckIn>();

    public DbSet<TicketKey> TicketKeys => Set<TicketKey>();

    public DbSet<OutboxEmail> Emails => Set<OutboxEmail>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Breaks ties in waiting rooms; a sequence rather than an identity, so someone who rejoins goes to the back.
        modelBuilder.HasSequence<long>("queue_sequence");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GiseDbContext).Assembly);
    }
}
