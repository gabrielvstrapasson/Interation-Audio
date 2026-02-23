using Brendha.Spike.Server.Hubs;
using Brendha.Spike.Server.Infrastructure;
using Brendha.Spike.Server.Infrastructure.Interfaces;
using Brendha.Spike.Server.Interfaces;
using Brendha.Spike.Server.Interfaces.Processing;
using Brendha.Spike.Server.Interfaces.Transport;
using Brendha.Spike.Server.Services;
using Brendha.Spike.Server.Services.Context;
using Brendha.Spike.Server.Services.GeminiConnection;
using Brendha.Spike.Server.Services.Interfaces;
using Brendha.Spike.Server.Services.Processing;
using Brendha.Spike.Server.Services.Transport;
using Brendha.Spike.Server.Workers;
using DotNetEnv;
using Microsoft.AspNetCore.SignalR;
using System.Text.Json;

Env.Load();
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<AudioStreamBuffer>();
builder.Services.AddSingleton<IInterviewLogger, InterviewFileLogger>();
builder.Services.AddSingleton<IGeminiReconnectionManager, GeminiReconnectionManager>();
builder.Services.AddSingleton<IResumePdfAnalyzer, ImgAnalyzerPdfService>();
builder.Services.AddSingleton<InterviewContextManager>();
builder.Services.AddSingleton<IWebSocketClient, WebSocketClient>();
builder.Services.AddSingleton<IGeminiMessageProcessor, GeminiMessageProcessor>();

builder.Services.AddSingleton<GeminiLiveService>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? config["Gemini:ApiKey"];

    if (string.IsNullOrEmpty(apiKey)) throw new Exception("API Key is not configured.");

    return new GeminiLiveService(
        apiKey,
        sp.GetRequiredService<ILogger<GeminiLiveService>>(),
        sp.GetRequiredService<IHubContext<InterviewHub>>(),
        sp.GetRequiredService<IInterviewLogger>(),
        sp.GetRequiredService<InterviewContextManager>(),
        sp.GetRequiredService<IGeminiReconnectionManager>(),
        sp.GetRequiredService<IWebSocketClient>(),
        sp.GetRequiredService<IGeminiMessageProcessor>()
    );
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });

builder.Services.AddHostedService<AudioProcessorWorker>();
builder.Services.AddOpenApi();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.WithOrigins("http://localhost:5500", "http://localhost:3000", "http://127.0.0.1:5500")
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials();
    });

});

builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
});

builder.Services.AddHttpClient();
builder.Services.AddScoped<ResumeProcessingService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors("AllowAll");


app.UseAuthorization();

app.MapControllers();
app.MapHub<InterviewHub>("/audioHub");
app.Run();
