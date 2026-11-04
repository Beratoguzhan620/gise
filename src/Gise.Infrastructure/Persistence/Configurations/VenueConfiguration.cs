using Gise.Core.Organizations;
using Gise.Core.Venues;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gise.Infrastructure.Persistence.Configurations;

internal sealed class VenueConfiguration : IEntityTypeConfiguration<Venue>
{
    public void Configure(EntityTypeBuilder<Venue> builder)
    {
        builder.ToTable("venues");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();
        builder.Property(v => v.Name).HasMaxLength(Venue.NameMaxLength);
        builder.Property(v => v.City).HasMaxLength(Venue.CityMaxLength);
        builder.Property(v => v.Address).HasMaxLength(Venue.AddressMaxLength);
        builder.OwnsMany(v => v.Sections, LayoutJson.Configure);
        builder.HasOne<Organization>().WithMany().HasForeignKey(v => v.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(v => v.OrganizationId);
    }
}

/// <summary>Floor plans are stored as JSON: they are read and written whole, never queried by part.</summary>
internal static class LayoutJson
{
    public static void Configure<TOwner>(OwnedNavigationBuilder<TOwner, Section> section)
        where TOwner : class
    {
        section.ToJson();
        section.Property(s => s.Kind).HasConversion<string>();
        section.OwnsMany(s => s.Rows);
    }
}
