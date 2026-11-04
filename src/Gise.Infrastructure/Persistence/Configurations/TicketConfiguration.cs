using Gise.Core.Events;
using Gise.Core.Sales;
using Gise.Core.Tickets;
using Gise.Core.Users;
using Gise.Core.Venues;
using Gise.Infrastructure.Mail;
using Gise.Infrastructure.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gise.Infrastructure.Persistence.Configurations;

internal sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("tickets");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.HolderName).HasMaxLength(Ticket.HolderMaxLength);
        builder.Property(t => t.SectionName).HasMaxLength(Section.NameMaxLength);
        builder.Property(t => t.Row).HasMaxLength(SeatRow.LabelMaxLength);
        builder.Property(t => t.CategoryName).HasMaxLength(PriceCategory.NameMaxLength);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(8);
        builder.Property(t => t.CheckedInGate).HasMaxLength(Ticket.GateMaxLength);
        builder.Ignore(t => t.Label);
        builder.HasOne<Order>().WithMany().HasForeignKey(t => t.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Event>().WithMany().HasForeignKey(t => t.EventId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(t => t.OrderId);
        builder.HasIndex(t => new { t.EventId, t.Status });
        builder.HasIndex(t => t.UserId);
    }
}

internal sealed class CheckInConfiguration : IEntityTypeConfiguration<CheckIn>
{
    public void Configure(EntityTypeBuilder<CheckIn> builder)
    {
        builder.ToTable("check_ins");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Gate).HasMaxLength(Ticket.GateMaxLength);
        builder.Property(c => c.Device).HasMaxLength(CheckIn.DeviceMaxLength);
        builder.Property(c => c.Outcome).HasConversion<string>().HasMaxLength(12);
        builder.HasOne<Event>().WithMany().HasForeignKey(c => c.EventId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Ticket>().WithMany().HasForeignKey(c => c.TicketId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => new { c.EventId, c.ScannedAt });
        builder.HasIndex(c => c.TicketId);
    }
}

internal sealed class TicketKeyConfiguration : IEntityTypeConfiguration<TicketKey>
{
    public void Configure(EntityTypeBuilder<TicketKey> builder)
    {
        builder.ToTable("ticket_keys");
        builder.HasKey(k => k.Id);
        builder.Property(k => k.Id).HasMaxLength(8);
    }
}

internal sealed class OutboxEmailConfiguration : IEntityTypeConfiguration<OutboxEmail>
{
    public void Configure(EntityTypeBuilder<OutboxEmail> builder)
    {
        builder.ToTable("outbox_emails");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(10);
        builder.Property(e => e.To).HasMaxLength(User.EmailMaxLength);
        builder.Property(e => e.LastError).HasMaxLength(300);
        builder.HasIndex(e => e.NextAttemptAt).HasFilter("sent_at IS NULL");
        builder.HasIndex(e => e.OrderId);
    }
}
