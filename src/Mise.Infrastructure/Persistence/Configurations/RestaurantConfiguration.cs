using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mise.Domain.Restaurants;

namespace Mise.Infrastructure.Persistence.Configurations;

internal sealed class RestaurantConfiguration : IEntityTypeConfiguration<Restaurant>
{
    public void Configure(EntityTypeBuilder<Restaurant> builder)
    {
        builder.ToTable("restaurants", t => t.HasCheckConstraint("ck_restaurants_last_order_number", "last_order_number >= 0"));
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Name).HasMaxLength(Restaurant.NameMaxLength).IsRequired();
    }
}

internal sealed class InviteConfiguration : IEntityTypeConfiguration<Invite>
{
    public void Configure(EntityTypeBuilder<Invite> builder)
    {
        builder.ToTable("invites");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();
        // Код уникален во всей системе: по нему ищут до входа, не зная ресторана.
        builder.Property(i => i.Code).HasMaxLength(Invite.CodeLength).IsRequired();
        builder.HasIndex(i => i.Code).IsUnique();
        builder.Property(i => i.Role).HasConversion<string>().HasMaxLength(16);
        builder.Ignore(i => i.DisplayCode);
    }
}
