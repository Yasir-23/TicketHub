using FluentValidation;
using TicketHub.Domain.Entities;

namespace TicketHub.Application.Features.Auth.Login;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .MaximumLength(UserConstraints.EmailMaxLength)
            .EmailAddress();

        RuleFor(x => x.Password)
            .NotEmpty();
    }
}
