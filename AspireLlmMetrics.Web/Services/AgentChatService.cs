using AGUI.Client;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;

namespace AspireLlmMetrics.Web.Services
{
    public sealed class AgentChatService
    {
        private readonly AIAgent _agent;
        private AgentSession? _session;

        public AgentChatService(
            IHttpClientFactory httpClientFactory)
        {
            var httpClient = httpClientFactory.CreateClient("apiservice");

            var aguiClient =
                new AGUIChatClient(
                    new AGUIChatClientOptions(httpClient, "/agents/chat"));

            _agent = aguiClient.AsAIAgent();
        }

        public async IAsyncEnumerable<AgentChatEvent> SendAsync(
            string message,
            [EnumeratorCancellation]
        CancellationToken cancellationToken = default)
        {
            _session ??=
                await _agent.CreateSessionAsync(
                    cancellationToken);

            await foreach (
                AgentResponseUpdate update in
                _agent.RunStreamingAsync(
                    message,
                    _session,
                    cancellationToken: cancellationToken))
            {
                foreach (var content in update.Contents)
                {
                    switch (content)
                    {
                        case TextContent textContent
                            when !string.IsNullOrEmpty(textContent.Text):

                            yield return
                                new AgentTextEvent(textContent.Text);

                            break;

                        case ToolApprovalRequestContent approval:
                            yield return
                                new AgentApprovalEvent(approval);

                            break;
                    }
                }
            }
        }

        public async IAsyncEnumerable<AgentChatEvent> RespondToApprovalAsync(
            ToolApprovalRequestContent request,
            bool approved,
            string? reason = null,
            [EnumeratorCancellation]
        CancellationToken cancellationToken = default)
        {
            if (_session is null)
            {
                throw new InvalidOperationException("Aucune session AG-UI active.");
            }

            var response = request.CreateResponse(approved, reason);

            ChatMessage resumeMessage = new(ChatRole.User, [response]);

            await foreach (
                AgentResponseUpdate update in
                    _agent.RunStreamingAsync([resumeMessage], _session, cancellationToken: cancellationToken))
            {
                foreach (var content in update.Contents)
                {
                    switch (content)
                    {
                        case TextContent textContent
                            when !string.IsNullOrEmpty(textContent.Text):

                            yield return new AgentTextEvent(textContent.Text);

                            break;

                        case ToolApprovalRequestContent approval:
                            yield return  new AgentApprovalEvent(approval);

                            break;
                    }
                }
            }
        }
    }
}
