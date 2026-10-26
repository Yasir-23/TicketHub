using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace TicketHub.Api.Extensions;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Reads the user id from the validated JWT "sub" claim (inbound claim mapping is disabled).
    /// </summary>
    public static bool TryGetUserId(this ClaimsPrincipal principal, out Guid userId) =>
        Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out userId);
}
