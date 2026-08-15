using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Approval.Api.IntegrationTests;

public class ApiSafetyTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ApiSafetyTests(WebApplicationFactory<Program> factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Tasks_WithoutTrustedIdentity_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/v1/tasks");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Submit_WithoutIdempotencyKey_ReturnsBadRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/objects/CHORDR/NO-KEY/submit?companyId=DB_KCC");
        request.Headers.Add("X-Approval-User", "manager");
        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
