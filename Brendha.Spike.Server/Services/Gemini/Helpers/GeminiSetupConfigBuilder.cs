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
            var systemInstructionText = InterviewPrompt.Build(job, resumeText);

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
                    InputAudioTranscription: new { },
                    OutputAudioTranscription: new { },
                    Tools: null
                ));
        }
    }
}
