using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Brendha.Spike.Core.Models.State
{
    public class InterviewSessionState
    {
        public DateTime SessionEndTime { get; set; }
        public string? PendingResumeText { get; set; }
        public string? CurrentResumeText { get; set; }
        public Stopwatch LatencyWatch { get; } = new();
        public bool IsWaitingForFirstResponse { get; set; }
        public bool AiIsSpeaking { get; set; }
        public JobPosition? CurrentJob { get; set; }
    }
}

