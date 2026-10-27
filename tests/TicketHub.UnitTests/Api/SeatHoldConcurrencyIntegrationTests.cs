using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using TicketHub.Api.Contracts;
using TicketHub.Application.Common.Interfaces;
using TicketHub.Domain.Entities;
using TicketHub.Domain.Enums;
using TicketHub.Infrastructure.BackgroundJobs;
using TicketHub.Infrastructure.Persistence;

namespace TicketHub.UnitTests.Api;

public sealed class SeatHoldConcurrencyIntegrationTests
{
    [Fact]
    public async Task ConcurrentRequestsForSameSeat_CreateOneHoldAndReturnConflictsForTheRest()
    {
        var postgres = await StartPostgresAsync();

        try
        {
            var connectionString =
                $"Host=127.0.0.1;Port={postgres.Port};Database=postgres;Username=postgres;Password=tickethub_test;Timeout=5";
            await WaitForPostgresAsync(connectionString);

            using var factory = new PostgresApiFactory(connectionString);
            using var client = factory.CreateClient();

            Guid eventId;
            Guid seatId;
            List<User> customers;

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await dbContext.Database.MigrateAsync();

                var now = DateTime.UtcNow;
                var venue = Venue.Create("Concurrency Test Venue", "Test City", "1 Test Street");
                var section = venue.AddSection("Main", [new SeatRowSpecification("A", 1)]);
                var @event = Event.Create(
                    venue.Id,
                    "Concurrency Test Event",
                    EventCategory.Concert,
                    "Integration test event",
                    now.AddDays(1),
                    now.AddDays(1).AddHours(2),
                    now);
                @event.SetSectionPrice(section.Id, 10_000, now);

                customers = Enumerable.Range(1, 50)
                    .Select(index => User.CreateCustomer(
                        $"Customer {index}",
                        $"customer-{index}@example.test",
                        "test-password-hash"))
                    .ToList();

                dbContext.Venues.Add(venue);
                dbContext.Events.Add(@event);
                dbContext.Users.AddRange(customers);
                await dbContext.SaveChangesAsync();

                eventId = @event.Id;
                seatId = section.Seats.Single().Id;
            }

            var tokenService = factory.Services.GetRequiredService<IJwtTokenService>();
            var startGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var attempts = customers.Select(async customer =>
            {
                var token = tokenService.CreateAccessToken(customer).Token;
                await startGate.Task;

                using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/events/{eventId}/holds")
                {
                    Content = JsonContent.Create(new HoldSeatsRequest([seatId]))
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                using var response = await client.SendAsync(request);
                return new HoldAttempt(response.StatusCode, await response.Content.ReadAsStringAsync());
            }).ToArray();

            startGate.SetResult(true);
            var results = await Task.WhenAll(attempts);

            Assert.Equal(1, results.Count(result => result.StatusCode == HttpStatusCode.Created));
            var conflicts = results.Where(result => result.StatusCode == HttpStatusCode.Conflict).ToList();
            Assert.Equal(49, conflicts.Count);
            Assert.All(conflicts, conflict =>
            {
                using var problem = System.Text.Json.JsonDocument.Parse(conflict.Body);
                Assert.Equal("Seats unavailable", problem.RootElement.GetProperty("title").GetString());
            });

            await using var verifyScope = factory.Services.CreateAsyncScope();
            var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var persistedHolds = await verifyDbContext.SeatHolds
                .AsNoTracking()
                .Where(hold => hold.EventId == eventId && hold.SeatId == seatId)
                .ToListAsync();

            Assert.Single(persistedHolds);
            Assert.Equal(HoldStatus.Active, persistedHolds[0].Status);
        }
        finally
        {
            await RunDockerAsync("rm", "--force", postgres.ContainerId);
        }
    }

    private static async Task<PostgresContainer> StartPostgresAsync()
    {
        var containerId = await RunDockerAsync(
            "run",
            "--detach",
            "--rm",
            "--publish",
            "127.0.0.1::5432",
            "--env",
            "POSTGRES_USER=postgres",
            "--env",
            "POSTGRES_PASSWORD=tickethub_test",
            "--env",
            "POSTGRES_DB=postgres",
            "postgres:16-alpine");

        try
        {
            var mapping = await RunDockerAsync("port", containerId, "5432/tcp");
            var portText = mapping[(mapping.LastIndexOf(':') + 1)..].Trim();
            var port = int.Parse(portText, CultureInfo.InvariantCulture);
            return new PostgresContainer(containerId, port);
        }
        catch
        {
            await RunDockerAsync("rm", "--force", containerId);
            throw;
        }
    }

    private static async Task WaitForPostgresAsync(string connectionString)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            try
            {
                await connection.OpenAsync();
                return;
            }
            catch (NpgsqlException) when (attempt < 59)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }

        throw new TimeoutException("The PostgreSQL test container did not become ready within 60 seconds.");
    }

    private static async Task<string> RunDockerAsync(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the Docker CLI for the PostgreSQL integration test.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Docker command '{string.Join(' ', arguments)}' failed with exit code {process.ExitCode}: {error}");
        }

        return output.Trim();
    }

    private sealed class PostgresApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
            builder.UseSetting("ConnectionStrings:Redis", string.Empty);
            builder.UseSetting(DatabaseMigrator.ApplyOnStartupKey, "false");
            builder.UseSetting("Jwt:SigningKey", "tickethub-integration-tests-signing-key-0123456789");
            builder.UseSetting("Jwt:Issuer", "TicketHub");
            builder.UseSetting("Jwt:Audience", "TicketHub.Api");

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

                var workers = services
                    .Where(descriptor => descriptor.ServiceType == typeof(IHostedService)
                        && descriptor.ImplementationType?.Namespace == typeof(ExpiredHoldReaperWorker).Namespace)
                    .ToList();
                foreach (var worker in workers)
                {
                    services.Remove(worker);
                }
            });
        }
    }

    private sealed record PostgresContainer(string ContainerId, int Port);

    private sealed record HoldAttempt(HttpStatusCode StatusCode, string Body);
}
