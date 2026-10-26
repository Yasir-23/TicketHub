using FluentValidation;
using TicketHub.Domain.Entities;

namespace TicketHub.Application.Features.Venues.CreateVenue;

public sealed class CreateVenueCommandValidator : AbstractValidator<CreateVenueCommand>
{
    public CreateVenueCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(VenueConstraints.NameMaxLength);

        RuleFor(x => x.City)
            .NotEmpty()
            .MaximumLength(VenueConstraints.CityMaxLength);

        RuleFor(x => x.Address)
            .NotEmpty()
            .MaximumLength(VenueConstraints.AddressMaxLength);
    }
}
