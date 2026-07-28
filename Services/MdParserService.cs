using System.Text;
using System.Text.RegularExpressions;
using ExamApp.Models;

namespace ExamApp.Services;

public class MdParserService
{
    private readonly MarkdownSafeRenderer _renderer;

    public MdParserService(MarkdownSafeRenderer renderer)
    {
        _renderer = renderer;
    }

    public List<Question> ParseFile(string filePath, string category)
    {
        var questions = new List<Question>();
        if (!File.Exists(filePath)) return questions;

        var content = File.ReadAllText(filePath, Encoding.UTF8);
        var fileName = Path.GetFileName(filePath);

        // Split by question blocks — look for patterns like **数字.**
        // Each question starts with **<number>.** and ends before the next **<number>.** or section header
        var questionBlocks = SplitQuestions(content);

        // Use file-level incrementing index to guarantee unique IDs
        // (file question numbers may repeat across sections)
        int fileIndex = 0;
        foreach (var block in questionBlocks)
        {
            var q = ParseQuestionBlock(block, category, fileName, fileIndex);
            if (q != null) { questions.Add(q); fileIndex++; }
        }

        return questions;
    }

    private List<string> SplitQuestions(string content)
    {
        var blocks = new List<string>();

        // Match question start pattern: **N.**  (where N is a number)
        var pattern = @"\*\*(\d+)\.\*\*";
        var matches = Regex.Matches(content, pattern);

        for (int i = 0; i < matches.Count; i++)
        {
            var start = matches[i].Index;
            var blockEnd = content.Length;

            if (i + 1 < matches.Count)
            {
                var nextStart = matches[i + 1].Index;
                // Find the nearest section delimiter between this question and the next
                blockEnd = FindNearestDelimiter(content, start, nextStart);
            }
            else
            {
                // Last question — find delimiter after it
                blockEnd = FindNearestDelimiter(content, start, content.Length);
            }

            // Trim to effective end (stop at section headers within the block)
            blockEnd = FindNearestDelimiter(content, start, blockEnd);

            var block = content[start..blockEnd].Trim();
            if (!string.IsNullOrWhiteSpace(block))
                blocks.Add(block);
        }

        return blocks;
    }

    /// <summary>
    /// Find the nearest section delimiter (---, ##, ###, ####, or Chinese numbered sections)
    /// between `start` and `end`, returning the position right before the delimiter.
    /// </summary>
    private static int FindNearestDelimiter(string content, int start, int end)
    {
        var segment = content[start..end];
        int bestPos = end;

        // Markdown headers: ##, ###, #### (but NOT ** which is bold)
        foreach (Match m in Regex.Matches(segment, @"\r?\n(?:#{2,4}\s)"))
        {
            var pos = start + m.Index;
            if (pos > start && pos < bestPos)
                bestPos = pos;
        }

        // Horizontal rules
        var hrIdx = segment.IndexOf("\n---");
        if (hrIdx < 0) hrIdx = segment.IndexOf("\r\n---");
        if (hrIdx >= 0)
        {
            var pos = start + hrIdx;
            if (pos > start && pos < bestPos)
                bestPos = pos;
        }

        // Chinese section headers: lines starting with 一、二、三、四... (numbered sections)
        var cnSectionMatch = Regex.Match(segment,
            @"\r?\n(?:[一二三四五六七八九十]+、|第[一二三四五六七八九十百千]+[章节])");
        if (cnSectionMatch.Success)
        {
            var pos = start + cnSectionMatch.Index;
            if (pos > start && pos < bestPos)
                bestPos = pos;
        }

        // Also stop at lines that look like section type labels (e.g., "一、单选题")
        var typeLabelMatch = Regex.Match(segment,
            @"\r?\n(?:(?:一|二|三|四|五|六|七|八|九|十)、\s*(?:单选|多选|判断|案例|简答|案例分析))") ;
        if (typeLabelMatch.Success)
        {
            var pos = start + typeLabelMatch.Index;
            if (pos > start && pos < bestPos)
                bestPos = pos;
        }

        return bestPos;
    }

