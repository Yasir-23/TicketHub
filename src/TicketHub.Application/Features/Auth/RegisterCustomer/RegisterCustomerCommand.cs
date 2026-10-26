using MediatR;
using TicketHub.Application.Common.Interfaces;

namespace TicketHub.Application.Features.Auth.RegisterCustomer;

public sealed record RegisterCustomerCommand(string FullName, string Email, string Password)
    : IRequest<RegisterCustomerResponse>, ISensitiveRequest;

public sealed record RegisterCustomerResponse(Guid UserId, string Email, string Role);
