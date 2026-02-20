using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Brendha.Spike.Core.Models.DTOs.Gemini
{
    public record GeminiSetupRequest(GeminiSetup setup);
    public record GeminiSetup(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("generation_config")] GenerationConfig GenerationConfig,
        [property: JsonPropertyName("system_instruction")] SystemInstruction SystemInstruction,
        [property: JsonPropertyName("tools")] List<Tool>? Tools = null);

    public record GenerationConfig(
        [property: JsonPropertyName("response_modalities")] string[] ResponseModalities,
        [property: JsonPropertyName("speech_config")] SpeechConfig SpeechConfig);

    public record SpeechConfig(
        [property: JsonPropertyName("voice_config")] VoiceConfig VoiceConfig);

    public record VoiceConfig(
            [property: JsonPropertyName("prebuilt_voice_config")] PrebuiltVoiceConfig PrebuiltVoiceConfig );

    public record PrebuiltVoiceConfig(
        [property: JsonPropertyName("voice_name")] string VoiceName );

    public record SystemInstruction(
        [property: JsonPropertyName("parts")] Part[] Parts );

    public record Part(
        [property: JsonPropertyName("text")] string text);

    public record Tool(
        [property: JsonPropertyName("function_declarations")] List<FunctionDeclaration> FunctionDeclarations );

    public record FunctionDeclaration(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("parameters")] FunctionParameters Parameters
    );

    public record FunctionParameters(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("properties")] Dictionary<string, PropertyDetail> Properties,
        [property: JsonPropertyName("required")] string[] Required );

    public record PropertyDetail(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("description")] string Description );

    public record ToolCallResponse(
        [property: JsonPropertyName("tool_response")] ToolResponsePayload ToolResponse );

    public record ToolResponsePayload(
        [property: JsonPropertyName("function_responses")] List<FunctionResponse> FunctionResponses );

    public record FunctionResponse(
        [property: JsonPropertyName("response")] object Response,
        [property: JsonPropertyName("id")] string Id );

    public record RealtimeInputRequest(
        [property: JsonPropertyName("realtime_input")] RealtimeInput RealtimeInput );

    public record RealtimeInput(
        [property: JsonPropertyName("media_chunks")] MediaChunk[] MediaChunks );

    public record MediaChunk(
        [property: JsonPropertyName("mime_type")] string MimeType,
        [property: JsonPropertyName("data")] string Data );
}
