using Brendha.Spike.Server.Hubs;
using Brendha.Spike.Server.Infrastructure;
using Brendha.Spike.Server.Services;
using Brendha.Spike.Server.Workers;
using DotNetEnv;

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

    return new GeminiLiveService(apiKey, logger);
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

// Configure the HTTP request pipeline.
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
