using AspireLlmMetrics.ApiService.Agents;
using AspireLlmMetrics.ApiService.AI.Ollama;
using AspireLlmMetrics.ApiService.Chat;
using AspireLlmMetrics.ApiService.Configuration;
using AspireLlmMetrics.ApiService.Metrics;
using Microsoft.Agents.AI;
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

// Récupération de la configuration LLM
var llmOptions = builder.Configuration.GetSection("LLM")
    .Get<LlmOptions>() ?? throw new InvalidOperationException("La configuration LLM est absente.");

// Ollama / IChatClient
var chatClient = OllamaClientFactory.Create(llmOptions);

builder.Services.AddSingleton<IChatClient>(chatClient);
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

// Chat POST route
app.MapPost("/chat", async (
    ChatRequest request,
    ChatAgent agent,
    CancellationToken cancellationToken) =>
{
    var response = await agent.ChatAsync(
        request.Prompt,
        cancellationToken);

    return Results.Ok(response);
});

app.MapDefaultEndpoints();

app.Run();