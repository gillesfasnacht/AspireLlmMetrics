using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AspireLlmMetrics.ApiService.Metrics
{
    public sealed class LlmMetrics
    {
        public const string MeterName = "AspireLlmMetrics.Llm";

        private readonly Counter<long> _requests;
        private readonly Counter<long> _successes;
        private readonly Counter<long> _cancellations;
        private readonly Counter<long> _inputTokens;
        private readonly Counter<long> _outputTokens;
        private readonly Counter<long> _totalTokens;
        private readonly Counter<long> _coldStarts;
        private readonly Counter<long> _errors;

        private readonly Histogram<double> _requestDuration;
        private readonly Histogram<double> _loadDuration;
        private readonly Histogram<double> _promptDuration;
        private readonly Histogram<double> _generationDuration;
        private readonly Histogram<double> _promptTokensPerSecond;
        private readonly Histogram<double> _generationTokensPerSecond;
        private readonly Histogram<double> _endToEndTokensPerSecond;

        public LlmMetrics(IMeterFactory meterFactory)
        {
            var meter = meterFactory.Create(MeterName);

            _requests = meter.CreateCounter<long>(
                "llm.requests",
                description: "Nombre de tentatives LLM, tous résultats confondus.");

            _successes = meter.CreateCounter<long>("llm.successes",
                description: "Nombre de requêtes LLM terminées avec succès.");
            _cancellations = meter.CreateCounter<long>("llm.cancellations",
                description: "Nombre de requêtes LLM annulées.");

            _inputTokens = meter.CreateCounter<long>(
                "llm.tokens.input",
                description: "Nombre de tokens en entrée.");

            _outputTokens = meter.CreateCounter<long>(
                "llm.tokens.output",
                description: "Nombre de tokens générés.");

            _totalTokens = meter.CreateCounter<long>(
                "llm.tokens.total",
                description: "Nombre total de tokens.");

            _coldStarts = meter.CreateCounter<long>(
                "llm.cold_starts",
                description: "Nombre de requêtes ayant nécessité un chargement du modèle.");

            _errors = meter.CreateCounter<long>(
                "llm.errors",
                description: "Nombre d'erreurs lors des requêtes LLM.");

            _requestDuration = meter.CreateHistogram<double>(
                "llm.request.duration",
                unit: "s",
                description: "Durée totale de la requête LLM.");

            _loadDuration = meter.CreateHistogram<double>(
                "llm.model.load.duration",
                unit: "s",
                description: "Durée de chargement du modèle.");

            _promptDuration = meter.CreateHistogram<double>(
                "llm.prompt.duration",
                unit: "s",
                description: "Durée d'évaluation du prompt.");

            _generationDuration = meter.CreateHistogram<double>(
                "llm.generation.duration",
                unit: "s",
                description: "Durée de génération de la réponse.");

            _promptTokensPerSecond = meter.CreateHistogram<double>(
                "llm.prompt.tokens_per_second",
                unit: "tokens/s",
                description: "Débit de traitement du prompt.");

            _generationTokensPerSecond = meter.CreateHistogram<double>(
                "llm.generation.tokens_per_second",
                unit: "tokens/s",
                description: "Débit réel de génération.");

            _endToEndTokensPerSecond = meter.CreateHistogram<double>(
                "llm.request.tokens_per_second",
                unit: "tokens/s",
                description: "Débit de tokens observé de bout en bout.");
        }

        public void RecordAttempt(string model) =>
            _requests.Add(1, new KeyValuePair<string, object?>("llm.model", model));

        public void RecordSuccess(
            string model,
            long inputTokens,
            long outputTokens,
            long totalTokens,
            double durationMs,
            double tokensPerSecond,
            OllamaMetrics? ollama)
        {
            var tags = new TagList
            {
                { "llm.model", model }
            };

            _successes.Add(1, tags);

            _inputTokens.Add(inputTokens, tags);
            _outputTokens.Add(outputTokens, tags);
            _totalTokens.Add(totalTokens, tags);

            RecordDuration(model, durationMs, "success");

            _endToEndTokensPerSecond.Record(
                tokensPerSecond,
                tags);

            if (ollama is null)
            {
                return;
            }

            _loadDuration.Record(
                ollama.LoadDurationNs / 1_000_000_000.0,
                tags);

            _promptDuration.Record(
                ollama.PromptEvalDurationNs / 1_000_000_000.0,
                tags);

            _generationDuration.Record(
                ollama.EvalDurationNs / 1_000_000_000.0,
                tags);

            _promptTokensPerSecond.Record(
                ollama.PromptTokensPerSecond,
                tags);

            _generationTokensPerSecond.Record(
                ollama.GenerationTokensPerSecond,
                tags);

            // Heuristique : un chargement > 100 ms est considéré
            // comme un cold start.
            if (ollama.LoadDurationMs > 100)
            {
                _coldStarts.Add(1, tags);
            }
        }

        public void RecordCancellation(string model, double durationMs)
        {
            _cancellations.Add(1, new KeyValuePair<string, object?>("llm.model", model));
            RecordDuration(model, durationMs, "cancelled");
        }

        private void RecordDuration(string model, double durationMs, string outcome)
        {
            var tags = new TagList { { "llm.model", model }, { "llm.outcome", outcome } };
            _requestDuration.Record(durationMs / 1000.0, tags);
        }

        public void RecordError(string model, double durationMs)
        {
            var tags = new TagList
            {
                { "llm.model", model }
            };

            _errors.Add(1, tags);
            RecordDuration(model, durationMs, "error");
        }

    }
}
