# AspireLlmMetrics

.NET 10 / Aspire application featuring a Blazor interface, Ollama chat through Agent Framework,
Tempo traces, Loki logs, and Prometheus metrics visualized in Grafana.

## Getting started

Prerequisites: the .NET 10 SDK, Docker running, and Ollama accessible on the host machine.
The default model is `granite4.2:8b`; it must be installed in Ollama.
Configure `LLM:Model` and `LLM:Endpoint` in the API's appsettings files,
or through the `LLM__Model` and `LLM__Endpoint` environment variables.

```powershell
dotnet run --project AspireLlmMetrics.AppHost --launch-profile http
```

Open `webfrontend` from the Aspire dashboard. The AppHost also starts
the API, the OpenTelemetry collector, Loki, Tempo, Prometheus, and Grafana.
The `https` profile requires a trusted .NET development certificate.
Grafana is available at http://localhost:3000 with the existing local configuration.
The **LLM - Requêtes et tokens** dashboard (LLM requests and tokens) is provisioned automatically.
Rates start appearing after several metric exports.

## Chat endpoints

- `POST /chat`: JSON `{ "prompt": "Hello" }`, returning a complete response with metrics.
- `POST /agents/chat`: the streaming AG-UI protocol used by the interface.

Both endpoints use the same instrumented client. Metrics count calls to the LLM
provider: one attempt, followed by a success, an error, or a cancellation.
If a future agent makes multiple LLM calls, each call will be counted separately.
Caller-requested cancellations and abandoned streams are tracked separately from
errors; a provider timeout without caller cancellation is still treated as an error.
Durations are expressed in seconds and include the `llm.outcome` label.
Tokens and throughput are recorded for successfully completed calls.
The cold start counter uses a model loading time greater than 100 ms as a heuristic.

Loki writes to the `loki-data` volume mounted at `/var/loki`. A one-off container sets up the volume permissions for Loki's non-root user (UID 10001).
Existing logs written to `/tmp/loki` are not migrated automatically.
Tempo exports its derived metrics to Prometheus's remote write receiver.

## Verification

```powershell
dotnet build AspireLlmMetrics.slnx
dotnet test AspireLlmMetrics.Tests
```

The tests use a simulated provider: they cover the JSON and AG-UI endpoints
with the frontend client, token counts, errors, and cancellations, without Docker
or an Ollama model. The solution includes the frontend so that its compilation
is checked alongside the other projects.
