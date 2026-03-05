using Brendha.Interview.Server.Interfaces.Agents;
using System.Text;
using System.Text.Json;

namespace Brendha.Interview.Server.Services.Gemini.Agents
{
    public class FeedbackGeminiClient(
        HttpClient httpClient,
        IConfiguration configuration) : IFeedbackGeminiClient
    {
        private readonly string _geminiRestUrl = configuration["Gemini:RestUrl"]!;
        private readonly string _apiKey = configuration["Gemini:ApiKey"]!;

        public async Task<string> GenerateAsync(string prompt)
        {
            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[] { new { text = prompt } }
                    }
                },
                generationConfig = new
                {
                    responseMimeType = "application/json",
                }
            };

            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var jsonPayload = JsonSerializer.Serialize(requestBody, options);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync($"{_geminiRestUrl}?key={_apiKey}", content);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                throw new Exception($"Gemini REST Error ({response.StatusCode}): {errorBody}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
            {
                var firstCandidate = candidates[0];
                if (firstCandidate.TryGetProperty("content", out var contentElement) &&
                    contentElement.TryGetProperty("parts", out var parts) && parts.GetArrayLength() > 0)
                {
                    return parts[0].GetProperty("text").GetString() ?? "Sem feedback gerado.";
                }
            }

            return "{}";
        }
    }
}