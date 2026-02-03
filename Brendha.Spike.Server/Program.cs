using Brendha.Spike.Server.Hubs;
using Brendha.Spike.Server.Infrastructure;
using Brendha.Spike.Server.Services;
using Brendha.Spike.Server.Workers;
using DotNetEnv;
using Microsoft.AspNetCore.SignalR;

Env.Load();
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<AudioStreamBuffer>();
builder.Services.AddSingleton<GeminiLiveService>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
    ?? config["Gemini:ApiKey"];

    if (string.IsNullOrEmpty(apiKey))
    {
        throw new Exception("Gemini API Key is not configured.");
    }

    var logger = sp.GetRequiredService<ILogger<GeminiLiveService>>();
    var hubContext = sp.GetRequiredService<IHubContext<InterviewHub>>();

    return new GeminiLiveService(apiKey, logger, hubContext);
});

builder.Services.AddControllers();
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
