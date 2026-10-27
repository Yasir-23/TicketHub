using FluentValidation;

namespace TicketHub.Application.Features.Reports.GetDailySalesReport;

public sealed class GetDailySalesReportQueryValidator : AbstractValidator<GetDailySalesReportQuery>
{
    public GetDailySalesReportQueryValidator()
    {
        RuleFor(x => x.From).NotNull();

        RuleFor(x => x.To).NotNull();

        RuleFor(x => x.To)
            .GreaterThanOrEqualTo(x => x.From)
            .When(x => x.From.HasValue && x.To.HasValue)
            .WithMessage("'To' must not be earlier than 'From'.");

        // Bounds the zero-filled day list the response contains.
        RuleFor(x => x)
            .Must(x => x.To!.Value.DayNumber - x.From!.Value.DayNumber < GetDailySalesReportQuery.MaxRangeDays)
            .When(x => x.From.HasValue && x.To.HasValue && x.To >= x.From)
            .OverridePropertyName(nameof(GetDailySalesReportQuery.To))
            .WithMessage($"The date range must not exceed {GetDailySalesReportQuery.MaxRangeDays} days.");
    }
}
