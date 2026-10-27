using TicketHub.Api;
using TicketHub.Application;
using TicketHub.Infrastructure;
using TicketHub.Infrastructure.Persistence.Seeding;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddBackgroundJobs(builder.Configuration)
    .AddApi(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.DisplayRequestDuration();
    });
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

// After authentication, so booking-policy can partition by user.
app.UseRateLimiter();

app.MapControllers();

await app.Services.SeedAdminUserAsync();

await app.RunAsync();

// Exposed for WebApplicationFactory-based integration tests.
public partial class Program;
