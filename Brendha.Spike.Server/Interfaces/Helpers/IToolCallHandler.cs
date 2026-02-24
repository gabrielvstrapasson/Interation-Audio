using Brendha.Spike.Core.Models.DTOs.Gemini;
using System.Text.Json;

namespace Brendha.Spike.Server.Interfaces.Helpers
{
    public interface IToolCallHandler
    {
        Task<List<FunctionResponse>> HandleAsync(JsonElement toolCall);
    }
}
