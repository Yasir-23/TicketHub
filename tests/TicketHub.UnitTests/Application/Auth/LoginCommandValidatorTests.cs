using FluentAssertions;
using TicketHub.Application.Features.Auth.Login;

namespace TicketHub.UnitTests.Application.Auth;

public sealed class LoginCommandValidatorTests
{
    private readonly LoginCommandValidator _validator = new();

    [Fact]
    public void Validate_WithValidCommand_Passes()
    {
        _validator.Validate(new LoginCommand("admin@tickethub.com", "AdminSecurePassword123!"))
            .IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("", "secret")]
    [InlineData("not-an-email", "secret")]
    [InlineData("admin@tickethub.com", "")]
    public void Validate_WithMissingOrInvalidFields_Fails(string email, string password)
    {
        _validator.Validate(new LoginCommand(email, password)).IsValid.Should().BeFalse();
    }
}
