namespace Brendha.Spike.Server.Interfaces
{
    public interface IResumePdfAnalyzer
    {
        Task<string> ExtractTextFromPdf(Stream stream, string fileName);
    }
}
