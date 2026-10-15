using Mise.Domain.Common;
using Mise.Domain.Restaurants;
using Mise.Domain.Users;

namespace Mise.Domain.Tests.Restaurants;

public class InviteTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);

    private static Invite NewInvite(UserRole role = UserRole.Chef) =>
        Invite.Create(Guid.NewGuid(), role, createdBy: Guid.NewGuid(), Now, TimeSpan.FromDays(7));

    [Fact]
    public void Code_is_eight_unambiguous_characters_shown_in_two_groups()
    {
        var invite = NewInvite();

        Assert.Matches("^[ABCDEFGHJKMNPQRSTUVWXYZ23456789]{8}$", invite.Code);
        Assert.Equal($"{invite.Code[..4]}-{invite.Code[4..]}", invite.DisplayCode);
    }

    [Theory]
    [InlineData(" k7q2-pxm4 ", "K7Q2PXM4")]
    [InlineData("K7Q2PXM4", "K7Q2PXM4")]
    [InlineData("k7q2 pxm4", "K7Q2PXM4")]
    public void Typed_code_is_normalized(string typed, string expected) => Assert.Equal(expected, Invite.Normalize(typed));

    [Fact]
    public void Invite_is_single_use_and_expires()
    {
        var invite = NewInvite();
        Assert.True(invite.IsUsableAt(Now.AddDays(6)));
        Assert.False(invite.IsUsableAt(Now.AddDays(7)));

        invite.Accept(Guid.NewGuid(), Now.AddHours(1));

        Assert.False(invite.IsUsableAt(Now.AddHours(2)));
        var error = Assert.Throws<DomainException>(() => invite.Accept(Guid.NewGuid(), Now.AddHours(2)));
        Assert.Equal(Invite.Errors.NotUsable, error.Code);
    }

    [Fact]
    public void Revoked_invite_cannot_be_accepted()
    {
        var invite = NewInvite();
        invite.Revoke(Now.AddMinutes(5));

        Assert.False(invite.IsUsableAt(Now.AddMinutes(6)));
        Assert.Throws<DomainException>(() => invite.Accept(Guid.NewGuid(), Now.AddMinutes(6)));
    }

    [Fact]
    public void Service_role_cannot_be_invited()
    {
        var error = Assert.Throws<DomainException>(() => NewInvite(UserRole.Simulator));
        Assert.Equal(Invite.Errors.InvalidRole, error.Code);
    }
}
