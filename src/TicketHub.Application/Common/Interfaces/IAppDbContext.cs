using Microsoft.EntityFrameworkCore;
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

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
