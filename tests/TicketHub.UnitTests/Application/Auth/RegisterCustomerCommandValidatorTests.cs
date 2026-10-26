using FluentAssertions;
using TicketHub.Application.Features.Auth.RegisterCustomer;

namespace TicketHub.UnitTests.Application.Auth;

public sealed class RegisterCustomerCommandValidatorTests
{
    private const string ValidName = "Jane Doe";
    private const string ValidEmail = "jane.doe@example.com";
    private const string ValidPassword = "Password123!";

    private readonly RegisterCustomerCommandValidator _validator = new();

    [Fact]
    public void Validate_WithValidCommand_Passes()
    {
        var result = _validator.Validate(new RegisterCustomerCommand(ValidName, ValidEmail, ValidPassword));

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("@example.com")]
    [InlineData("jane.doe@")]
    public void Validate_WithInvalidEmail_Fails(string email)
    {
        var result = _validator.Validate(new RegisterCustomerCommand(ValidName, email, ValidPassword));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RegisterCustomerCommand.Email));
    }

    [Fact]
    public void Validate_WithTooLongEmail_Fails()
    {
        var email = new string('a', 250) + "@example.com";

        var result = _validator.Validate(new RegisterCustomerCommand(ValidName, email, ValidPassword));

        result.Errors.Should().Contain(e => e.PropertyName == nameof(RegisterCustomerCommand.Email));
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("Pa1!", "too short")]
    [InlineData("password123!", "no uppercase letter")]
    [InlineData("PASSWORD123!", "no lowercase letter")]
    [InlineData("Password!!!", "no digit")]
    [InlineData("Password123", "no special character")]
    public void Validate_WithWeakPassword_Fails(string password, string reason)
    {
        var result = _validator.Validate(new RegisterCustomerCommand(ValidName, ValidEmail, password));

        result.IsValid.Should().BeFalse(because: $"a password with {reason} must be rejected");
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RegisterCustomerCommand.Password));
    }

    [Fact]
    public void Validate_WithTooLongPassword_Fails()
    {
        var password = "Aa1!" + new string('x', RegisterCustomerCommandValidator.PasswordMaxLength);

        var result = _validator.Validate(new RegisterCustomerCommand(ValidName, ValidEmail, password));

        result.Errors.Should().Contain(e => e.PropertyName == nameof(RegisterCustomerCommand.Password));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WithMissingFullName_Fails(string fullName)
    {
        var result = _validator.Validate(new RegisterCustomerCommand(fullName, ValidEmail, ValidPassword));

        result.Errors.Should().Contain(e => e.PropertyName == nameof(RegisterCustomerCommand.FullName));
    }
}
