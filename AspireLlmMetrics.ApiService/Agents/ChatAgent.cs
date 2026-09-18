using AspireLlmMetrics.ApiService.Chat;
using AspireLlmMetrics.ApiService.Configuration;
using AspireLlmMetrics.ApiService.Metrics;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace AspireLlmMetrics.ApiService.Agents;

public sealed class ChatAgent(AIAgent agent, IOptions<LlmOptions> options)
{
    public async Task<ChatResponse> ChatAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = await agent.RunAsync(prompt, cancellationToken: cancellationToken);
        stopwatch.Stop();

        var rawResponse = response.RawRepresentation as Microsoft.Extensions.AI.ChatResponse;
        var ollama = OllamaMetrics.FromRawRepresentation(rawResponse?.RawRepresentation);
        var inputTokens = response.Usage?.InputTokenCount ?? ollama?.PromptEvalCount ?? 0;
        var outputTokens = response.Usage?.OutputTokenCount ?? ollama?.EvalCount ?? 0;
        var totalTokens = response.Usage?.TotalTokenCount ?? inputTokens + outputTokens;
        var durationMs = stopwatch.Elapsed.TotalMilliseconds;

        return new ChatResponse(
            Response: response.Text,
            Model: options.Value.Model,
            InputTokens: inputTokens,
            OutputTokens: outputTokens,
            TotalTokens: totalTokens,
            DurationMs: durationMs,
            TokensPerSecond: durationMs > 0 ? outputTokens / (durationMs / 1000.0) : 0,
            Ollama: ollama);
    }
}
