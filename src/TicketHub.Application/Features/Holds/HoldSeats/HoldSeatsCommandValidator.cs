using FluentValidation;
using TicketHub.Domain.Common;

namespace TicketHub.Application.Features.Holds.HoldSeats;

public sealed class HoldSeatsCommandValidator : AbstractValidator<HoldSeatsCommand>
{
    public HoldSeatsCommandValidator()
    {
        RuleFor(x => x.EventId)
            .NotEmpty();

        RuleFor(x => x.CustomerId)
            .NotEmpty();

        RuleFor(x => x.SeatIds)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(seatIds => seatIds.Count <= BookingRules.MaxTicketsPerCustomerPerEvent)
            .WithMessage($"A hold can contain at most {BookingRules.MaxTicketsPerCustomerPerEvent} seats.")
            .Must(seatIds => seatIds.Distinct().Count() == seatIds.Count)
            .WithMessage("Each seat can only be requested once.");

        RuleForEach(x => x.SeatIds)
            .NotEmpty();
    }
}
