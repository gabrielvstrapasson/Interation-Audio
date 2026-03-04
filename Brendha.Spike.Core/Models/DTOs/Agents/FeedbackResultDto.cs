using System;
using System.Collections.Generic;
using System.Text;

namespace Brendha.Interview.Core.Models.DTOs.Agents
{
    public class FeedbackResultDto
    {
        public int Adherence { get; set; }
        public string Report { get; set; }
        public string Message { get; set; }
        public JustificationDto Justification { get; set; }
    }
    public class JustificationDto
    {
        public string Experience { get; set; }
        public string Skills { get; set; }
        public string Overall { get; set; }
    }
}
