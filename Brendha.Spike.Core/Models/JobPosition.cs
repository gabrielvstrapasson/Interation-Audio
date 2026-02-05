using System;
using System.Collections.Generic;
using System.Text;

namespace Brendha.Spike.Core.Models
{
    public class JobPosition
    {
        public string Title { get; set; }
        public string Level { get; set; }
        public string[] MandatorySkills { get; set; }
        public string[] NiceToHaveSkills { get; set; }
        public string Description { get; set; }
        public string SalaryRange { get; set; }
    }
}
