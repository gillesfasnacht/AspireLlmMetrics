using AspireLlmMetrics.ApiService.Metrics;
using Microsoft.Extensions.AI;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace AspireLlmMetrics.ApiService.Telemetry;

// Instrument the provider once, regardless of whether the caller uses JSON or AG-UI.
public sealed class InstrumentedChatClient(
    IChatClient innerClient,
    LlmMetrics metrics,
    string model) : DelegatingChatClient(innerClient)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        using var request = new RequestTelemetry(metrics, options?.ModelId ?? model);
        try
        {
            var response = await base.GetResponseAsync(messages, options, cancellationToken);
            request.Complete(response.Usage, OllamaMetrics.FromRawRepresentation(response.RawRepresentation));
            return response;
        }
        catch (Exception exception)
        {
            request.Fail(exception, cancellationToken);
            throw;
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var request = new RequestTelemetry(metrics, options?.ModelId ?? model);
        var usage = new UsageDetails();
        OllamaMetrics? ollama = null;
        IAsyncEnumerator<ChatResponseUpdate> enumerator;
        try
        {
            enumerator = base.GetStreamingResponseAsync(messages, options, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
        }
        catch (Exception exception)
        {
            request.Fail(exception, cancellationToken);
            throw;
        }

        await using (enumerator)
        {
            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = await enumerator.MoveNextAsync();
                }
                catch (Exception exception)
                {
                    request.Fail(exception, cancellationToken);
                    throw;
                }

                if (!hasNext)
                {
                    request.Complete(usage, ollama);
                    yield break;
                }

                var update = enumerator.Current;
                foreach (var content in update.Contents.OfType<UsageContent>())
                {
                    usage.Add(content.Details);
                }

                ollama = OllamaMetrics.FromRawRepresentation(update.RawRepresentation) ?? ollama;
                yield return update;
            }
        }
    }

    private sealed class RequestTelemetry : IDisposable
    {
        private readonly LlmMetrics _metrics;
        private readonly string _model;
        private readonly Activity? _activity;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private bool _finished;

        public RequestTelemetry(LlmMetrics metrics, string model)
        {
            _metrics = metrics;
            _model = model;
            _activity = LlmActivitySource.ActivitySource.StartActivity("LLM.Chat", ActivityKind.Client);
            _activity?.SetTag("llm.model", model);
            _metrics.RecordAttempt(model);
        }

        public void Complete(UsageDetails? usage, OllamaMetrics? ollama)
        {
            _stopwatch.Stop();
            var input = usage?.InputTokenCount ?? ollama?.PromptEvalCount ?? 0;
            var output = usage?.OutputTokenCount ?? ollama?.EvalCount ?? 0;
            var total = usage?.TotalTokenCount ?? input + output;
            var durationMs = _stopwatch.Elapsed.TotalMilliseconds;
            var tokensPerSecond = durationMs > 0 ? output / (durationMs / 1000.0) : 0;

            _metrics.RecordSuccess(_model, input, output, total, durationMs, tokensPerSecond, ollama);
            _activity?.SetTag("llm.input_tokens", input);
            _activity?.SetTag("llm.output_tokens", output);
            _activity?.SetTag("llm.total_tokens", total);
            _activity?.SetTag("llm.outcome", "success");
            if (ollama is not null)
            {
                _activity?.SetTag("llm.load_duration_ms", ollama.LoadDurationMs);
                _activity?.SetTag("llm.prompt_duration_ms", ollama.PromptEvalDurationMs);
                _activity?.SetTag("llm.generation_duration_ms", ollama.EvalDurationMs);
                _activity?.SetTag("llm.generation_tokens_per_second", ollama.GenerationTokensPerSecond);
                _activity?.SetTag("llm.prompt_tokens_per_second", ollama.PromptTokensPerSecond);
                _activity?.SetTag("llm.cold_start", ollama.LoadDurationMs > 100);
            }
            _activity?.SetStatus(ActivityStatusCode.Ok);
            _finished = true;
        }

        public void Fail(Exception exception, CancellationToken cancellationToken)
        {
            if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                Cancel();
                return;
            }

            _metrics.RecordError(_model, _stopwatch.Elapsed.TotalMilliseconds);
            _activity?.SetTag("llm.outcome", "error");
            _activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            _activity?.AddException(exception);
            _finished = true;
        }

        private void Cancel()
        {
            _metrics.RecordCancellation(_model, _stopwatch.Elapsed.TotalMilliseconds);
            _activity?.SetTag("llm.outcome", "cancelled");
            _finished = true;
        }

        public void Dispose()
        {
            // Breaking out of a stream before completion is also an abandoned request.
            if (!_finished)
            {
                Cancel();
            }
            _activity?.Dispose();
        }
    }
}
