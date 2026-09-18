using AspireLlmMetrics.ApiService.Agents;
using AspireLlmMetrics.ApiService.AI.Ollama;
using AspireLlmMetrics.ApiService.Chat;
using AspireLlmMetrics.ApiService.Configuration;
using AspireLlmMetrics.ApiService.Metrics;
using AspireLlmMetrics.ApiService.Telemetry;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);

// Ajout des services AG-UI
builder.Services.AddAGUIServer();

// Configuration LLM
builder.Services.AddOptions<LlmOptions>()
    .BindConfiguration("LLM")
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.Model),
        "LLM:Model doit être configuré.")
    .Validate(
        options => Uri.TryCreate(
            options.Endpoint,
            UriKind.Absolute,
            out _),
        "LLM:Endpoint doit être une URL valide.")
    .ValidateOnStart();

// Le même client instrumenté sert les routes JSON et AG-UI.
builder.Services.AddSingleton<IChatClient>(services =>
{
    var options = services.GetRequiredService<IOptions<LlmOptions>>().Value;
    return new InstrumentedChatClient(
        OllamaClientFactory.Create(options),
        services.GetRequiredService<LlmMetrics>(),
        options.Model);
});
builder.Services.AddSingleton<AIAgent>(serviceProvider =>
{
    var client = serviceProvider.GetRequiredService<IChatClient>();
    return client.AsAIAgent();
});

builder.Services.AddSingleton<ChatAgent>();
builder.Services.AddSingleton<LlmMetrics>();

// ASP.NET Core
builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapAGUIServer("/agents/chat", app.Services.GetRequiredService<AIAgent>());

// Chat POST route
app.MapPost("/chat", async (
    ChatRequest request,
    ChatAgent agent,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Prompt))
    {
        return Results.BadRequest(new { error = "Le prompt ne doit pas être vide." });
    }

    var response = await agent.ChatAsync(
        request.Prompt,
        cancellationToken);

    return Results.Ok(response);
});

app.MapDefaultEndpoints();

app.Run();
