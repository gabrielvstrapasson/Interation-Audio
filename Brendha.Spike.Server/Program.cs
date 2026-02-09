using Brendha.Spike.Server.Hubs;
using Brendha.Spike.Server.Infrastructure;
using Brendha.Spike.Server.Infrastructure.Interfaces;
using Brendha.Spike.Server.Services;
using Brendha.Spike.Server.Workers;
using DotNetEnv;
using Microsoft.AspNetCore.SignalR;
using System.Text.Json;

Env.Load();
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<AudioStreamBuffer>();
builder.Services.AddSingleton<IInterviewLogger, InterviewFileLogger>();

builder.Services.AddSingleton<GeminiLiveService>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? config["Gemini:ApiKey"];

    if (string.IsNullOrEmpty(apiKey)) throw new Exception("API Key is not configured.");

    return new GeminiLiveService(
        apiKey,
        sp.GetRequiredService<ILogger<GeminiLiveService>>(),
        sp.GetRequiredService<IHubContext<InterviewHub>>(),
        sp.GetRequiredService<IInterviewLogger>()
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
        policy.SetIsOriginAllowed(origin => true)
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
