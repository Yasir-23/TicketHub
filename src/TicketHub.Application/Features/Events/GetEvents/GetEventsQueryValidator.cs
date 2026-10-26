using FluentValidation;
using TicketHub.Domain.Entities;

namespace TicketHub.Application.Features.Events.GetEvents;

public sealed class GetEventsQueryValidator : AbstractValidator<GetEventsQuery>
{
    // Keeps (PageNumber - 1) * PageSize within int range for Skip().
    private const int MaxPageNumber = int.MaxValue / GetEventsQuery.MaxPageSize;

    public GetEventsQueryValidator()
    {
        RuleFor(x => x.PageNumber)
            .InclusiveBetween(1, MaxPageNumber);

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, GetEventsQuery.MaxPageSize);

        RuleFor(x => x.Search)
            .MaximumLength(EventConstraints.TitleMaxLength);

        RuleFor(x => x.City)
            .MaximumLength(VenueConstraints.CityMaxLength);

        RuleFor(x => x.Category)
            .IsInEnum();

        RuleFor(x => x.StartDateTo)
            .GreaterThanOrEqualTo(x => x.StartDateFrom)
            .When(x => x.StartDateFrom.HasValue && x.StartDateTo.HasValue)
            .WithMessage("'Start Date To' must not be earlier than 'Start Date From'.");
    }
}
