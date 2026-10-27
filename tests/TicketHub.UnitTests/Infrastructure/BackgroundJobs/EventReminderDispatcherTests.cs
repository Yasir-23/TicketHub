using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TicketHub.Domain.Entities;
using TicketHub.Domain.Enums;
using TicketHub.Infrastructure.BackgroundJobs;
using TicketHub.Infrastructure.Persistence;
using TicketHub.UnitTests.TestSupport;

namespace TicketHub.UnitTests.Infrastructure.BackgroundJobs;

public sealed class EventReminderDispatcherTests : IAsyncLifetime
{
    private static readonly DateTime Now = TestDbContextFactory.UtcNow.UtcDateTime;

    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly AppDbContext _dbContext;
    private readonly RecordingLogger<EventReminderDispatcher> _logger = new();
    private readonly User _customer = User.CreateCustomer("Jane Doe", "jane@example.com", "hash");
    private Venue _venue = null!;

    public EventReminderDispatcherTests() => _dbContext = TestDbContextFactory.Create(_databaseName);

    public async Task InitializeAsync()
    {
        _venue = Venue.Create("Olympia Arena", "London", "Blythe Rd");
        _dbContext.Venues.Add(_venue);
        _dbContext.Users.Add(_customer);
        await _dbContext.SaveChangesAsync();
    }

    public Task DisposeAsync() => _dbContext.DisposeAsync().AsTask();

    [Fact]
    public async Task DueForReminder_SelectsConfirmedBookingsStartingIn24To25Hours()
    {
        var justBefore = await AddBooking(Now.AddHours(24).AddMinutes(-1));
        var windowStart = await AddBooking(Now.AddHours(24));
        var midWindow = await AddBooking(Now.AddHours(24).AddMinutes(30));
        var windowEnd = await AddBooking(Now.AddHours(25));
        var dayAway = await AddBooking(Now.AddDays(3));
        var cancelled = await AddBooking(Now.AddHours(24).AddMinutes(10), booking => booking.Cancel(Now));
        var alreadyReminded = await AddBooking(Now.AddHours(24).AddMinutes(20), booking => booking.MarkReminderSent(Now.AddHours(-1)));

        var due = await EventReminderDispatcher
            .DueForReminder(_dbContext.Bookings, Now, TimeSpan.FromHours(24), TimeSpan.FromHours(1))
            .Select(booking => booking.Id)
            .ToListAsync();

        due.Should().BeEquivalentTo([windowStart.Id, midWindow.Id]);
        due.Should().NotContain([justBefore.Id, windowEnd.Id, dayAway.Id, cancelled.Id, alreadyReminded.Id]);
    }

    [Fact]
    public async Task DueForReminder_ExcludesBookingsForSoftDeletedEvents()
    {
        var booking = await AddBooking(Now.AddHours(24).AddMinutes(5));
        await using (var adminContext = TestDbContextFactory.Create(_databaseName))
        {
            adminContext.Events.Remove(await adminContext.Events.SingleAsync(e => e.Id == booking.EventId));
            await adminContext.SaveChangesAsync();
        }

        var due = await EventReminderDispatcher
            .DueForReminder(_dbContext.Bookings, Now, TimeSpan.FromHours(24), TimeSpan.FromHours(1))
            .ToListAsync();

        due.Should().BeEmpty();
    }

    [Fact]
    public async Task Dispatch_MarksRemindersSentAndNeverSendsTwice()
    {
        var due = await AddBooking(Now.AddHours(24).AddMinutes(15));
        var notDue = await AddBooking(Now.AddHours(48));
        var dispatcher = CreateDispatcher();

        var firstRun = await dispatcher.DispatchDueRemindersAsync(CancellationToken.None);
        var secondRun = await CreateDispatcher().DispatchDueRemindersAsync(CancellationToken.None);

        firstRun.Should().Be(1);
        secondRun.Should().Be(0);
        await using var verification = TestDbContextFactory.Create(_databaseName);
        (await verification.Bookings.SingleAsync(b => b.Id == due.Id)).ReminderSentAtUtc.Should().Be(Now);
        (await verification.Bookings.SingleAsync(b => b.Id == notDue.Id)).ReminderSentAtUtc.Should().BeNull();
        _logger.Entries.Should().ContainSingle(entry => entry.Message.Contains("Reminder dispatched"))
            .Which.Message.Should().Contain(due.TicketNumber);
    }

    [Fact]
    public async Task Dispatch_RespectsBatchSize()
    {
        for (var index = 0; index < 3; index++)
        {
            await AddBooking(Now.AddHours(24).AddMinutes(index));
        }

        var dispatched = await CreateDispatcher(batchSize: 2).DispatchDueRemindersAsync(CancellationToken.None);

        dispatched.Should().Be(2);
        (await _dbContext.Bookings.CountAsync(b => b.ReminderSentAtUtc == null)).Should().Be(1);
    }

    private EventReminderDispatcher CreateDispatcher(int batchSize = 500) =>
        new(
            TestDbContextFactory.Create(_databaseName),
            new FixedTimeProvider(TestDbContextFactory.UtcNow),
            Options.Create(new EventReminderOptions { BatchSize = batchSize }),
            _logger);

    private async Task<Booking> AddBooking(DateTime eventStartUtc, Action<Booking>? configure = null)
    {
        var @event = Event.Create(
            _venue.Id,
            "Concert",
            EventCategory.Concert,
            null,
            eventStartUtc,
            eventStartUtc.AddHours(3),
            Now);
        var booking = Booking.Create(@event.Id, _customer.Id, 5000, $"TKT-{Guid.NewGuid():N}"[..20]);
        booking.Confirm(Now.AddDays(-1));
        configure?.Invoke(booking);

        _dbContext.Events.Add(@event);
        _dbContext.Bookings.Add(booking);
        await _dbContext.SaveChangesAsync();
        return booking;
    }
}
