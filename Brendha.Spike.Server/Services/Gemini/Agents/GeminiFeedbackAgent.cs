using Brendha.Spike.Core.Models;
using Brendha.Spike.Core.Models.DTOs.Agents;
using Brendha.Spike.Server.Interfaces.Agents;
using Brendha.Spike.Server.Services.Prompt;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Brendha.Spike.Server.Services.Gemini.Agents
{
    public class GeminiFeedbackAgent(IFeedbackGeminiClient geminiClient) : IGeminiFeedbackAgent
    {
        public async Task<FeedbackResultDto> GenerateFeedbackAsync(string logPath, JobPosition jobPosition)
        {
            var interviewLog = await File.ReadAllTextAsync(logPath);
            var prompt = FeedbackPrompt.Build(jobPosition, interviewLog);
            var geminiResponseJson = await geminiClient.GenerateAsync(prompt);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            try
            {
                var feedbackResult = JsonSerializer.Deserialize<FeedbackResultDto>(geminiResponseJson, options);
                return feedbackResult ?? new FeedbackResultDto();
            }
            catch (JsonException ex)
            {
                throw new Exception($"Fail to convert JSON created by IA. IA return: {geminiResponseJson}", ex);
            }
        }
    }
}
