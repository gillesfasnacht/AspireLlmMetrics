using AspireLlmMetrics.ApiService.Metrics;

namespace AspireLlmMetrics.ApiService.Chat
{
    public sealed record ChatResponse(
        string Response,
        string Model,
        long InputTokens,
        long OutputTokens,
        long TotalTokens,
        double DurationMs,
        double TokensPerSecond,
        OllamaMetrics? Ollama);
}
