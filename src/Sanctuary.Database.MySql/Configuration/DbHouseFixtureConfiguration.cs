using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Sanctuary.Database.Entities;

namespace Sanctuary.Database.MySql.Configuration;

public sealed class DbHouseFixtureConfiguration : IEntityTypeConfiguration<DbHouseFixture>
{
    public void Configure(EntityTypeBuilder<DbHouseFixture> builder)
    {
        builder.HasKey(fixture => fixture.Id);
        builder.Property(fixture => fixture.Id).ValueGeneratedNever();
        builder.HasIndex(fixture => fixture.PlacementId).IsUnique();
        builder.HasOne(fixture => fixture.House).WithMany()
            .HasForeignKey(fixture => fixture.HouseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(fixture => fixture.Item).WithMany()
            .HasForeignKey(fixture => new { fixture.ItemId, fixture.CharacterId }).OnDelete(DeleteBehavior.Restrict);
    }
}
