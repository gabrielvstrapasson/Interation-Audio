using Brendha.Interview.Server.Interfaces.Resume;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Brendha.Interview.Server.Services.Resume
{
    public class ImgAnalyzerPdfService (
        IHttpClientFactory httpClientFactory,
        ILogger<ImgAnalyzerPdfService> logger) : IResumePdfAnalyzer
    {
        private const string IMG_ANALYZER_URL = "https://api.imganalyzer.bne.com.br/api/v1/Files/extract-text";

        public async Task<string> ExtractTextFromPdf(Stream stream, string fileName)
        {
            try
            {
                var client = httpClientFactory.CreateClient();
                using var content = new MultipartFormDataContent();

                if (stream.Position < 0) stream.Position = 0;
                using var fileContent = new StreamContent(stream);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
                content.Add(fileContent, "file", fileName);

                var response = await client.PostAsync(IMG_ANALYZER_URL, content);
                if (!response.IsSuccessStatusCode) return null!;

                var json = await response.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(json);
                var entities = doc.RootElement.GetProperty("result").GetProperty("entities");

                foreach (var entity in entities.EnumerateArray())
                {
                    if (entity.GetProperty("name").GetString() == "fileText")
                    {
                        if (entity.TryGetProperty("value", out var value))
                        {
                            return value.GetString()!;
                        }
                    }
                }
                return null!;
            }
            catch (Exception ex)
            {
                logger.LogError($"Erro ImgAnalyzer: {ex.Message}");
                return null!;
            }
        }
    }
}
