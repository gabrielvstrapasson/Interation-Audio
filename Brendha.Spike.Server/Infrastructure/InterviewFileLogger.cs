using Brendha.Spike.Server.Interfaces.Logger;
using Microsoft.AspNetCore.Components.Web;
using System.Runtime.CompilerServices;
using System.Text;
using static System.Net.Mime.MediaTypeNames;

namespace Brendha.Spike.Server.Infrastructure
{
    public class InterviewFileLogger : IInterviewLogger
    {
        private readonly string _filePath;
        private readonly StringBuilder _memoryHistory = new();
        private readonly ILogger<InterviewFileLogger> _logger;

        public InterviewFileLogger(ILogger<InterviewFileLogger> logger)
        {
            _logger = logger;

            string projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../"));
            string logFolder = Path.Combine(projectRoot, "logs_interview");

            if (!Directory.Exists(logFolder)) Directory.CreateDirectory(logFolder);

            _filePath = Path.Combine(logFolder, $"interview_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            _logger.LogInformation("Interview created at: {_filePath}", _filePath);
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
    }
}
