using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class ApiBoundaryTests
{
    [Fact]
    public void Only_sign_in_and_csrf_are_anonymous_api_entry_points()
    {
        using var app = new ApiFactory();
        using var client = app.CreateClient();
        var endpoints = app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/", StringComparison.Ordinal) == true
                && e.RoutePattern.RawText != "/api/{**path}").ToArray();
        Assert.NotEmpty(endpoints);
        foreach (var endpoint in endpoints)
        {
            var anonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
            Assert.Equal(endpoint.RoutePattern.RawText is "/api/auth/login" or "/api/auth/csrf", anonymous);
            if (!anonymous)
                Assert.Contains(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(), data => data.Policy == "Parent");
        }
    }
}
