using System.Diagnostics;

namespace AspireLlmMetrics.ApiService.Telemetry
{
    public static class LlmActivitySource
    {
        public const string Name = "AspireLlmMetrics.Llm";

        public static readonly ActivitySource ActivitySource = new(Name);
    }
}
