using FluentValidation;
using TicketHub.Domain.Entities;

namespace TicketHub.Application.Features.Venues.ConfigureSections;

public sealed class ConfigureSectionsAndSeatsCommandValidator : AbstractValidator<ConfigureSectionsAndSeatsCommand>
{
    public ConfigureSectionsAndSeatsCommandValidator()
    {
        RuleFor(x => x.VenueId)
            .NotEmpty();

        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(SectionConstraints.NameMaxLength);

        RuleFor(x => x.Rows)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(rows => rows.Count <= SectionConstraints.MaxRowsPerSection)
            .WithMessage($"A section cannot have more than {SectionConstraints.MaxRowsPerSection} rows.")
            .Must(HaveUniqueRowNumbers)
            .WithMessage("Row numbers must be unique within a section.");

        RuleForEach(x => x.Rows).ChildRules(row =>
        {
            row.RuleFor(r => r.RowNumber)
                .NotEmpty()
                .MaximumLength(SectionConstraints.RowNumberMaxLength);

            row.RuleFor(r => r.SeatCount)
                .InclusiveBetween(1, SectionConstraints.MaxSeatsPerRow);
        });
    }

    private static bool HaveUniqueRowNumbers(IReadOnlyList<SectionRowDto>? rows) =>
        rows is null || rows
            .Where(r => !string.IsNullOrWhiteSpace(r.RowNumber))
            .GroupBy(r => r.RowNumber.Trim().ToUpperInvariant())
            .All(group => group.Count() == 1);
}
