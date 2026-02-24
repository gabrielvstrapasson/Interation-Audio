namespace Brendha.Spike.Server.Interfaces.Resume
{
    public interface IResumePdfAnalyzer
    {
        Task<string> ExtractTextFromPdf(Stream stream, string fileName);
    }
}
