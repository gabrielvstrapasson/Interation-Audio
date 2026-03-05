using System;
using System.Collections.Generic;
using System.Text;

namespace Brendha.Interview.Core.Models
{
    public class JobPosition
    {
        public required string Title { get; set; }
        public required string Level { get; set; }
        public string[]? MandatorySkills { get; set; }
        public string[]? NiceToHaveSkills { get; set; }
        public required string Description { get; set; }
        public required string SalaryRange { get; set; }
        public string[]? Benefits { get; set; }
    }
}
