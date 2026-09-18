using AspireLlmMetrics.Web.Components;
using AspireLlmMetrics.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Aspire
builder.AddServiceDefaults();

// Blazor
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ApiService
builder.Services.AddHttpClient(
    "apiservice",
    client =>
    {
        client.BaseAddress =
            new Uri("https+http://apiservice");
    });

builder.Services.AddScoped<AgentChatService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Aspire health endpoints
app.MapDefaultEndpoints();

app.Run();
