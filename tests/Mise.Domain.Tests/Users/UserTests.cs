using Mise.Domain.Users;

namespace Mise.Domain.Tests.Users;

public class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly LockoutPolicy Policy = new(MaxFailedAttempts: 3, Duration: TimeSpan.FromMinutes(5));

    [Fact]
    public void Login_is_normalized_and_validated()
    {
        var user = User.Create(Guid.NewGuid(), "  Manager.One ", "Анна", UserRole.Manager, "hash", Now);

        Assert.Equal("manager.one", user.Login);
        Assert.True(user.IsActive);
        Assert.Throws<ArgumentException>(() => User.Create(Guid.NewGuid(), "a b", "x", UserRole.Chef, "hash", Now));
        Assert.Throws<ArgumentException>(() => User.Create(Guid.NewGuid(), "ab", "x", UserRole.Chef, "hash", Now));
    }

    [Fact]
    public void Failed_attempts_lock_the_account_for_a_while()
    {
        var user = User.Create(Guid.NewGuid(), "chef", "Повар", UserRole.Chef, "hash", Now);

        user.RegisterFailedLogin(Now, Policy);
        user.RegisterFailedLogin(Now, Policy);
        Assert.False(user.IsLockedOut(Now));

        user.RegisterFailedLogin(Now, Policy);
        Assert.True(user.IsLockedOut(Now.AddMinutes(4)));
        Assert.Equal(Now.AddMinutes(5), user.LockoutUntil);
        Assert.False(user.IsLockedOut(Now.AddMinutes(5)));
    }

    [Fact]
    public void Successful_login_resets_counters()
    {
        var user = User.Create(Guid.NewGuid(), "chef", "Повар", UserRole.Chef, "hash", Now);
        user.RegisterFailedLogin(Now, Policy);
        user.RegisterFailedLogin(Now, Policy);

        user.RegisterSuccessfulLogin(Now.AddMinutes(1));

        Assert.Equal(0, user.FailedLoginCount);
        Assert.Null(user.LockoutUntil);
        Assert.Equal(Now.AddMinutes(1), user.LastLoginAt);
    }

    [Fact]
    public void Lockout_starts_over_after_it_expires()
    {
        var user = User.Create(Guid.NewGuid(), "chef", "Повар", UserRole.Chef, "hash", Now);
        for (var i = 0; i < 3; i++)
            user.RegisterFailedLogin(Now, Policy);

        var later = Now.AddMinutes(10);
        user.RegisterFailedLogin(later, Policy);

        Assert.False(user.IsLockedOut(later));
        Assert.Equal(1, user.FailedLoginCount);
    }

    [Fact]
    public void Refresh_token_is_active_until_revoked_or_expired()
    {
        var token = new RefreshToken(Guid.NewGuid(), Guid.NewGuid(), "hash", Guid.NewGuid(), Now, Now.AddDays(7));

        Assert.True(token.IsActiveAt(Now.AddDays(6)));
        Assert.False(token.IsActiveAt(Now.AddDays(7)));

        var next = Guid.NewGuid();
        token.Revoke(Now.AddHours(1), RefreshTokenRevocation.Rotated, next);
        token.Revoke(Now.AddHours(2), RefreshTokenRevocation.Reused);

        Assert.False(token.IsActiveAt(Now.AddHours(1)));
        Assert.Equal(RefreshTokenRevocation.Rotated, token.RevokeReason);
        Assert.Equal(next, token.ReplacedById);
        Assert.Equal(Now.AddHours(1), token.RevokedAt);
    }
}
