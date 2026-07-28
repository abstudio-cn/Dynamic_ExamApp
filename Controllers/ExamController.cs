using Microsoft.AspNetCore.Mvc;
using ExamApp.Models;
using ExamApp.Services;

namespace ExamApp.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ExamController : ControllerBase
{
    private readonly QuestionBankService _bank;
    private readonly DeepSeekAiService _ai;
    private readonly ResultStoreService _resultStore;
    private readonly SettingsService _settings;

    public ExamController(QuestionBankService bank, DeepSeekAiService ai, ResultStoreService resultStore, SettingsService settings)
    {
        _bank = bank;
        _ai = ai;
        _resultStore = resultStore;
        _settings = settings;
    }

    /// <summary>
    /// Get system config from 题库/题库类型.md.
    /// </summary>
    [HttpGet("config")]
    public ActionResult<object> GetConfig()
    {
        var config = _bank.GetSystemConfig();
        return Ok(config);
    }

    /// <summary>
    /// Get all available categories with question counts.
    /// </summary>
    [HttpGet("categories")]
    public ActionResult<List<CategoryInfo>> GetCategories()
    {
        return Ok(_bank.GetAllCategoryInfos());
    }

    /// <summary>
    /// Debug: get first few questions from a category.
    /// </summary>
    [HttpGet("debug/{category}")]
    public ActionResult<List<QuestionDto>> DebugQuestions(string category, [FromQuery] int count = 5)
    {
        var questions = _bank.GetRandomQuestions(category, new List<string> { "all" }, count);
        Console.WriteLine($"Debug: category={category}, found={questions.Count}");
        if (questions.Count > 0)
        {
            Console.WriteLine($"  First type={questions[0].Type}, answer={questions[0].Answer}");
        }

        var dtos = questions.Select(q => new QuestionDto
        {
            Id = q.Id,
            Type = q.Type.ToString(),
            ContentHtml = q.ContentHtml,
            Options = q.Options,
            Difficulty = q.Difficulty
        }).ToList();
        return Ok(dtos);
    }

    /// <summary>
    /// Debug: get type distribution for a category.
    /// </summary>
    [HttpGet("debug-types/{category}")]
    public ActionResult<object> DebugTypes(string category)
    {
        // Get ALL questions (not random sample) - just count by type
        var questions = _bank.GetRandomQuestions(category, new List<string> { "all" }, int.MaxValue);
        var dist = new Dictionary<string, int>();
        foreach (var q in questions)
        {
            var t = q.Type.ToString();
            dist[t] = dist.GetValueOrDefault(t) + 1;
        }
        return Ok(new { category, total = questions.Count, distribution = dist });
    }

    /// <summary>
    /// Get random questions for an exam.
    /// Query params: category, types (comma-separated: single,multi,judge,case,fill), count
    /// </summary>
    [HttpPost("questions")]
    public ActionResult<List<QuestionDto>> GetQuestions([FromBody] ExamRequest request)
    {
        // Resolve categories: prefer list, fall back to single for compatibility
        var categories = request.Categories?.Count > 0
            ? request.Categories
            : (!string.IsNullOrWhiteSpace(request.Category) ? new List<string> { request.Category } : new List<string>());

        if (categories.Count == 0)
            return BadRequest("请选择题库类别。");

        var questions = _bank.GetRandomQuestions(categories, request.Types, request.Count);

        var dtos = questions.Select(q => new QuestionDto
        {
            Id = q.Id,
            Type = q.Type switch
            {
                QuestionType.SingleChoice => "single",
                QuestionType.MultiChoice => "multi",
                QuestionType.TrueFalse => "judge",
                QuestionType.CaseAnalysis => "case",
                QuestionType.FillInBlank => "fill",
                _ => "unknown"
            },
            ContentHtml = q.ContentHtml,
            Options = q.Options,
            Difficulty = q.Difficulty
        }).ToList();

        return Ok(dtos);
    }

    /// <summary>
    /// Submit answers and get grading results.
    /// </summary>
    [HttpPost("submit")]
    public async Task<ActionResult<ExamResult>> SubmitAnswers([FromBody] ExamSubmission submission)
    {
        if (submission.Answers == null || submission.Answers.Count == 0)
            return BadRequest("请提交答案。");

        var graded = new List<GradedQuestion>();
        int correctCount = 0;
        int aiScoredCount = 0;
        double aiTotalScore = 0;

        foreach (var ans in submission.Answers)
        {
            var q = _bank.GetQuestionById(ans.Id);
            if (q == null) continue;

            var gradedQ = new GradedQuestion
            {
                Id = q.Id,
                Type = q.Type switch
                {
                    QuestionType.SingleChoice => "single",
                    QuestionType.MultiChoice => "multi",
                    QuestionType.TrueFalse => "judge",
                    QuestionType.CaseAnalysis => "case",
                    QuestionType.FillInBlank => "fill",
                    _ => "unknown"
                },
                ContentHtml = q.ContentHtml,
                Options = q.Options,
                UserAnswer = ans.Answer ?? "",
                CorrectAnswer = q.Answer,
                AnalysisHtml = q.AnalysisHtml,
                Difficulty = q.Difficulty
            };

            if (q.IsAiScored)
            {
                // AI scoring for case analysis / short answer
                aiScoredCount++;
                var scoreResult = await _ai.ScoreAnswerAsync(q.Content, q.Answer, ans.Answer ?? "");

                gradedQ.IsCorrect = scoreResult.Score >= 60; // 60% as passing threshold
                gradedQ.AiScoreDetail = $"AI评分: {scoreResult.Score}/100\n\n{scoreResult.Feedback}";
                aiTotalScore += scoreResult.Score;

                if (scoreResult.Score >= 60) correctCount++;
            }
            else
            {
                // Objective & fill-in-blank questions: compare answers
                gradedQ.IsCorrect = CompareAnswers(q.Answer, ans.Answer ?? "", q.Type);
                if (gradedQ.IsCorrect) correctCount++;
            }

            graded.Add(gradedQ);
        }

        var result = new ExamResult
        {
            TotalQuestions = graded.Count,
            CorrectCount = correctCount,
            Score = graded.Count > 0 ? Math.Round((double)correctCount / graded.Count * 100, 1) : 0,
            AiScoredCount = aiScoredCount,
            AiTotalScore = aiTotalScore,
            AiMaxScore = aiScoredCount * 100,
            Questions = graded
        };

        // Store result server-side for tamper-proof verification
        result.ResultId = _resultStore.Store(result);

        return Ok(result);
    }

