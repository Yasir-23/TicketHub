using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using TicketHub.Api.Middleware;
using TicketHub.Api.Networking;
using TicketHub.Api.Observability;
using TicketHub.Api.RateLimiting;
using TicketHub.Infrastructure.Authentication;

namespace TicketHub.Api;

public static class DependencyInjection
{
    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration configuration)
    {
        // Enums travel as names ("Concert", "Available"), matching docs/API.md.
        services.AddControllers()
            .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                context.ProblemDetails.Instance ??= context.HttpContext.Request.Path;
                context.ProblemDetails.Extensions.TryAdd(
                    "traceId",
                    Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);
            };
        });
        services.AddExceptionHandler<GlobalExceptionHandler>();

        services.AddJwtAuthentication();
        services.AddAuthorization();
        services.AddTicketHubRateLimiting(configuration);
        services.AddTicketHubForwardedHeaders(configuration);
        services.AddTicketHubHealthChecks(configuration);

        services.AddEndpointsApiExplorer();
        services.AddSwaggerWithBearerAuth();

        return services;
    }

    private static IServiceCollection AddJwtAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // Configure from the validated JwtOptions so token issuance and validation share one source of truth.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearerOptions, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;

                bearerOptions.MapInboundClaims = false;
                bearerOptions.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = jwt.CreateSigningKey(),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = JwtRegisteredClaimNames.Sub,
                    RoleClaimType = JwtTokenService.RoleClaimType
                };
            });

        return services;
    }

    private static IServiceCollection AddSwaggerWithBearerAuth(this IServiceCollection services)
    {
        const string schemeId = "Bearer";

        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "TicketHub Enterprise Ticketing API",
                Version = "v1",
                Description = "High-concurrency Event Ticket Booking System featuring zero double-booking guarantees, 10-minute hold engines, resilient payments, and real-time executive reporting.",
                Contact = new OpenApiContact { Name = "TicketHub Engineering Team" }
            });

            var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            if (File.Exists(xmlPath))
            {
                options.IncludeXmlComments(xmlPath);
            }

            options.TagActionsBy(description =>
            {
                if (description.RelativePath?.Contains("/holds", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return ["Holds"];
                }

                if (description.RelativePath?.StartsWith("health", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return ["Health"];
                }

                var controller = description.ActionDescriptor.RouteValues["controller"];
                return [controller ?? "API"];
            });
            options.DocumentFilter<SwaggerTagDescriptionsDocumentFilter>();

            options.AddSecurityDefinition(schemeId, new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Description = "Paste the access token returned by POST /api/v1/auth/login.",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT"
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = schemeId }
                }] = Array.Empty<string>()
            });
        });

        return services;
    }
}
