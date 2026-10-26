using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using TicketHub.Domain.Entities;

namespace TicketHub.Application.Common.Interfaces;

public interface IAppDbContext
{
    DbSet<User> Users { get; }

    DbSet<Venue> Venues { get; }

    DbSet<Section> Sections { get; }

    DbSet<Seat> Seats { get; }

    DbSet<Event> Events { get; }

    DbSet<EventSectionPricing> EventSectionPricings { get; }

    DbSet<SeatHold> SeatHolds { get; }

    /// <summary>Used by handlers that need an explicit transaction (e.g. the seat hold hot path).</summary>
    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
