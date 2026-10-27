using Serilog;
using TicketHub.Api;
using TicketHub.Api.Observability;
using TicketHub.Application;
using TicketHub.Infrastructure;
using TicketHub.Infrastructure.Persistence;
using TicketHub.Infrastructure.Persistence.Seeding;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, logger) =>
    logger.ConfigureTicketHubLogging(context.Configuration, services));

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddBackgroundJobs(builder.Configuration)
    .AddApi(builder.Configuration);

var app = builder.Build();

// First, so everything downstream (rate limiting, logging, HTTPS redirection) sees the real client.
app.UseForwardedHeaders();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseTicketHubRequestLogging();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.RoutePrefix = "swagger";
        options.DisplayRequestDuration();
    });
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

// After authentication, so booking-policy can partition by user.
app.UseRateLimiter();

app.MapControllers();
app.MapTicketHubHealthChecks();

await app.Services.ApplyDatabaseMigrationsAsync();
await app.Services.SeedAdminUserAsync();

await app.RunAsync();

// Exposed for WebApplicationFactory-based integration tests.
public partial class Program;
