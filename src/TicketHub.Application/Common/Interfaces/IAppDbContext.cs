using Microsoft.EntityFrameworkCore;
using TicketHub.Domain.Entities;

namespace TicketHub.Application.Common.Interfaces;

public interface IAppDbContext
{
    DbSet<User> Users { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
