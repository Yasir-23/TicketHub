using FluentAssertions;
using TicketHub.Application.Features.Events.GetEvents;
using TicketHub.Domain.Entities;
using TicketHub.Domain.Enums;
using TicketHub.Infrastructure.Caching;
using TicketHub.Infrastructure.Persistence;
using TicketHub.UnitTests.TestSupport;

namespace TicketHub.UnitTests.Application.Events;

public sealed class GetEventsQueryTests : IAsyncLifetime
{
    private static readonly DateTime Now = TestDbContextFactory.UtcNow.UtcDateTime;

    private readonly AppDbContext _dbContext = TestDbContextFactory.Create();
    private readonly GetEventsQueryValidator _validator = new();

    public async Task InitializeAsync()
    {
        var london = Venue.Create("Olympia Arena", "London", "Blythe Rd");
        var vip = london.AddSection("VIP", [new SeatRowSpecification("A", 2)]);
        var balcony = london.AddSection("Balcony", [new SeatRowSpecification("A", 2)]);
        var paris = Venue.Create("Accor Arena", "Paris", "8 Bd de Bercy");
        var parisFloor = paris.AddSection("Floor", [new SeatRowSpecification("A", 2)]);
        _dbContext.Venues.AddRange(london, paris);

        AddEvent(london, "Coldplay Live", EventCategory.Concert, "World tour", days: 10, (vip.Id, 15000), (balcony.Id, 7500));
        AddEvent(london, "Tech Summit", EventCategory.Seminar, "Talks about cloud and AI", days: 20, (vip.Id, 5000));
        AddEvent(paris, "Champions Final", EventCategory.Sports, "Football final", days: 30, (parisFloor.Id, 20000));
        AddEvent(paris, "Jazz Night", EventCategory.Concert, "Smooth coldplay covers", days: 40);
        var deleted = AddEvent(london, "Cancelled Gig", EventCategory.Concert, "Gone", days: 15);
        deleted.MarkDeleted(Now);

        await _dbContext.SaveChangesAsync();
    }

    public Task DisposeAsync() => _dbContext.DisposeAsync().AsTask();

    [Fact]
    public async Task WithoutFilters_ReturnsLiveEventsOrderedByStartDate()
    {
        var result = await Handle(new GetEventsQuery());

        result.Items.Select(e => e.Title).Should().Equal("Coldplay Live", "Tech Summit", "Champions Final", "Jazz Night");
        result.TotalCount.Should().Be(4);
        result.PageNumber.Should().Be(1);
        result.PageSize.Should().Be(GetEventsQuery.DefaultPageSize);
        result.TotalPages.Should().Be(1);
    }

    [Fact]
    public async Task Search_MatchesTitleOrDescriptionCaseInsensitively()
    {
        var result = await Handle(new GetEventsQuery { Search = "COLDPLAY" });

        result.Items.Select(e => e.Title).Should().BeEquivalentTo("Coldplay Live", "Jazz Night");
    }

    [Fact]
    public async Task CityFilter_IsCaseInsensitive()
    {
        var result = await Handle(new GetEventsQuery { City = "paris" });

        result.Items.Should().OnlyContain(e => e.City == "Paris").And.HaveCount(2);
    }

    [Fact]
    public async Task CategoryFilter_ReturnsOnlyThatCategory()
    {
        var result = await Handle(new GetEventsQuery { Category = EventCategory.Concert });

        result.Items.Select(e => e.Title).Should().Equal("Coldplay Live", "Jazz Night");
    }

    [Fact]
    public async Task DateRangeFilter_IsInclusive()
    {
        var result = await Handle(new GetEventsQuery
        {
            StartDateFrom = Now.AddDays(20),
            StartDateTo = Now.AddDays(30)
        });

        result.Items.Select(e => e.Title).Should().Equal("Tech Summit", "Champions Final");
    }

    [Fact]
    public async Task Pagination_ReturnsRequestedPageAndTotals()
    {
        var result = await Handle(new GetEventsQuery { PageNumber = 2, PageSize = 3 });

        result.Items.Select(e => e.Title).Should().Equal("Jazz Night");
        result.TotalCount.Should().Be(4);
        result.TotalPages.Should().Be(2);
        result.PageNumber.Should().Be(2);
        result.PageSize.Should().Be(3);
    }

    [Fact]
    public async Task Items_IncludeVenueAndMinimumPrice()
    {
        var result = await Handle(new GetEventsQuery());

        var coldplay = result.Items.Single(e => e.Title == "Coldplay Live");
        coldplay.VenueName.Should().Be("Olympia Arena");
        coldplay.City.Should().Be("London");
        coldplay.MinPriceCents.Should().Be(7500);

        result.Items.Single(e => e.Title == "Jazz Night").MinPriceCents.Should().BeNull();
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, GetEventsQuery.MaxPageSize + 1)]
    public void Validator_RejectsOutOfRangePaging(int pageNumber, int pageSize)
    {
        _validator.Validate(new GetEventsQuery { PageNumber = pageNumber, PageSize = pageSize })
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_RejectsInvertedDateRange()
    {
        _validator.Validate(new GetEventsQuery { StartDateFrom = Now.AddDays(2), StartDateTo = Now.AddDays(1) })
            .IsValid.Should().BeFalse();
    }

    private Task<TicketHub.Application.Common.Models.PagedResult<EventSummaryDto>> Handle(GetEventsQuery query) =>
        new GetEventsQueryHandler(_dbContext, TestCache.Create(), new InMemoryCatalogCacheGeneration())
            .Handle(query, CancellationToken.None);

    private Event AddEvent(
        Venue venue,
        string title,
        EventCategory category,
        string description,
        int days,
        params (Guid SectionId, long PriceCents)[] prices)
    {
        var start = Now.AddDays(days);
        var @event = Event.Create(venue.Id, title, category, description, start, start.AddHours(3), Now);
        foreach (var (sectionId, priceCents) in prices)
        {
            @event.SetSectionPrice(sectionId, priceCents, Now);
        }

        _dbContext.Events.Add(@event);
        return @event;
    }
}
