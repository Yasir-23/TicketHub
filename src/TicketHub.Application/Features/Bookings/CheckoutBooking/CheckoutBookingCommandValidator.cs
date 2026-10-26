using FluentValidation;

namespace TicketHub.Application.Features.Bookings.CheckoutBooking;

public sealed class CheckoutBookingCommandValidator : AbstractValidator<CheckoutBookingCommand>
{
    public CheckoutBookingCommandValidator()
    {
        RuleFor(command => command.PaymentMethod).NotEmpty().MaximumLength(50);
        RuleFor(command => command.PaymentToken).NotEmpty().MaximumLength(256);
        RuleFor(command => command.IdempotencyKey)
            .NotEmpty()
            .MaximumLength(128)
            .Must(key => Guid.TryParse(key, out _))
            .WithMessage("Idempotency-Key must be a valid UUID.");
    }
}
