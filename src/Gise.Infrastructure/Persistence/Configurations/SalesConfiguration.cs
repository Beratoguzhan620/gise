using Gise.Core.Events;
using Gise.Core.Queue;
using Gise.Core.Sales;
using Gise.Core.Users;
using Gise.Core.Venues;
using Gise.Infrastructure.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gise.Infrastructure.Persistence.Configurations;

internal sealed class QueueEntryConfiguration : IEntityTypeConfiguration<QueueEntry>
{
    public void Configure(EntityTypeBuilder<QueueEntry> builder)
    {
        builder.ToTable("queue_entries");
        builder.HasKey(q => new { q.EventId, q.UserId });
        builder.Property(q => q.Sequence).HasDefaultValueSql("nextval('queue_sequence')");
        builder.HasOne<Event>().WithMany().HasForeignKey(q => q.EventId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(q => q.UserId).OnDelete(DeleteBehavior.Cascade);
        // Someone's position is how many entries sort before theirs: this index answers it without a scan.
        builder.HasIndex(q => new { q.EventId, q.OrderKey, q.Sequence });
    }
}

internal sealed class HoldConfiguration : IEntityTypeConfiguration<Hold>
{
    public void Configure(EntityTypeBuilder<Hold> builder)
    {
        builder.ToTable("holds");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).ValueGeneratedNever();
        builder.Property(h => h.Status).HasConversion<string>().HasMaxLength(10);
        builder.HasOne<Event>().WithMany().HasForeignKey(h => h.EventId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(h => h.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(h => new { h.EventId, h.UserId, h.Status });
        builder.HasIndex(h => h.ExpiresAt).HasFilter("status = 'Active'");
    }
}

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.Number).HasMaxLength(Order.NumberLength);
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(o => o.Currency).HasMaxLength(3);
        builder.Property(o => o.PaymentProvider).HasMaxLength(20);
        builder.Property(o => o.PaymentId).HasMaxLength(100);
        builder.Property(o => o.CheckoutUrl).HasMaxLength(500);
        builder.Property(o => o.RefundReason).HasConversion<string>().HasMaxLength(16);
        builder.OwnsMany(o => o.Lines, line =>
        {
            line.ToJson();
            line.Property(l => l.SectionKey).HasMaxLength(Section.KeyMaxLength);
        });
        builder.HasOne<Event>().WithMany().HasForeignKey(o => o.EventId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(o => o.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Hold>().WithOne().HasForeignKey<Order>(o => o.HoldId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(o => o.Number).IsUnique();
        builder.HasIndex(o => new { o.UserId, o.CreatedAt });
        builder.HasIndex(o => new { o.EventId, o.Status });
        builder.HasIndex(o => o.PaymentDeadline).HasFilter("status = 'PendingPayment'");
        builder.HasIndex(o => o.RefundRequestedAt).HasFilter("status = 'RefundPending'");
    }
}

internal sealed class ProcessedNoticeConfiguration : IEntityTypeConfiguration<ProcessedNotice>
{
    public void Configure(EntityTypeBuilder<ProcessedNotice> builder)
    {
        builder.ToTable("processed_notices");
        builder.HasKey(n => new { n.Provider, n.NoticeId });
        builder.Property(n => n.Provider).HasMaxLength(20);
        builder.Property(n => n.NoticeId).HasMaxLength(100);
        builder.Property(n => n.Type).HasMaxLength(40);
        builder.HasIndex(n => n.OrderId);
    }
}
