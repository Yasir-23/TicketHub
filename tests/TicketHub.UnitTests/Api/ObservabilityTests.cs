using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TicketHub.Api.Networking;
using TicketHubForwardedHeaders = TicketHub.Api.Networking.ForwardedHeadersExtensions;
using TicketHub.Api.Observability;
using TicketHub.UnitTests.TestSupport;

namespace TicketHub.UnitTests.Api;

public sealed class ObservabilityTests(TicketHubApiFactory factory) : IClassFixture<TicketHubApiFactory>
{
    // ---- correlation IDs ----

    [Fact]
    public async Task CorrelationId_EchoesWellFormedCallerValue()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "order-42.retry_1");

        var response = await client.SendAsync(request);

        response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single().Should().Be("order-42.retry_1");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("bad value\r\nInjected: header")]
    [InlineData("{json}")]
    public async Task CorrelationId_GeneratesTraceIdWhenMissingOrMalformed(string? supplied)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        if (supplied is not null)
        {
            request.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, supplied);
        }

        var response = await client.SendAsync(request);

        response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single()
            .Should().MatchRegex("^[0-9a-f]{32}$", "it falls back to the W3C trace ID");
    }

    [Theory]
    [InlineData("abc-123", true)]
    [InlineData("A.B_C-9", true)]
    [InlineData("", false)]
    [InlineData("has space", false)]
    [InlineData("semi;colon", false)]
    public void CorrelationId_Validation(string value, bool expected)
    {
        CorrelationIdMiddleware.IsWellFormed(value).Should().Be(expected);
        CorrelationIdMiddleware.IsWellFormed(new string('a', 65)).Should().BeFalse();
    }

    [Fact]
    public void ConsoleTemplate_IncludesTimestampLevelCorrelationIdAndMessage()
    {
        LoggingExtensions.ConsoleOutputTemplate.Should()
            .Contain("{Timestamp:").And.Contain("{Level:u3}").And.Contain("{CorrelationId}").And.Contain("{Message:lj}");
    }

    // ---- forwarded headers ----

    [Fact]
    public async Task ForwardedHeaders_FromTrustedProxy_RewriteClientIp()
    {
        var context = await RunForwardedHeaders(remoteIp: "172.18.0.5", forwardedFor: "203.0.113.7", knownNetworks: ["172.16.0.0/12"]);

        context.Connection.RemoteIpAddress.Should().Be(IPAddress.Parse("203.0.113.7"));
    }

    [Fact]
    public async Task ForwardedHeaders_FromUntrustedSender_AreIgnored()
    {
        // A client cannot spoof its address (and dodge per-IP rate limits) by sending X-Forwarded-For itself.
        var context = await RunForwardedHeaders(remoteIp: "198.51.100.9", forwardedFor: "203.0.113.7", knownNetworks: ["172.16.0.0/12"]);

        context.Connection.RemoteIpAddress.Should().Be(IPAddress.Parse("198.51.100.9"));
    }

    [Fact]
    public async Task ForwardedHeaders_TrustOnlyLoopbackByDefault()
    {
        var context = await RunForwardedHeaders(remoteIp: "172.18.0.5", forwardedFor: "203.0.113.7", knownNetworks: []);

        context.Connection.RemoteIpAddress.Should().Be(IPAddress.Parse("172.18.0.5"));
    }

    [Fact]
    public void ForwardedHeaders_BindsProxiesAndNetworksFromConfiguration()
    {
        var options = BuildOptions(knownNetworks: ["10.0.0.0/8"], knownProxies: ["192.0.2.10"]);

        options.ForwardedHeaders.Should().Be(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);
        options.ForwardLimit.Should().Be(1);
        options.KnownProxies.Should().Contain(IPAddress.Parse("192.0.2.10"));
        options.KnownNetworks.Should().Contain(n => n.Prefix.Equals(IPAddress.Parse("10.0.0.0")) && n.PrefixLength == 8);
    }

    [Theory]
    [InlineData("10.0.0.0")]
    [InlineData("not-an-ip/8")]
    [InlineData("10.0.0.0/x")]
    public void ForwardedHeaders_RejectsMalformedNetworks(string cidr)
    {
        var act = () => TicketHubForwardedHeaders.ParseNetwork(cidr);

        act.Should().Throw<FormatException>();
    }

    private static ForwardedHeadersOptions BuildOptions(string[] knownNetworks, string[]? knownProxies = null)
    {
        var settings = new Dictionary<string, string?>();
        for (var index = 0; index < knownNetworks.Length; index++)
        {
            settings[$"ForwardedHeaders:KnownNetworks:{index}"] = knownNetworks[index];
        }

        for (var index = 0; index < (knownProxies?.Length ?? 0); index++)
        {
            settings[$"ForwardedHeaders:KnownProxies:{index}"] = knownProxies![index];
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection()
            .AddTicketHubForwardedHeaders(configuration)
            .BuildServiceProvider()
            .GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
    }

    private static async Task<HttpContext> RunForwardedHeaders(string remoteIp, string forwardedFor, string[] knownNetworks)
    {
        var options = BuildOptions(knownNetworks);
        var app = new ApplicationBuilder(new ServiceCollection().AddLogging().BuildServiceProvider());
        app.UseForwardedHeaders(options);
        var pipeline = app.Build();

        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
        context.Request.Headers["X-Forwarded-For"] = forwardedFor;
        await pipeline(context);
        return context;
    }
}
