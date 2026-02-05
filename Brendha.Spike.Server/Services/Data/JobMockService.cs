using Brendha.Spike.Core.Models;

namespace Brendha.Spike.Server.Services.Data
{
    public class JobMockService
    {
        public static JobPosition GetCurrentJob()
        {
            return new JobPosition
            {
                Title = "Desenvolvedor .NET Backend",
                Level = "Sênior",
                Description = "Você atuará na modernização de um legado monolítico para microsserviços. O sistema é crítico, financeiro e exige alta resiliência.",
                SalaryRange = "R$ 18.000 - R$ 22.000 (PJ)",
                MandatorySkills = new[]
                   {
                    ".NET 8",
                    "C# Avançado",
                    "Azure Service Bus",
                    "Clean Architecture",
                    "SQL Server",
                    "Entity Framework Core"
                },
                NiceToHaveSkills = new[]
                   {
                    "Kubernetes (AKS)",
                    "OpenTelemetry",
                    "Redis"
                }
            };
        }
    }
}
