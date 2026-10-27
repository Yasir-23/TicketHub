using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using IPNetwork = Microsoft.AspNetCore.HttpOverrides.IPNetwork;

namespace TicketHub.Api.Networking;

/// <summary>
/// Honors X-Forwarded-For / X-Forwarded-Proto from trusted reverse proxies, so per-IP rate limiting
/// (auth-policy) sees the real client address. Only proxies listed under "ForwardedHeaders" are trusted
/// (loopback by default): trusting every sender would let any client spoof its IP and dodge rate limits.
/// </summary>
public static class ForwardedHeadersExtensions
{
    public const string SectionName = "ForwardedHeaders";

    public static IServiceCollection AddTicketHubForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        var knownProxies = section.GetSection("KnownProxies").Get<string[]>() ?? [];
        var knownNetworks = section.GetSection("KnownNetworks").Get<string[]>() ?? [];

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // One trusted hop: only the address appended by our own proxy is used.
            options.ForwardLimit = 1;

            foreach (var proxy in knownProxies)
            {
                options.KnownProxies.Add(IPAddress.Parse(proxy));
            }

            foreach (var network in knownNetworks)
            {
                options.KnownNetworks.Add(ParseNetwork(network));
            }
        });

        return services;
    }

    /// <summary>Parses CIDR notation such as <c>172.16.0.0/12</c>.</summary>
    internal static IPNetwork ParseNetwork(string cidr)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var prefix) || !int.TryParse(parts[1], out var length))
        {
            throw new FormatException($"'{cidr}' is not a valid CIDR network (expected e.g. 172.16.0.0/12).");
        }

        return new IPNetwork(prefix, length);
    }
}
