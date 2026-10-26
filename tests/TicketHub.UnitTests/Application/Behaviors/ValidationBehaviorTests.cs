using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TicketHub.Application;
using TicketHub.Application.Common.Behaviors;
using TicketHub.Application.Common.Interfaces;
using TicketHub.Application.Features.Auth.RegisterCustomer;

namespace TicketHub.UnitTests.Application.Behaviors;

public sealed class ValidationBehaviorTests
{
    public sealed record SampleRequest(string Name) : IRequest<string>;

    private sealed class SampleRequestValidator : AbstractValidator<SampleRequest>
    {
        public SampleRequestValidator() => RuleFor(x => x.Name).NotEmpty();
    }

    [Fact]
    public async Task Handle_WithoutValidators_InvokesNext()
    {
        var behavior = new ValidationBehavior<SampleRequest, string>([]);

        var result = await behavior.Handle(new SampleRequest(""), () => Task.FromResult("ok"), CancellationToken.None);

        result.Should().Be("ok");
    }

    [Fact]
    public async Task Handle_WithValidRequest_InvokesNext()
    {
        var behavior = new ValidationBehavior<SampleRequest, string>([new SampleRequestValidator()]);

        var result = await behavior.Handle(new SampleRequest("valid"), () => Task.FromResult("ok"), CancellationToken.None);

        result.Should().Be("ok");
    }

    [Fact]
    public async Task Handle_WithInvalidRequest_ThrowsValidationExceptionAndSkipsNext()
    {
        var nextCalled = false;
        var behavior = new ValidationBehavior<SampleRequest, string>([new SampleRequestValidator()]);

        var act = () => behavior.Handle(
            new SampleRequest(""),
            () =>
            {
                nextCalled = true;
                return Task.FromResult("ok");
            },
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().ContainSingle(e => e.PropertyName == nameof(SampleRequest.Name));
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WithMultipleValidators_AggregatesAllFailures()
    {
        var first = new Mock<IValidator<SampleRequest>>();
        first.Setup(v => v.ValidateAsync(It.IsAny<IValidationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult([new ValidationFailure("A", "first failure")]));
        var second = new Mock<IValidator<SampleRequest>>();
        second.Setup(v => v.ValidateAsync(It.IsAny<IValidationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult([new ValidationFailure("B", "second failure")]));

        var behavior = new ValidationBehavior<SampleRequest, string>([first.Object, second.Object]);

        var act = () => behavior.Handle(new SampleRequest("x"), () => Task.FromResult("ok"), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Select(e => e.PropertyName).Should().BeEquivalentTo("A", "B");
    }

    [Fact]
    public async Task MediatorPipeline_WithInvalidRegisterCommand_RejectsBeforeHandlerRuns()
    {
        var dbContext = new Mock<IAppDbContext>(MockBehavior.Strict);
        var passwordHasher = new Mock<IPasswordHasher>(MockBehavior.Strict);

        var services = new ServiceCollection()
            .AddLogging()
            .AddApplication()
            .AddSingleton(dbContext.Object)
            .AddSingleton(passwordHasher.Object);
        await using var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        var act = () => sender.Send(new RegisterCustomerCommand("Jane Doe", "not-an-email", "weak"));

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Select(e => e.PropertyName).Should().Contain(
            [nameof(RegisterCustomerCommand.Email), nameof(RegisterCustomerCommand.Password)]);

        // Strict mocks would throw if the handler had touched persistence or hashing.
        passwordHasher.VerifyNoOtherCalls();
        dbContext.VerifyNoOtherCalls();
    }
}
