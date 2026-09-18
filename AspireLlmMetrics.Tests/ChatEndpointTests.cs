using AspireLlmMetrics.ApiService.Metrics;
using AspireLlmMetrics.ApiService.Telemetry;
using AspireLlmMetrics.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace AspireLlmMetrics.Tests;

public sealed class ChatEndpointTests
{
    [Fact]
    public async Task Json_chat_returns_response_and_records_one_request()
    {
        using var capture = new MetricCapture();
        await using var app = new ChatApplication();
        using var client = app.CreateClient();
        using var response = await client.PostAsJsonAsync("/chat", new { prompt = "Hello" });
        response.EnsureSuccessStatusCode();
        var chat = await response.Content.ReadFromJsonAsync<AspireLlmMetrics.ApiService.Chat.ChatResponse>();
        Assert.Equal("Bonjour", chat!.Response);
        Assert.Equal(5, chat.TotalTokens);
        Assert.Equal(1, capture.Sum("llm.requests"));
        Assert.Equal(1, capture.Sum("llm.successes"));
    }

    [Fact]
    public async Task Agui_endpoint_works_with_the_actual_frontend_client()
    {
        using var capture = new MetricCapture();
        await using var app = new ChatApplication();
        using var client = app.CreateClient();
        var service = new AgentChatService(new TestClientFactory(client));
        var text = "";
        await foreach (var item in service.SendAsync("Hello"))
            if (item is AgentTextEvent chunk) text += chunk.Text;
        Assert.Equal("Bonjour", text);
        Assert.Equal(1, capture.Sum("llm.requests"));
        Assert.Equal(1, capture.Sum("llm.successes"));
        Assert.Equal(2, capture.Sum("llm.tokens.output"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Empty_prompt_is_rejected_before_calling_provider(string? prompt)
    {
        using var capture = new MetricCapture();
        await using var app = new ChatApplication();
        using var client = app.CreateClient();
        using var response = await client.PostAsJsonAsync("/chat", new { prompt });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, capture.Sum("llm.requests"));
    }

    private sealed class TestClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class ChatApplication : WebApplicationFactory<AspireLlmMetrics.ApiService.Agents.ChatAgent>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IChatClient>();
                services.AddSingleton<IChatClient>(provider => new InstrumentedChatClient(
                    new StubChatClient(), provider.GetRequiredService<LlmMetrics>(), "test-model"));
            });
        }
    }
}
