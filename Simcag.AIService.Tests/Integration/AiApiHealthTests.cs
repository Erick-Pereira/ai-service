using System.Net;
using FluentAssertions;

namespace Simcag.AIService.Tests.Integration;

public sealed class AiApiHealthTests : IClassFixture<AiApiTestFactory>
{
    private readonly AiApiTestFactory _factory;

    public AiApiHealthTests(AiApiTestFactory factory) => _factory = factory;

    [Fact]
    public async Task Get_Health_Live_Returns_200()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/live");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Get_Health_Ready_Returns_200()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/ready");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
