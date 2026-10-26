using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketHub.Application.Common.Exceptions;
using TicketHub.Application.Common.Interfaces;
using TicketHub.Domain.Entities;

namespace TicketHub.Application.Features.Auth.RegisterCustomer;

public sealed class RegisterCustomerCommandHandler(IAppDbContext dbContext, IPasswordHasher passwordHasher)
    : IRequestHandler<RegisterCustomerCommand, RegisterCustomerResponse>
{
    public async Task<RegisterCustomerResponse> Handle(
        RegisterCustomerCommand request,
        CancellationToken cancellationToken)
    {
        var email = User.NormalizeEmail(request.Email);

        if (await dbContext.Users.AnyAsync(u => u.Email == email, cancellationToken))
        {
            throw new ConflictException("An account with this email already exists.");
        }

        var user = User.CreateCustomer(request.FullName, email, passwordHasher.Hash(request.Password));

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RegisterCustomerResponse(user.Id, user.Email, user.Role.ToString());
    }
}
