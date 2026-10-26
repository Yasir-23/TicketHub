using FluentValidation;
using TicketHub.Domain.Common;
using TicketHub.Domain.Entities;

namespace TicketHub.Application.Features.Events.CreateEvent;

public sealed class CreateEventCommandValidator : AbstractValidator<CreateEventCommand>
{
    public CreateEventCommandValidator(TimeProvider timeProvider)
    {
        RuleFor(x => x.VenueId)
            .NotEmpty();

        RuleFor(x => x.Title)
            .NotEmpty()
            .MaximumLength(EventConstraints.TitleMaxLength);

        RuleFor(x => x.Category)
            .IsInEnum();

        RuleFor(x => x.Description)
            .MaximumLength(EventConstraints.DescriptionMaxLength);

        RuleFor(x => x.StartDateUtc)
            .Must(start => start.AsUtc() > timeProvider.GetUtcNow().UtcDateTime)
            .WithMessage("Event start date must be in the future.");

        RuleFor(x => x.EndDateUtc)
            .Must((command, end) => end.AsUtc() > command.StartDateUtc.AsUtc())
            .WithMessage("Event end date must be after its start date.");

        RuleFor(x => x.SectionPricing)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(pricing => pricing.Select(p => p.SectionId).Distinct().Count() == pricing.Count)
            .WithMessage("Each section can only be priced once.");

        RuleForEach(x => x.SectionPricing).ChildRules(price =>
        {
            price.RuleFor(p => p.SectionId).NotEmpty();
            price.RuleFor(p => p.PriceCents).GreaterThanOrEqualTo(0);
        });
    }
}
