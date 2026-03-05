using Brendha.Interview.Server.Hubs;
using Brendha.Interview.Server.Infrastructure;
using Brendha.Interview.Server.Interfaces.Agents;
using Brendha.Interview.Server.Interfaces.Connection;
using Brendha.Interview.Server.Interfaces.Context;
using Brendha.Interview.Server.Interfaces.Helpers;
using Brendha.Interview.Server.Interfaces.Logger;
using Brendha.Interview.Server.Interfaces.Processing;
using Brendha.Interview.Server.Interfaces.Resume;
using Brendha.Interview.Server.Interfaces.Transport;
using Brendha.Interview.Server.Services.Connection;
using Brendha.Interview.Server.Services.Context;
using Brendha.Interview.Server.Services.Gemini;
using Brendha.Interview.Server.Services.Gemini.Agents;
using Brendha.Interview.Server.Services.Gemini.Helpers;
using Brendha.Interview.Server.Services.Processing;
using Brendha.Interview.Server.Services.Resume;
using Brendha.Interview.Server.Services.Transport;
using Brendha.Interview.Server.Workers;
using DotNetEnv;
using System.Text.Json;

Env.Load();
var builder = WebApplication.CreateBuilder(args);


builder.Services.AddSingleton<AudioStreamBuffer>();
builder.Services.AddSingleton<IResumePdfAnalyzer, ImgAnalyzerPdfService>();
builder.Services.AddSingleton<IGeminiSetupConfigBuilder, GeminiSetupConfigBuilder>();
builder.Services.AddSingleton<IGeminiFeedbackAgent, GeminiFeedbackAgent>();
builder.Services.AddSingleton<IFeedbackGeminiClient, FeedbackGeminiClient>();
builder.Services.AddSingleton<InterviewSessionManager>();
builder.Services.AddSingleton<IInterviewLoggerFactory, InterviewLoggerFactory>();

builder.Services.AddTransient<IWebSocketClient, WebSocketClient>();
builder.Services.AddTransient<IGeminiReconnectionManager, GeminiReconnectionManager>();
builder.Services.AddTransient<IGeminiConnectionManager, GeminiConnectionManager>();
builder.Services.AddTransient<IInterviewContextManager, InterviewContextManager>();
builder.Services.AddTransient<IGeminiMessageProcessor, GeminiMessageProcessor>();
builder.Services.AddTransient<InterviewOrchestratorService>();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });

builder.Services.AddHostedService<AudioProcessorWorker>();
builder.Services.AddOpenApi();

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

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthorization();

app.MapControllers();
app.MapHub<InterviewHub>("/audioHub");
app.Run();
