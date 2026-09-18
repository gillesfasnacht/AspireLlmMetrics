using AspireLlmMetrics.ApiService.Metrics;
using AspireLlmMetrics.ApiService.Telemetry;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace AspireLlmMetrics.Tests;

public sealed class TelemetryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Successful_request_records_tokens_and_configured_model_once(bool streaming)
    {
        using var capture = new MetricCapture();
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var metrics = new LlmMetrics(services.GetRequiredService<IMeterFactory>());
        using var client = new InstrumentedChatClient(new StubChatClient(), metrics, "test-model");
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == LlmActivitySource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Add
        };
        ActivitySource.AddActivityListener(listener);

        if (streaming)
        {
            var text = "";
            await foreach (var update in client.GetStreamingResponseAsync("Hello")) text += update.Text;
            Assert.Equal("Bonjour", text);
        }
        else
        {
            Assert.Equal("Bonjour", (await client.GetResponseAsync("Hello")).Text);
        }

        Assert.Equal(1, capture.Sum("llm.requests"));
        Assert.Equal(1, capture.Sum("llm.successes"));
        Assert.Equal(0, capture.Sum("llm.errors"));
        Assert.Equal(0, capture.Sum("llm.cancellations"));
        Assert.Equal(3, capture.Sum("llm.tokens.input"));
        Assert.Equal(2, capture.Sum("llm.tokens.output"));
        Assert.Equal(5, capture.Sum("llm.tokens.total"));
        var activity = Assert.Single(activities);
        Assert.Equal("test-model", activity.GetTagItem("llm.model"));
        Assert.Equal(ActivityStatusCode.Ok, activity.Status);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Failed_or_cancelled_request_is_counted_once(bool streaming, bool cancelled)
    {
        using var capture = new MetricCapture();
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        using var cts = new CancellationTokenSource();
        if (cancelled) cts.Cancel();
        Exception failure = cancelled ? new OperationCanceledException(cts.Token) : new InvalidOperationException("Provider failed");
        using var client = new InstrumentedChatClient(new StubChatClient(failure),
            new LlmMetrics(services.GetRequiredService<IMeterFactory>()), "test-model");

        async Task Run()
        {
            if (streaming)
            {
                await foreach (var _ in client.GetStreamingResponseAsync("Hello", cancellationToken: cts.Token)) { }
            }
            else await client.GetResponseAsync("Hello", cancellationToken: cts.Token);
        }

        var actual = await Record.ExceptionAsync(Run);
        Assert.Same(failure, actual);
        Assert.Equal(1, capture.Sum("llm.requests"));
        Assert.Equal(0, capture.Sum("llm.successes"));
        Assert.Equal(cancelled ? 0 : 1, capture.Sum("llm.errors"));
        Assert.Equal(cancelled ? 1 : 0, capture.Sum("llm.cancellations"));
        Assert.Single(capture.Values, x => x.Name == "llm.request.duration");
    }

    [Fact]
    public async Task Abandoned_stream_is_a_cancellation()
    {
        using var capture = new MetricCapture();
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        using var client = new InstrumentedChatClient(new StubChatClient(),
            new LlmMetrics(services.GetRequiredService<IMeterFactory>()), "test-model");
        await foreach (var _ in client.GetStreamingResponseAsync("Hello")) break;
        Assert.Equal(1, capture.Sum("llm.requests"));
        Assert.Equal(1, capture.Sum("llm.cancellations"));
        Assert.Equal(0, capture.Sum("llm.successes"));
        Assert.Equal(0, capture.Sum("llm.errors"));
    }

    [Fact]
    public async Task Provider_timeout_without_caller_cancellation_is_an_error()
    {
        using var capture = new MetricCapture();
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        using var client = new InstrumentedChatClient(new StubChatClient(new OperationCanceledException()),
            new LlmMetrics(services.GetRequiredService<IMeterFactory>()), "test-model");
        await Assert.ThrowsAsync<OperationCanceledException>(() => client.GetResponseAsync("Hello"));
        Assert.Equal(1, capture.Sum("llm.errors"));
        Assert.Equal(0, capture.Sum("llm.cancellations"));
    }
}

internal sealed class StubChatClient(Exception? failure = null) : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (failure is not null) return Task.FromException<ChatResponse>(failure);
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Bonjour"))
        {
            ResponseId = "response-1",
            Usage = new UsageDetails { InputTokenCount = 3, OutputTokenCount = 2 }
        });
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        if (failure is not null) throw failure;
        yield return new ChatResponseUpdate(ChatRole.Assistant, "Bonjour") { MessageId = "message-1", ResponseId = "response-1" };
        yield return new ChatResponseUpdate
        {
            ResponseId = "response-1", MessageId = "message-1", Role = ChatRole.Assistant,
            FinishReason = ChatFinishReason.Stop,
            Contents = [new UsageContent(new UsageDetails { InputTokenCount = 3, OutputTokenCount = 2 })]
        };
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    public void Dispose() { }
}

internal sealed class MetricCapture : IDisposable
{
    private readonly MeterListener _listener = new();
    public ConcurrentQueue<(string Name, double Value)> Values { get; } = new();
    public MetricCapture()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == LlmMetrics.MeterName) listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) => Values.Enqueue((instrument.Name, value)));
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, state) => Values.Enqueue((instrument.Name, value)));
        _listener.Start();
    }
    public double Sum(string name) => Values.Where(x => x.Name == name).Sum(x => x.Value);
    public void Dispose() => _listener.Dispose();
}
