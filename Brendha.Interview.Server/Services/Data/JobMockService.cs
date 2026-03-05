using Brendha.Interview.Core.Models;
using System.Runtime.CompilerServices;

namespace Brendha.Interview.Server.Services.Data
{
    public class JobMockService
    {
        private static JobPosition _currentJob = GetDefaultJob();
        public static JobPosition GetCurrentJob() => _currentJob;
        public static void UpdateJob(JobPosition job)
        {
            _currentJob = job ?? GetDefaultJob();
        }

        public static void ResetToDefault()
        {
            _currentJob = GetDefaultJob();
        }


        private static JobPosition GetDefaultJob()
        {
            return new JobPosition
            {
                Title = "Desenvolvedor .NET Backend Júnior",
                Level = "Júnior",
                Description = "Você atuará no desenvolvimento e manutenção de APIs REST, corrigindo bugs e implementando novas features com supervisão. Ótima oportunidade para aprender sobre microsserviços e sistemas financeiros.",
                SalaryRange = "R$ 4.000 - R$ 6.000 (PJ)",
                MandatorySkills = new[]
                {
                    ".NET 8",
                    "C# (Fundamentos e OO)",
                    "REST APIs",
                    "SQL Server",
                    "Entity Framework Core",
                    "Git"
                },
                NiceToHaveSkills = new[]
                {
                    "Docker",
                    "Noções de Azure",
                    "Testes Unitários (xUnit)",
                    "Conhecimento em SOLID"
                },
                Benefits = new[]
                {
                    "Home Office",
                    "Horário Flexível",
                    "Plano de Saúde",
                    "Vale Refeição",
                    "Mentoria Técnica"
                }
            };
        }
    }
}