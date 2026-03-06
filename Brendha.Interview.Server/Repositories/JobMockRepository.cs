using Brendha.Interview.Core.Models;
using Brendha.Interview.Server.Interfaces.Repositories.Job;

namespace Brendha.Interview.Server.Repositories
{
    public class JobMockRepository : IJobRepository
    {

        private static JobPosition _currentJob = GetDefaultJob();
        public Task<JobPosition?> GetCurrentJobAsync()
        {
            return Task.FromResult<JobPosition?>(_currentJob);
        }

        public Task ResetToDefaultAsync()
        {
            _currentJob = GetDefaultJob();
            return Task.CompletedTask;
        }

        public Task UpdateJobAsync(JobPosition job)
        {
            _currentJob = job ?? GetDefaultJob();
            return Task.CompletedTask;
        }

        private static JobPosition GetDefaultJob() => new JobPosition
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

