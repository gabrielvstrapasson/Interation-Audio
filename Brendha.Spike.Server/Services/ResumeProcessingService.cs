using System.Net.Http.Headers;
using System.Text.Json;

namespace Brendha.Spike.Server.Services
{
    public class ResumeProcessingService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<ResumeProcessingService> _logger;
        private const string IMG_ANALYZER_URL = "https://api.imganalyzer.bne.com.br/api/v1/Files/extract-text";

        public ResumeProcessingService(IHttpClientFactory httpClientFactory, ILogger<ResumeProcessingService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<string> ProcessPdfAsync(Stream fileStream, string fileNmae)
        {
            var rawText = await ExtractTextFromPdf(fileStream, fileNmae);
            if (string.IsNullOrWhiteSpace(rawText))
            {
                _logger.LogWarning("ImgAnalyzer returned empty");
                return null;
            }

            return rawText;
        }
        public Task<string> ProcessResumeText(string resumeText)
        {
            if (string.IsNullOrWhiteSpace(resumeText))
            {
                _logger.LogWarning("Resume text is empty");
            }
            return Task.FromResult(resumeText);
        }

        private async Task<string> ExtractTextFromPdf(Stream stream, string fileName)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                using var content = new MultipartFormDataContent();

                if (stream.Position < 0) stream.Position = 0;
                using var fileContent = new StreamContent(stream);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
                content.Add(fileContent, "file", fileName);

                var response = await client.PostAsync(IMG_ANALYZER_URL, content);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(json);
                var entities = doc.RootElement.GetProperty("result").GetProperty("entities");

                foreach (var entity in entities.EnumerateArray())
                {
                    if (entity.GetProperty("name").GetString() == "fileText")
                    {
                        if (entity.TryGetProperty("value", out var value))
                        {
                            return value.GetString();
                        }
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erro ImgAnalyzer: {ex.Message}");
                return null;
            }
        }


    }
}
