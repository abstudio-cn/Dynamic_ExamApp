namespace ExamApp.Models;

public class ExamRequest
{
    public List<string> Categories { get; set; } = new(); // 支持多类别
    public string Category { get; set; } = ""; // 向后兼容单类别
    public List<string> Types { get; set; } = new(); // single, multi, judge, case
    public int Count { get; set; } = 20;
    public int? PerCategoryCount { get; set; } // 每类别抽题数，如设置则按比例分配
}

public class QuestionDto
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string ContentHtml { get; set; } = "";
    public List<string> Options { get; set; } = new();
    public string Difficulty { get; set; } = "";
}

public class AnswerSubmission
{
    public string Id { get; set; } = "";
    public string Answer { get; set; } = "";
}

public class ExamSubmission
{
    public List<AnswerSubmission> Answers { get; set; } = new();
}

public class GradedQuestion
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string ContentHtml { get; set; } = "";
    public List<string> Options { get; set; } = new();
    public string UserAnswer { get; set; } = "";
    public string CorrectAnswer { get; set; } = "";
    public bool IsCorrect { get; set; }
    public string AnalysisHtml { get; set; } = "";
    public string Difficulty { get; set; } = "";
    public string? AiScoreDetail { get; set; } // for AI-scored questions
}

public class ExamResult
{
    public string ResultId { get; set; } = "";    // Server-side result ID for verification
    public int TotalQuestions { get; set; }
    public int CorrectCount { get; set; }
    public double Score { get; set; }           // percentage 0-100
    public int AiScoredCount { get; set; }
    public double AiTotalScore { get; set; }    // AI scored total
    public double AiMaxScore { get; set; }      // AI scored max possible
    public List<GradedQuestion> Questions { get; set; } = new();
}

public class CategoryInfo
{
    public string Name { get; set; } = "";
    public int TotalQuestions { get; set; }
    public int SingleChoiceCount { get; set; }
    public int MultiChoiceCount { get; set; }
    public int TrueFalseCount { get; set; }
    public int CaseAnalysisCount { get; set; }
}

public class SaveSettingsRequest
{
    public string? ApiUrl { get; set; }
    public string? ApiKey { get; set; }
}
