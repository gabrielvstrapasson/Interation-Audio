using Brendha.Spike.Server.Hubs;
using Brendha.Spike.Server.Infrastructure;
using Brendha.Spike.Server.Interfaces.Agents;
using Brendha.Spike.Server.Interfaces.Connection;
using Brendha.Spike.Server.Interfaces.Context;
using Brendha.Spike.Server.Interfaces.Helpers;
using Brendha.Spike.Server.Interfaces.Logger;
using Brendha.Spike.Server.Interfaces.Processing;
using Brendha.Spike.Server.Interfaces.Resume;
using Brendha.Spike.Server.Interfaces.Transport;
using Brendha.Spike.Server.Services.Connection;
using Brendha.Spike.Server.Services.Context;
using Brendha.Spike.Server.Services.Gemini;
using Brendha.Spike.Server.Services.Gemini.Agents;
using Brendha.Spike.Server.Services.Gemini.Helpers;
using Brendha.Spike.Server.Services.Processing;
using Brendha.Spike.Server.Services.Resume;
using Brendha.Spike.Server.Services.Transport;
using Brendha.Spike.Server.Workers;
using DotNetEnv;
using Microsoft.AspNetCore.SignalR;
using System.Text.Json;

Env.Load();
var builder = WebApplication.CreateBuilder(args);


builder.Services.AddSingleton<AudioStreamBuffer>();
builder.Services.AddSingleton<IResumePdfAnalyzer, ImgAnalyzerPdfService>();
builder.Services.AddSingleton<IGeminiSetupConfigBuilder, GeminiSetupConfigBuilder>();
builder.Services.AddSingleton<IGeminiFeedbackAgent, GeminiFeedbackAgent>();
builder.Services.AddSingleton<IFeedbackGeminiClient, FeedbackGeminiClient>();
builder.Services.AddSingleton<InterviewSessionManager>();

builder.Services.AddTransient<IWebSocketClient, WebSocketClient>();
builder.Services.AddTransient<IGeminiReconnectionManager, GeminiReconnectionManager>();
builder.Services.AddTransient<IInterviewFileLogger, InterviewFileLogger>();
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
