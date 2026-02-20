using Brendha.Spike.Server.Interfaces;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Brendha.Spike.Server.Services
{
    public class ResumeProcessingService (
        ILogger<ResumeProcessingService> logger,
        IResumePdfAnalyzer resumePdfAnalyzer)
    {

        public async Task<string> ProcessPdfAsync(Stream fileStream, string fileName)
        {
            var rawText = await resumePdfAnalyzer.ExtractTextFromPdf(fileStream, fileName);
            if (string.IsNullOrWhiteSpace(rawText))
            {
                logger.LogWarning("ImgAnalyzer returned empty");
                return null!;
            }

            return rawText;
        }
        public Task<string> ProcessResumeText(string resumeText)
        {
            if (string.IsNullOrWhiteSpace(resumeText))
            {
                logger.LogWarning("Resume text is empty");
            }
            return Task.FromResult(resumeText);
        }
    }
}
