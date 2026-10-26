using FluentAssertions;
using TicketHub.Infrastructure.Authentication;

namespace TicketHub.UnitTests.Infrastructure;

public sealed class BCryptPasswordHasherTests
{
    // Minimum work factor keeps the suite fast; production uses BCryptPasswordHasher.DefaultWorkFactor.
    private readonly BCryptPasswordHasher _hasher = new(workFactor: 4);

    [Fact]
    public void Hash_ThenVerify_WithSamePassword_Succeeds()
    {
        var hash = _hasher.Hash("Password123!");

        hash.Should().NotBe("Password123!");
        _hasher.Verify("Password123!", hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_WithWrongPassword_Fails()
    {
        var hash = _hasher.Hash("Password123!");

        _hasher.Verify("Password123?", hash).Should().BeFalse();
    }

    [Fact]
    public void Verify_WithMalformedHash_ReturnsFalse()
    {
        _hasher.Verify("Password123!", "not-a-bcrypt-hash").Should().BeFalse();
    }
}
