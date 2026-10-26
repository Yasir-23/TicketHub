using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using TicketHub.Application.Common.Exceptions;
using TicketHub.Application.Common.Interfaces;
using TicketHub.Application.Features.Auth.RegisterCustomer;
using TicketHub.Domain.Entities;
using TicketHub.Domain.Enums;
using TicketHub.UnitTests.TestSupport;

namespace TicketHub.UnitTests.Application.Auth;

public sealed class RegisterCustomerCommandHandlerTests
{
    private readonly Mock<IPasswordHasher> _passwordHasher = new();

    public RegisterCustomerCommandHandlerTests()
    {
        _passwordHasher.Setup(h => h.Hash(It.IsAny<string>())).Returns<string>(p => $"hashed::{p}");
    }

    [Fact]
    public async Task Handle_WithNewEmail_PersistsCustomerWithHashedPassword()
    {
        await using var dbContext = TestDbContextFactory.Create();
        var handler = new RegisterCustomerCommandHandler(dbContext, _passwordHasher.Object);

        var response = await handler.Handle(
            new RegisterCustomerCommand("Jane Doe", "  Jane.Doe@Example.com ", "Password123!"),
            CancellationToken.None);

        response.Email.Should().Be("jane.doe@example.com");
        response.Role.Should().Be("Customer");

        var user = await dbContext.Users.SingleAsync();
        user.Id.Should().Be(response.UserId);
        user.FullName.Should().Be("Jane Doe");
        user.Role.Should().Be(UserRole.Customer);
        user.PasswordHash.Should().Be("hashed::Password123!");
        user.CreatedAtUtc.Should().Be(TestDbContextFactory.UtcNow.UtcDateTime);
    }

    [Fact]
    public async Task Handle_WithExistingEmailInDifferentCase_ThrowsConflict()
    {
        await using var dbContext = TestDbContextFactory.Create();
        dbContext.Users.Add(User.CreateCustomer("Existing", "jane.doe@example.com", "hash"));
        await dbContext.SaveChangesAsync();

        var handler = new RegisterCustomerCommandHandler(dbContext, _passwordHasher.Object);

        var act = () => handler.Handle(
            new RegisterCustomerCommand("Jane Doe", "JANE.DOE@example.com", "Password123!"),
            CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
        (await dbContext.Users.CountAsync()).Should().Be(1);
        _passwordHasher.Verify(h => h.Hash(It.IsAny<string>()), Times.Never);
    }
}
