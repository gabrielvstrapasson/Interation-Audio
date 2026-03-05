using Brendha.Interview.Core.Models;
using System.Text.Json;

namespace Brendha.Interview.Server.Services.Prompt
{
    public class FeedbackPrompt
    {
        public static string Build(JobPosition job, string interviewLog)
        {
            var feedbackInstructions = new
            {
                meta = new
                {
                    role = "Technical Interview Feedback Agent.",
                    goal = "Analyze the interview log and generate a direct, critical report for the user, comparing the candidate's answers with the job requirements.",
                    output_language = "Portugues (pt-BR) ONLY.",
                    tone = "Direct, critical, and strictly honest. No need to be friendly or polite. Be objective and transparent."
                },

                job_context = new
                {
                    title = job.Title,
                    level = job.Level,
                    description = job.Description,
                    mandatory_skills = job.MandatorySkills,
                    nice_to_have_skills = job.NiceToHaveSkills,
                    salary_range = job.SalaryRange,
                    benefits = job.Benefits
                },

                interview_log = interviewLog,

                feedback_rules = new
                {
                    analysis = "Analyze only the content of the interview log. Do not ask questions, only evaluate.",
                    comparison = "Compare the candidate's answers with the mandatory and nice-to-have skills required for the job.",
                    experience = "Give more weight to experiences directly related to the job role.",
                    adherence = "Calculate the candidate's adherence to the job as a percentage from 0 to 100.",
                    justification = new
                    {
                        experience = "Explain how the candidate's experiences influenced the adherence.",
                        skills = "Explain how the skills present in the answers influenced the adherence.",
                        overall = "Clearly justify the assigned adherence percentage."
                    },
                    incompatibility = "If the candidate's profile is not compatible with the job, explain briefly and directly why.",
                    restrictions = "DO NOT mention salary expectations, gender, address, email, weight, or height at any time.",
                    validation = "Ensure all instructions have been strictly followed before delivering the response. If not, reformulate."
                },

                output_format = new
                {
                    CRITICAL_INSTRUCTION = "You MUST return a valid JSON object ONLY, with NO markdown formatting, using the exact following keys:",
                    adherence = "Integer from 0 to 100",
                    report = "A detailed, direct, and critical text (string)",
                    message = "A final, clear, and objective message about the candidate's adherence to the job.",
                    justification = new
                    {
                        experience = "Justification based on experiences (string)",
                        skills = "Justification based on skills (string)",
                        overall = "General justification (string)"
                    }
                }
            };

            string promptString = JsonSerializer.Serialize(feedbackInstructions, new JsonSerializerOptions { WriteIndented = false });
            return promptString;
        }
    }
}
