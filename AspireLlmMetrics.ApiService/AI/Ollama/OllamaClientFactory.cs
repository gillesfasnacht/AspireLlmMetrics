using AspireLlmMetrics.ApiService.Configuration;
using Microsoft.Extensions.AI;
using OllamaSharp;

namespace AspireLlmMetrics.ApiService.AI.Ollama
{
    public static class OllamaClientFactory
    {
        public static IChatClient Create(LlmOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            var client = new OllamaApiClient(
                new Uri(options.Endpoint))
            {
                SelectedModel = options.Model
            };

            return client;
        }
    }
}
