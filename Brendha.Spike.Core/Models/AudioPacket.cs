using System;
using System.Collections.Generic;
using System.Text;

namespace Brendha.Interview.Core.Models
{
    public class AudioPacket
    {
        public byte[] Payload { get; set; } = Array.Empty<byte>();
        public long Timestamp { get; set; }
        public string SessionId { get; set; } = string.Empty;
    }
}
