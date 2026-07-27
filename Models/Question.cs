namespace ExamApp.Models;

public enum QuestionType
{
    SingleChoice,   // 单选题
    MultiChoice,    // 多选题
    TrueFalse,      // 判断题
    CaseAnalysis,   // 案例分析/简答题
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
    public string ContentHtml { get; set; } = "";   // safe HTML rendering
    public List<string> Options { get; set; } = new();
    public string Answer { get; set; } = "";        // e.g., "C", "ABDE", "对", "错", or full text for case analysis
    public string Analysis { get; set; } = "";      // 解析 (optional)
    public string AnalysisHtml { get; set; } = "";  // safe HTML for analysis
    public string Difficulty { get; set; } = "";    // 基础/进阶/综合

    public bool IsAiScored => Type == QuestionType.CaseAnalysis;
}
