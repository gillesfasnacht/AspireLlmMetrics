using AspireLlmMetrics.ApiService.Chat;
using AspireLlmMetrics.ApiService.Configuration;
using AspireLlmMetrics.ApiService.Metrics;
using AspireLlmMetrics.ApiService.Telemetry;

using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;
using OllamaSharp.Models.Chat;
using System.Diagnostics;

namespace AspireLlmMetrics.ApiService.Agents
{
    public sealed class ChatAgent
    {
        private readonly AIAgent _agent;
        private readonly LlmMetrics _metrics;
        private readonly ILogger<ChatAgent> _logger;
        private readonly LlmOptions _llmOptions;

        public ChatAgent(
            AIAgent agent,
            LlmMetrics metrics,
            ILogger<ChatAgent> logger,
            IOptions<LlmOptions> options)
        {
            _agent = agent;
            _metrics = metrics;
            _logger = logger;
            _llmOptions = options.Value;
        }

        public async Task<ChatResponse> ChatAsync(
            string prompt,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation(
                "LLM request started. Model={Model}",
                _llmOptions.Model);

            using var activity =
                LlmActivitySource.ActivitySource.StartActivity(
                    "ChatAgent.ChatAsync",
                    ActivityKind.Internal);

            using var agentActivity =
                LlmActivitySource.ActivitySource.StartActivity(
                    "AgentFramework.RunAsync",
                    ActivityKind.Internal);

            var stopwatch = Stopwatch.StartNew();

            try
            {
                var response = await _agent.RunAsync(
                    prompt,
                    cancellationToken: cancellationToken);

                stopwatch.Stop();

                var usage = response.Usage;

                var inputTokens =
                    usage?.InputTokenCount ?? 0;

                var outputTokens =
                    usage?.OutputTokenCount ?? 0;

                var totalTokens =
                    usage?.TotalTokenCount ?? 0;

                var durationMs =
                    stopwatch.Elapsed.TotalMilliseconds;

                var tokensPerSecond =
                    durationMs > 0
                        ? outputTokens / (durationMs / 1000.0)
                        : 0;

                _logger.LogInformation(
                    "LLM response received. InputTokens={InputTokens}, OutputTokens={OutputTokens}, DurationMs={DurationMs}",
                    inputTokens,
                    outputTokens,
                    durationMs);

                activity?.SetTag(
                    "llm.model",
                    "gemma4:12b-it-qat");

                activity?.SetTag(
                    "llm.input_tokens",
                    inputTokens);

                activity?.SetTag(
                    "llm.output_tokens",
                    outputTokens);

                activity?.SetTag(
                    "llm.total_tokens",
                    totalTokens);

                OllamaMetrics? ollamaMetrics = null;

                if (response.RawRepresentation
                    is Microsoft.Extensions.AI.ChatResponse chatResponse)
                {
                    if (chatResponse.RawRepresentation
                        is ChatDoneResponseStream ollamaResponse)
                    {
                        ollamaMetrics = new OllamaMetrics(
                            ollamaResponse.TotalDuration,
                            ollamaResponse.LoadDuration,
                            ollamaResponse.PromptEvalCount,
                            ollamaResponse.PromptEvalDuration,
                            ollamaResponse.EvalCount,
                            ollamaResponse.EvalDuration);
                    }

                    if (ollamaMetrics is not null)
                    {
                        _logger.LogInformation(
                            "Ollama completed. LoadMs={LoadMs}, PromptMs={PromptMs}, GenerationMs={GenerationMs}, GenerationTokensPerSecond={TokensPerSecond}",
                            ollamaMetrics.LoadDurationMs,
                            ollamaMetrics.PromptEvalDurationMs,
                            ollamaMetrics.EvalDurationMs,
                            ollamaMetrics.GenerationTokensPerSecond);

                        activity?.SetTag(
                            "llm.load_duration_ms",
                            ollamaMetrics.LoadDurationMs);

                        activity?.SetTag(
                            "llm.prompt_duration_ms",
                            ollamaMetrics.PromptEvalDurationMs);

                        activity?.SetTag(
                            "llm.generation_duration_ms",
                            ollamaMetrics.EvalDurationMs);

                        activity?.SetTag(
                            "llm.generation_tokens_per_second",
                            ollamaMetrics.GenerationTokensPerSecond);

                        activity?.SetTag(
                            "llm.prompt_tokens_per_second",
                            ollamaMetrics.PromptTokensPerSecond);

                        activity?.SetTag(
                            "llm.cold_start",
                            ollamaMetrics.LoadDurationMs > 100);

                        activity?.SetStatus(
                            ActivityStatusCode.Ok);
                    }
                }

                _metrics.RecordRequest(
                    _llmOptions.Model,
                    inputTokens,
                    outputTokens,
                    totalTokens,
                    durationMs,
                    tokensPerSecond,
                    ollamaMetrics);

                return new ChatResponse(
                    Response: response.Text,
                    Model: _llmOptions.Model,
                    InputTokens: inputTokens,
                    OutputTokens: outputTokens,
                    TotalTokens: totalTokens,
                    DurationMs: durationMs,
                    TokensPerSecond: tokensPerSecond,
                    Ollama: ollamaMetrics);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                _metrics.RecordError(_llmOptions.Model);

                activity?.SetStatus(
                    ActivityStatusCode.Error,
                    ex.Message);

                activity?.AddException(ex);

                _logger.LogError(ex, "LLM request failed. Model={Model}", _llmOptions.Model);

                throw;
            }
        }
    }
}