    /// <summary>
    /// Get stored result by ID (for tamper-proof score verification).
    /// </summary>
    [HttpGet("result/{resultId}")]
    public ActionResult<ExamResult> GetResult(string resultId)
    {
        var result = _resultStore.Get(resultId);
        if (result == null)
            return NotFound(new { error = "结果不存在或已过期" });
        return Ok(result);
    }

    /// <summary>
    /// Get current API settings (key masked).
    /// </summary>
    [HttpGet("settings")]
    public ActionResult<object> GetSettings()
    {
        var s = _settings.Load();
        return Ok(new
        {
            apiUrl = s.ApiUrl,
            apiKeyMasked = string.IsNullOrWhiteSpace(s.ApiKey) ? ""
                : s.ApiKey.Length <= 8 ? new string('*', s.ApiKey.Length)
                : s.ApiKey[..4] + new string('*', s.ApiKey.Length - 8) + s.ApiKey[^4..],
            isConfigured = _settings.IsConfigured()
        });
    }

    /// <summary>
    /// Save API settings.
    /// </summary>
    [HttpPost("settings")]
    public ActionResult<object> SaveSettings([FromBody] SaveSettingsRequest req)
    {
        var s = _settings.Load();
        if (!string.IsNullOrWhiteSpace(req.ApiUrl))
            s.ApiUrl = req.ApiUrl;
        if (!string.IsNullOrWhiteSpace(req.ApiKey) && req.ApiKey.Length > 10 && !req.ApiKey.Contains("***"))
            s.ApiKey = req.ApiKey;
        _settings.Save(s);
        return Ok(new { success = true, isConfigured = _settings.IsConfigured() });
    }

    /// <summary>
    /// Verify AI API connection.
    /// </summary>
    [HttpPost("settings/verify")]
    public async Task<ActionResult<object>> VerifySettings()
    {
        var (ok, message) = await _ai.VerifyConnectionAsync();
        return Ok(new { ok, message });
    }

    /// <summary>
    /// Check if case analysis questions can be used (API configured).
    /// </summary>
    [HttpGet("settings/ai-status")]
    public ActionResult<object> GetAiStatus()
    {
        return Ok(new { configured = _settings.IsConfigured() });
    }

    private static bool CompareAnswers(string correct, string user, QuestionType type)
    {
        if (string.IsNullOrWhiteSpace(user)) return false;

        var cleanCorrect = correct.Trim();
        var cleanUser = user.Trim();

        if (type == QuestionType.TrueFalse)
        {
            // Normalize True/False answers: map all variants to canonical form
            return NormalizeJudgeAnswer(cleanCorrect) == NormalizeJudgeAnswer(cleanUser);
        }

        if (type == QuestionType.MultiChoice)
        {
            // For multi-choice, sort the letters and compare
            var c = cleanCorrect.ToUpper();
            var u = cleanUser.ToUpper();
            var correctSorted = string.Concat(c.Where(char.IsLetter).OrderBy(ch => ch));
            var userSorted = string.Concat(u.Where(char.IsLetter).OrderBy(ch => ch));
            return correctSorted == userSorted;
        }

        if (type == QuestionType.FillInBlank)
        {
            // Multi-blank: split by | and compare each part
            var correctParts = cleanCorrect.Split('|', StringSplitOptions.TrimEntries);
            var userParts = cleanUser.Split('|', StringSplitOptions.TrimEntries);
            
            if (correctParts.Length != userParts.Length)
                return false;
            
            for (int i = 0; i < correctParts.Length; i++)
            {
                var nc = correctParts[i].Replace("\u3000", " ").Replace("  ", " ").Trim();
                var nu = userParts[i].Replace("\u3000", " ").Replace("  ", " ").Trim();
                if (!string.Equals(nc, nu, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }

        // For single choice, exact match (case-insensitive)
        return string.Equals(cleanCorrect, cleanUser, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Normalize judge answers: map 对/✓/√/V/True → TRUE, 错/×/✗/X/False → FALSE
    /// </summary>
    private static string NormalizeJudgeAnswer(string answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) return "";

        // True variants
        if (answer is "对" or "正确" or "✓" or "✔" or "√" or "V" or "v" or "True" or "true" or "TRUE" or "T" or "t")
            return "TRUE";

        // False variants
        if (answer is "错" or "错误" or "×" or "✗" or "✘" or "☓" or "X" or "x" or "False" or "false" or "FALSE" or "F" or "f")
            return "FALSE";

        return answer;
    }
}
