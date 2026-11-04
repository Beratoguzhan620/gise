using Gise.Core.Events;
using Gise.Core.Organizations;
using Gise.Core.Venues;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gise.Infrastructure.Persistence.Configurations;

internal sealed class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        builder.ToTable("events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Title).HasMaxLength(Event.TitleMaxLength);
        builder.Property(e => e.Description).HasMaxLength(Event.DescriptionMaxLength);
        builder.Property(e => e.VenueName).HasMaxLength(Venue.NameMaxLength);
        builder.Property(e => e.City).HasMaxLength(Venue.CityMaxLength);
        builder.Property(e => e.Address).HasMaxLength(Venue.AddressMaxLength);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(10);

        builder.OwnsMany(e => e.Categories, category =>
        {
            category.ToTable("price_categories");
            category.WithOwner().HasForeignKey("EventId");
            category.HasKey(c => c.Id);
            category.Property(c => c.Id).ValueGeneratedNever();
            category.Property(c => c.Name).HasMaxLength(PriceCategory.NameMaxLength);
        });
        builder.OwnsMany(e => e.Pricing, pricing => pricing.ToJson());
        builder.OwnsMany(e => e.Layout, LayoutJson.Configure);

        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Venue>().WithMany().HasForeignKey(e => e.VenueId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.OrganizationId);
        builder.HasIndex(e => new { e.Status, e.StartsAt });
    }
}

internal sealed class PlaceConfiguration : IEntityTypeConfiguration<Place>
{
    public void Configure(EntityTypeBuilder<Place> builder)
    {
        builder.ToTable("places");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.SectionKey).HasMaxLength(Section.KeyMaxLength);
        builder.Property(p => p.Row).HasMaxLength(SeatRow.LabelMaxLength);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(10);
        builder.HasOne<Event>().WithMany().HasForeignKey(p => p.EventId).OnDelete(DeleteBehavior.Cascade);
        // A seat exists once; standing spots have no row, so NULLS NOT DISTINCT keeps their numbers unique too.
        builder.HasIndex(p => new { p.EventId, p.SectionKey, p.Row, p.Number }).IsUnique().AreNullsDistinct(false);
        builder.HasIndex(p => new { p.EventId, p.SectionKey, p.Status });
        builder.HasIndex(p => p.HoldId).HasFilter("hold_id IS NOT NULL");
    }
}
