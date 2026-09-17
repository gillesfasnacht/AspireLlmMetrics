using Microsoft.Extensions.AI;

namespace AspireLlmMetrics.Web.Services
{
    public abstract record AgentChatEvent;

    public sealed record AgentTextEvent(string Text) : AgentChatEvent;

    public sealed record AgentApprovalEvent(ToolApprovalRequestContent Request) : AgentChatEvent;
}
