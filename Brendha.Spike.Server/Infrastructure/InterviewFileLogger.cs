using Brendha.Spike.Server.Interfaces.Logger;
using System.Text;

namespace Brendha.Spike.Server.Infrastructure
{
    public class InterviewFileLogger : IInterviewFileLogger
    {
        private readonly string _filePath;
        private readonly StringBuilder _memoryHistory = new();
        private readonly ILogger<InterviewFileLogger> _logger;

        public string LogFilePath => _filePath;

        public InterviewFileLogger(ILogger<InterviewFileLogger> logger, string connectionId)
        {
            _logger = logger;

            string projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../"));
            string logFolder = Path.Combine(projectRoot, "logs_interview");

            if (!Directory.Exists(logFolder)) Directory.CreateDirectory(logFolder);

            var sanitizedConnectionId = connectionId.Replace("/", "_").Replace("=", "");
            _filePath = Path.Combine(logFolder, $"interview_log_{DateTime.Now:yyyyMMdd_HHmmss}_{sanitizedConnectionId}.txt");
            _logger.LogInformation("Interview log created for connection {connectionId} at: {_filePath}", connectionId, _filePath);
        }

        public async Task LogAsync(string role, string message)
        {

            if (string.IsNullOrWhiteSpace(message)) { return; }

            bool isAI = role.Contains("Brendha", StringComparison.OrdinalIgnoreCase) ||
                role.Contains("IA", StringComparison.OrdinalIgnoreCase);


            if (!isAI)
            {
                if (message.StartsWith("**") ||
                    message.StartsWith("I've") ||
                    message.StartsWith("Thinking"))
                {
                    return;
                }
            }

            lock (_memoryHistory)
            {
                _memoryHistory.AppendLine($"{role}: {message}");
            }

            try
            {
                var logLine = $"[{DateTime.Now:HH:mm:ss}] {role}: {message}{Environment.NewLine}";
                await File.AppendAllTextAsync(_filePath, logLine);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error to write in log file: {ex.Message}");
            }
        }


        public string GetHistory()
        {
            lock (_memoryHistory)
            {
                return _memoryHistory.ToString();
            }
        }

        public void FinalizeAndReset()
        {
            lock (_memoryHistory)
            {
                _memoryHistory.Clear();
            }
            _logger.LogInformation("Interview finalized and log history has been reset for the next session.");
        }
    }
}
