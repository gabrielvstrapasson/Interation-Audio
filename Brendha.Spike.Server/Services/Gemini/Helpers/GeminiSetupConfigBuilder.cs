using Brendha.Spike.Core.Models;
using Brendha.Spike.Core.Models.DTOs.Gemini;
using Brendha.Spike.Server.Interfaces.Helpers;
using Brendha.Spike.Server.Services.Prompt;
using Brendha.Spike.Server.Shared;

namespace Brendha.Spike.Server.Services.Gemini.Helpers
{
    public class GeminiSetupConfigBuilder : IGeminiSetupConfigBuilder
    {
        public GeminiSetupRequest Build(JobPosition job, string resumeText, string? reconnectionContext = null)
        {
            var systemInstructionText = BrendhaPromptInterview.Build(job, resumeText);

            if (!string.IsNullOrWhiteSpace(reconnectionContext))
            {
                systemInstructionText += "\n " +
                    "History recovery\n" +
                    "Important: continue the interview based on the following history of the conversation. Use it to adapt your next questions and answers, but do not repeat the same information again:\n" +
                    "Do not introduce your self again. " +
                    $"Context recovery: {reconnectionContext}";
            }

            return new GeminiSetupRequest(new GeminiSetup(
                Model: "models/gemini-2.5-flash-native-audio-latest",
                GenerationConfig: new GenerationConfig(
                    ResponseModalities: new[] { "AUDIO" },
                    SpeechConfig: new SpeechConfig(new VoiceConfig(new PrebuiltVoiceConfig("Aoede")))
                    ),
                    SystemInstruction: new SystemInstruction(new[] { new Part(systemInstructionText) }),
                    Tools: new List<Tool>
                    {
                        new Tool(new List<FunctionDeclaration>
                        {
                            new FunctionDeclaration(
                                Name: GeminiToolNames.TOOL_NAME,
                                Description: "Record the candidate's speech.",
                                Parameters: new FunctionParameters(
                                    Type: "OBJECT",
                                    Properties: new Dictionary<string, PropertyDetail>
                                    {
                                        { "texto_entendido", new PropertyDetail("STRING", "Texto exato da fala.") }
                                    },
                                    Required: new[] { "texto_entendido" }
                                )
                            ),

                            new FunctionDeclaration(
                                Name: GeminiToolNames.TOOL_NAME_AI_QUESTION,
                                Description: "Record your own question or statement to the candidate for logging purposes.",
                                Parameters: new FunctionParameters(
                                    Type: "OBJECT",
                                    Properties: new Dictionary<string, PropertyDetail>
                                    {
                                        {"minha_pergunta", new PropertyDetail("STRING", "The exact question or statement you just asked/said to the candidate") }
                                    },
                                    Required: new[] {"minha_pergunta"}
                                    )
                                )
                        })
                    }
                ));
        }
    }
}