    private Question? ParseQuestionBlock(string block, string category, string fileName, int fileIndex)
    {
        try
        {
            // Extract question number
            var numMatch = Regex.Match(block, @"^\*\*(\d+)\.\*\*");
            if (!numMatch.Success) return null;
            var number = int.Parse(numMatch.Groups[1].Value);

            // Determine type
            var type = DetermineType(block);

            // Extract answer
            var answer = ExtractAnswer(block);

            // Extract difficulty
            var difficulty = ExtractDifficulty(block);

            // Extract analysis
            var analysis = ExtractAnalysis(block);

            // Extract question content and options
            var (content, options) = ExtractContentAndOptions(block, type);

            // Count blanks for FillInBlank questions
            int blankCount = 0;
            if (type == QuestionType.FillInBlank)
            {
                blankCount = Regex.Matches(content, "______").Count;
                if (blankCount == 0) blankCount = 1; // fallback: at least 1 blank
            }

            // Generate unique ID using file-level index (prevents collisions when numbers repeat)
            var id = $"{category}_{fileName}_{fileIndex}";
            // Sanitize ID for URL safety
            id = Regex.Replace(id, @"[^a-zA-Z0-9_\u4e00-\u9fff-]", "_");

            return new Question
            {
                Id = id,
                Category = category,
                SourceFile = fileName,
                Type = type,
                Number = number,
                Content = content,
                ContentHtml = _renderer.RenderToHtml(content),
                Options = options,
                Answer = answer,
                Analysis = analysis,
                AnalysisHtml = string.IsNullOrEmpty(analysis) ? "" : _renderer.RenderToHtml(analysis),
                Difficulty = difficulty,
                BlankCount = blankCount
            };
        }
        catch
        {
            return null;
        }
    }

    private QuestionType DetermineType(string block)
    {
        // === Layer 1: Explicit type tags (highest priority) ===
        // Chinese-bracket tags
        if (block.Contains("【案例分析】") || block.Contains("【简答】") ||
            block.Contains("【问答题】") || block.Contains("【案例"))
            return QuestionType.CaseAnalysis;

        // Inline [type] tags (common in examcoo UUID-sourced files)
        if (block.Contains("[单选题]"))
            return QuestionType.SingleChoice;
        if (block.Contains("[多选题]"))
            return QuestionType.MultiChoice;
        if (block.Contains("[判断题]"))
            return QuestionType.TrueFalse;

        // === Layer 2: Pattern-based case analysis detection ===
        if (block.Contains("【问题】") || block.Contains("**问题") ||
            block.Contains("**分析") || block.Contains("**背景") ||
            block.Contains("**案例"))
            return QuestionType.CaseAnalysis;

        // === Layer 2.5: Fill-in-blank detection (before answer-based checks) ===
        // Detect ______ / ___ (underscore blanks) used in fill-in-the-blank / oral calculation
        if (block.Contains("______") || block.Contains("____") ||
            block.Contains("___"))
            return QuestionType.FillInBlank;

        // === Layer 3: Answer-based detection ===
        // Extract answer (try both inline and multi-line formats)
        var answerMatch = Regex.Match(block, @"\*\*答案[：:]\s*([^\*]+)\*\*");
        var ans = answerMatch.Success ? answerMatch.Groups[1].Value.Trim() : "";

        // If no inline answer, try multi-line answer (no closing **)
        if (string.IsNullOrWhiteSpace(ans))
        {
            var mlMatch = Regex.Match(block,
                @"\*\*答案[：:]\s*\n?([\s\S]+?)(?=\n\s*\*\*难度|\n\s*\*\*解析|\n\s*\*\*依据|\n\s*\*\*来源|\n\s*---|\n\s*\*\*?\d+\.\*\*|\Z)",
                RegexOptions.Singleline);
            if (mlMatch.Success)
                ans = mlMatch.Groups[1].Value.Trim();
        }

        // Check for 判断题 — exact answer match
        var judgeAnswers = new HashSet<string> {
            "对", "错", "正确", "错误",
            "\u221a", "\u2713", "\u2714",  // √ ✓ ✔
            "\u00d7", "\u2717", "\u2718", "\u2613",  // × ✗ ✘ ☓
            "V", "v", "X", "x"
        };
        if (judgeAnswers.Contains(ans))
            return QuestionType.TrueFalse;

        // Multi-choice: answer has multiple uppercase letters (e.g., "ABDE")
        if (ans.Length > 1 && Regex.IsMatch(ans, @"^[A-E]+$"))
            return QuestionType.MultiChoice;

        // Single-choice: answer is a single uppercase letter (e.g., "C")
        if (ans.Length == 1 && Regex.IsMatch(ans, @"^[A-E]$"))
            return QuestionType.SingleChoice;

        // === Layer 4: Complex answer check (before option counting) ===
        if (!string.IsNullOrWhiteSpace(ans) && (
            ans.Contains("①") || ans.Contains("②") || ans.Contains("③") ||
            ans.Contains("（1）") || ans.Contains("(1)") ||
            ans.Contains("\n") ||
            (ans.Length > 10 && Regex.IsMatch(ans, @"[\u4e00-\u9fff]"))))
            return QuestionType.CaseAnalysis;

        // === Layer 5: Option count based ===
        var optionMatches = Regex.Matches(block, @"(?<![A-Z])[A-E][.、)\s．]");

        if (optionMatches.Count >= 5)
            return QuestionType.MultiChoice;
        if (optionMatches.Count >= 2 && optionMatches.Count <= 4)
            return QuestionType.SingleChoice;

        // TrueFalse fallback: exactly 2 options that are 对/错 pair
        if (optionMatches.Count == 2)
        {
            var stripped = Regex.Replace(block, @"\*\*答案[：:][^\n]+\*\*.*$", "", RegexOptions.Singleline);
            var optText = Regex.Replace(stripped, @"\*\*\d+\.\*\*\s*", "");
            if (optText.Contains("对") && optText.Contains("错"))
                return QuestionType.TrueFalse;
        }

        // === Layer 6: Fallbacks ===
        if (optionMatches.Count == 0 && block.Length > 200)
            return QuestionType.CaseAnalysis;

        return QuestionType.SingleChoice;
    }

