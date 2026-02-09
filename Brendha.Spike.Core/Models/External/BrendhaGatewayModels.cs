using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Brendha.Spike.Core.Models.External
{
    public class BrendhaGatewayModels
    {
        public class BrendhaRequest
        {
            public ChatInfo Chat { get; set; }
            public CustomizationInfo Customization { get; set; }
            public ChatOptionsInfo ChatOptions { get; set; }
        }

        public class ChatInfo { public string Title { get; set; } }
        public class CustomizationInfo { public List<TemplateInfo> Templates { get; set; } }
        public class TemplateInfo
        {
            public bool IsSingleUsePromptInChat { get; set; }
            public string Name { get; set; }
            public Dictionary<string, string> Params { get; set; }
        }

        public class ChatOptionsInfo {  public bool IsChat { get; set; } public bool SaveHistory { get; set; } }
        public class BrendhaResponse { public bool Success { get; set; } public string MessageText { get; set; } }
        public class ImgAnalyzerResponse { public ImgAnalyzerResult Result { get; set; } }
        public class ImgAnalyzerResult { public List<ImgAnalyzerEntity> Entities { get; set; } }
        public class ImgAnalyzerEntity { public string Name { get; set; } public string Value { get; set; } }   


    }
}
