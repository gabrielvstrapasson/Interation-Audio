using System;
using System.Collections.Generic;
using System.Text;

namespace Brendha.Spike.Core.Models.DTOs.Agents
{
    public class FeedbackRequestDto
    {
        public string LogPath { get; set; }
        public JobPosition JobPosition { get; set; }
    }
}
