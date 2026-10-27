using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace TicketHub.Api;

public sealed class SwaggerTagDescriptionsDocumentFilter : IDocumentFilter
{
    private static readonly OpenApiTag[] TagDescriptions =
    [
        new() { Name = "Auth", Description = "Customer registration and authentication using JWT access tokens." },
        new() { Name = "Venues", Description = "Administrative venue, section, row, and seat layout management." },
        new() { Name = "Events", Description = "Event creation, public catalog search, and seat-map availability." },
        new() { Name = "Holds", Description = "Customer seat reservations with a 10-minute expiration window." },
        new() { Name = "Bookings", Description = "Checkout, customer booking history, and eligible cancellations." },
        new() { Name = "Reports", Description = "Administrative revenue, occupancy, and sales reporting." },
        new() { Name = "Health", Description = "Service liveness and dependency readiness probes." }
    ];

    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        swaggerDoc.Tags = TagDescriptions
            .Select(tag => new OpenApiTag { Name = tag.Name, Description = tag.Description })
            .ToList();
    }
}
