using System.Diagnostics;
using System.Net;
using AgentTokenStats.Adapters;
using AgentTokenStats.Api;
using AgentTokenStats.Infrastructure;
using AgentTokenStats.Services;

Environment.SetEnvironmentVariable("ASPNETCORE_URLS", null);

var noBrowser = args.Contains("--no-browser", StringComparer.OrdinalIgnoreCase)
    || string.Equals(Environment.GetEnvironmentVariable("AGENTTOKENSTATS_NO_BROWSER"), "1", StringComparison.OrdinalIgnoreCase);

var port = PortFinder.Find();
var url = $"http://127.0.0.1:{port}";

var builder = WebApplication.CreateBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Warning);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(IPAddress.Loopback, port);
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});

builder.Services.AddSingleton<AppConfigStore>();
builder.Services.AddSingleton<OpenCodeDataSource>();
builder.Services.AddSingleton<CodexDataSource>();
builder.Services.AddSingleton<PiDataSource>();
builder.Services.AddSingleton<ClaudeCodeDataSource>();
builder.Services.AddSingleton<IEnumerable<IAgentDataSource>>(sp =>
[
    sp.GetRequiredService<OpenCodeDataSource>(),
    sp.GetRequiredService<CodexDataSource>(),
    sp.GetRequiredService<PiDataSource>(),
    sp.GetRequiredService<ClaudeCodeDataSource>()
]);
builder.Services.AddSingleton<StatsService>();

builder.Environment.WebRootFileProvider = EmbeddedWebRoot.Create(builder.Environment);

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapApi();
app.MapFallback(async context =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    var index = context.RequestServices
        .GetRequiredService<IWebHostEnvironment>()
        .WebRootFileProvider
        .GetFileInfo("index.html");
    if (index.Exists)
    {
        context.Response.ContentType = "text/html; charset=utf-8";
        await using var stream = index.CreateReadStream();
        await stream.CopyToAsync(context.Response.Body);
        return;
    }

    context.Response.ContentType = "text/plain; charset=utf-8";
    await context.Response.WriteAsync("Agent Token Stats API is running. Frontend is not built yet.");
});

app.Lifetime.ApplicationStarted.Register(() =>
{
    app.Logger.LogInformation("Listening on {Url} (loopback only)", url);
    if (noBrowser)
        return;
    try
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Failed to open a browser.");
    }
});

app.Run();
