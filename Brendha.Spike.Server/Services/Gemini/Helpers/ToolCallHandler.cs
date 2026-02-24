using Brendha.Spike.Core.Models.DTOs.Gemini;
using Brendha.Spike.Server.Hubs;
using Brendha.Spike.Server.Interfaces.Helpers;
using Brendha.Spike.Server.Interfaces.Logger;
using Brendha.Spike.Server.Shared;
using Microsoft.AspNetCore.SignalR;
using System.Text.Json;

namespace Brendha.Spike.Server.Services.Gemini.Helpers
{
    public class ToolCallHandler(
        IInterviewLogger interviewLogger,
        ILogger<ToolCallHandler> logger,
        IHubContext<InterviewHub> hubContext) : IToolCallHandler
    {
        public async Task<List<FunctionResponse>> HandleAsync (JsonElement toolCall)
        {
            
            var responses = new List<FunctionResponse>();
            if (!toolCall.TryGetProperty("functionCalls", out var functionCalls)) return responses;

            foreach (var call in functionCalls.EnumerateArray())
            {
                var name = call.GetProperty("name").GetString();
                var id = call.GetProperty("id").GetString();

                if (name == GeminiToolNames.TOOL_NAME)
                {
                    var args = call.GetProperty("args");
                    if (args.TryGetProperty("texto_entendido", out var txtElement))
                    {
                        var text = txtElement.GetString();
                        await interviewLogger.LogAsync("Candidato:", text ?? "");
                        logger.LogInformation($"Candidato: {text}");

                    }
                }
                else if (name == GeminiToolNames.TOOL_NAME_AI_QUESTION)
                {
                    var args = call.GetProperty("args");
                    if (args.TryGetProperty("minha_pergunta", out var questionElement))
                    {
                        var question = questionElement.GetString();
                        await interviewLogger.LogAsync("Brendha (IA)", question ?? "");
                        logger.LogInformation($"Brendha (IA): {question}");

                        if (!string.IsNullOrEmpty(question))
                        {
                            await hubContext.Clients.All.SendAsync("ReceiveAIQuestion", question);
                        }
                    }
                }

                responses.Add(new FunctionResponse(new { result = "ok" }, id!));
            }
            return responses;
           
        }
    }
}
