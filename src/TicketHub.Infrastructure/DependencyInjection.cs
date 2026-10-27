using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TicketHub.Application.Common.Interfaces;
using TicketHub.Infrastructure.Authentication;
using TicketHub.Infrastructure.BackgroundJobs;
using TicketHub.Infrastructure.Payments;
using TicketHub.Infrastructure.Persistence;
using TicketHub.Infrastructure.Persistence.Seeding;

namespace TicketHub.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "DefaultConnection";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");

        services.TryAddSingleton(TimeProvider.System);

        services.AddDbContext<AppDbContext>(options => options
            .UseNpgsql(connectionString)
            // Seats/pricings deliberately have no filter of their own: rows under a soft-deleted
            // venue, section or event are meant to drop out of joins, so this warning is expected.
            .ConfigureWarnings(warnings =>
                warnings.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning)));
        services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<ICustomerEventLock, PostgresAdvisoryCustomerEventLock>();
        services.AddHttpClient<IPaymentGateway, SimulatedPaymentGateway>(client =>
            client.BaseAddress = new Uri("https://payments.tickethub.local/"))
            .ConfigurePrimaryHttpMessageHandler(() => new SimulatedPaymentHandler())
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 3;
                options.Retry.Delay = TimeSpan.FromMilliseconds(200);
                options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
                options.Retry.UseJitter = true;
                options.CircuitBreaker.FailureRatio = 0.5;
                options.CircuitBreaker.MinimumThroughput = 10;
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
                options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(15);
            });

        services.Configure<AdminSeedOptions>(configuration.GetSection(AdminSeedOptions.SectionName));

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddSingleton<IPasswordHasher>(new BCryptPasswordHasher());

        return services;
    }

    /// <summary>
    /// SDD section 6: the ticket fulfillment channel plus the hosted workers that drain it, reap expired holds
    /// and send event reminders.
    /// </summary>
    public static IServiceCollection AddBackgroundJobs(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddOptions<ExpiredHoldReaperOptions>()
            .Bind(configuration.GetSection(ExpiredHoldReaperOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<TicketFulfillmentOptions>()
            .Bind(configuration.GetSection(TicketFulfillmentOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<EventReminderOptions>()
            .Bind(configuration.GetSection(EventReminderOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<ITicketFulfillmentChannel, TicketFulfillmentChannel>();
        services.AddScoped<ExpiredHoldReaper>();
        services.AddScoped<EventReminderDispatcher>();

        services.AddHostedService<TicketFulfillmentBackgroundWorker>();
        services.AddHostedService<ExpiredHoldReaperWorker>();
        services.AddHostedService<EventReminderBackgroundWorker>();

        return services;
    }
}
