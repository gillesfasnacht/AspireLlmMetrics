namespace AspireLlmMetrics.ApiService.Metrics
{
    public sealed record OllamaMetrics(
    long TotalDurationNs,
    long LoadDurationNs,
    long PromptEvalCount,
    long PromptEvalDurationNs,
    long EvalCount,
    long EvalDurationNs)
    {
        public double TotalDurationMs =>
            TotalDurationNs / 1_000_000.0;

        public double LoadDurationMs =>
            LoadDurationNs / 1_000_000.0;

        public double PromptEvalDurationMs =>
            PromptEvalDurationNs / 1_000_000.0;

        public double EvalDurationMs =>
            EvalDurationNs / 1_000_000.0;

        public double GenerationTokensPerSecond =>
            EvalDurationNs > 0
                ? EvalCount / (EvalDurationNs / 1_000_000_000.0)
                : 0;

        public double PromptTokensPerSecond =>
            PromptEvalDurationNs > 0
                ? PromptEvalCount /
                  (PromptEvalDurationNs / 1_000_000_000.0)
                : 0;
    }
}
