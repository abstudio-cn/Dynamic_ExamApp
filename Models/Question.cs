namespace ExamApp.Models;

public enum QuestionType
{
    SingleChoice,   // 单选题
    MultiChoice,    // 多选题
    TrueFalse,      // 判断题
    FillInBlank,    // 填空题（含口算题）
    CaseAnalysis,   // 案例分析/简答题/应用题
    Unknown
}

public class Question
{
    public string Id { get; set; } = "";
    public string Category { get; set; } = "";      // e.g., 公路工程
    public string SourceFile { get; set; } = "";    // source md file
    public QuestionType Type { get; set; }
    public int Number { get; set; }
    public string Content { get; set; } = "";       // raw question text
    public string ContentHtml { get; set; } = "";   // safe HTML rendering (with blanks replaced by markers)
    public List<string> Options { get; set; } = new();
    public string Answer { get; set; } = "";        // e.g., "C", "ABDE", "对", "错", "5 | 8 | 40" for multi-blank
    public string Analysis { get; set; } = "";      // 解析 (optional)
    public string AnalysisHtml { get; set; } = "";  // safe HTML for analysis
    public string Difficulty { get; set; } = "";    // 基础/进阶/综合
    public int BlankCount { get; set; }             // 填空题空白数（0表示非填空题）

    public bool IsAiScored => Type == QuestionType.CaseAnalysis;
    public bool IsTextAnswer => Type is QuestionType.CaseAnalysis or QuestionType.FillInBlank;

    /// <summary>
    /// For FillInBlank: split answer by | into individual blank answers.
    /// e.g., "5 | 8 | 40" → ["5", "8", "40"]
    /// </summary>
    public string[] GetBlankAnswers()
    {
        if (Type != QuestionType.FillInBlank || string.IsNullOrWhiteSpace(Answer))
            return Array.Empty<string>();
        return Answer.Split('|', StringSplitOptions.TrimEntries);
    }
}