    private string ExtractAnswer(string block)
    {
        // Format 1: **答案: X** or **答案：X** (inline answer, wrapped in bold)
        var match = Regex.Match(block, @"\*\*答案[：:]\s*([^\*]+?)\s*\*\*");
        if (match.Success)
        {
            var ans = match.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(ans))
                return NormalizeLineBreaks(ans).TrimEnd('*').Trim();
        }

        // Format 2: **答案:** followed by multi-line answer (no closing ** before next field)
        // Matches from **答案: ... up to **难度, **解析, **依据, **来源, ---, or next question
        var caseMatch = Regex.Match(block,
            @"\*\*答案[：:]\s*\n?([\s\S]+?)(?=\n\s*\*\*难度|\n\s*\*\*解析|\n\s*\*\*依据|\n\s*\*\*来源|\n\s*---|\n\s*\*\*?\d+\.\*\*|\Z)",
            RegexOptions.Singleline);
        if (caseMatch.Success)
        {
            var ans = caseMatch.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(ans))
                return NormalizeLineBreaks(ans).TrimEnd('*').Trim();
        }

        // Format 3: 【参考答案】... (self-created question bank, Chinese-bracket format)
        var refMatch = Regex.Match(block,
            @"【参考答案】\s*([\s\S]+?)(?=\n\s*\*\*难度|\n\s*\*\*解析|\n\s*---|\n\s*\*\*?\d+\.\*\*|$)",
            RegexOptions.Singleline);
        if (refMatch.Success)
        {
            var ans = refMatch.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(ans))
                return NormalizeLineBreaks(ans).TrimEnd('*').Trim();
        }

        return "";
    }

    /// <summary>
    /// Convert <BR> tags from source to actual newlines.
    /// </summary>
    private static string NormalizeLineBreaks(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        return Regex.Replace(text, @"<BR\s*/?>", "\n", RegexOptions.IgnoreCase);
    }

    private string ExtractDifficulty(string block)
    {
        var match = Regex.Match(block, @"\*\*难度[：:]\s*([^\*]+?)\s*\*\*");
        if (match.Success)
            return match.Groups[1].Value.Trim();

        return "";
    }

    private string ExtractAnalysis(string block)
    {
        // Look for **解析: or **解析：
        var match = Regex.Match(block, @"\*\*解析[：:]\s*([^\*]+?)\s*\*\*");
        if (match.Success)
            return match.Groups[1].Value.Trim();

        // Some formats have the analysis after the answer on a new line
        match = Regex.Match(block, @"\*\*答案[：:]\s*[^\*]+\*\*\s*\n+\s*(.+?)(?:\n\*\*|\n*$)", RegexOptions.Singleline);
        if (match.Success)
        {
            var text = match.Groups[1].Value.Trim();
            // Only treat as analysis if it's not another metadata field
            if (!text.StartsWith("**") && text.Length > 10)
                return text;
        }

        return "";
    }

    private (string content, List<string> options) ExtractContentAndOptions(string block, QuestionType type)
    {
        var options = new List<string>();

        // Remove the question number prefix
        var text = Regex.Replace(block, @"^\*\*\d+\.\*\*\s*", "");

        // For case analysis and fill-in-blank, content is everything before **答案**
        if (type == QuestionType.CaseAnalysis || type == QuestionType.FillInBlank)
        {
            // Remove **答案** and everything after
            var answerIdx = text.IndexOf("\n**答案");
            if (answerIdx > 0)
                text = text[..answerIdx].Trim();
            else
            {
                var altIdx = text.IndexOf("**答案");
                if (altIdx > 0)
                    text = text[..altIdx].Trim();
            }

            // Also strip 【参考答案】 and everything after
            var refIdx = text.IndexOf("【参考答案】");
            if (refIdx > 0)
                text = text[..refIdx].Trim();

            // Remove metadata lines from content
            text = Regex.Replace(text, @"\*\*难度[：:][^\*]+\*\*", "");
            text = Regex.Replace(text, @"\*\*解析[：:][^\*]+\*\*", "");
            text = Regex.Replace(text, @"\*\*依据[：:][^\*]+\*\*", "");
            text = Regex.Replace(text, @"\*\*来源[：:][^\*]+\*\*", "");
            text = Regex.Replace(text, @"\n(?:解析|依据|来源)[：:].+(?:\n(?!\s*\*\*|\s*---).+)*", "");
            return (text.Trim(), options);
        }

        // For choice questions, split content from options
        // Remove metadata lines (inline **答案: X**, **难度: X**)
        text = Regex.Replace(text, @"\*\*答案[：:][^\*]+\*\*", "");
        text = Regex.Replace(text, @"\*\*难度[：:][^\*]+\*\*", "");
        // Remove inline **解析: ...** and **依据: ...** (bold metadata that should not appear in question)
        text = Regex.Replace(text, @"\*\*解析[：:][^\*]+\*\*", "");
        text = Regex.Replace(text, @"\*\*依据[：:][^\*]+\*\*", "");
        text = Regex.Replace(text, @"\*\*来源[：:][^\*]+\*\*", "");
        // Remove multi-line non-bold解析/依据 after answer
        text = Regex.Replace(text, @"\n(?:解析|依据|来源)[：:].+(?:\n(?!\s*[A-E][.、)\s．]|\s*\*\*|\s*---).+)*", "");

        // Split options: pattern A. xxx B. xxx C. xxx
        // First separate the question content from options
        var optionStartIdx = -1;
        var optionPatterns = new[] { "\nA.", "\nA ", "\nA、", "\nA)", "\nA．" };
        foreach (var pat in optionPatterns)
        {
            var idx = text.IndexOf(pat);
            if (idx >= 0 && (optionStartIdx < 0 || idx < optionStartIdx))
                optionStartIdx = idx;
        }

        string content;
        string optionSection;

        if (optionStartIdx > 0)
        {
            content = text[..optionStartIdx].Trim();
            optionSection = text[optionStartIdx..].Trim();
        }
        else
        {
            content = text.Trim();
            return (content, options);
        }

        // Parse options from option section
        // Match patterns like A. xxx or B、xxx
        var optMatches = Regex.Matches(optionSection, @"([A-E])[.、)\s．]+(.+?)(?=\s*[A-E][.、)\s．]|\s*\*\*|$)", RegexOptions.Singleline);
        foreach (Match m in optMatches)
        {
            var optText = m.Groups[2].Value.Trim();
            // Clean up trailing whitespace/newlines
            optText = Regex.Replace(optText, @"\s+", " ").Trim();
            if (!string.IsNullOrWhiteSpace(optText))
                options.Add(optText);
        }

        return (content, options);
    }
}
