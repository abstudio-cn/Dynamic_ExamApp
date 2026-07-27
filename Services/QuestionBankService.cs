using System.Collections.Concurrent;
using ExamApp.Models;

namespace ExamApp.Services;

public class QuestionBankService
{
    private readonly MdParserService _parser;
    private readonly string _bankRoot;
    private readonly ConcurrentDictionary<string, List<Question>> _categoryQuestions = new();
    private readonly List<string> _categories = new();

    public IReadOnlyList<string> Categories => _categories.AsReadOnly();

    public QuestionBankService(MdParserService parser, string bankRoot)
    {
        _parser = parser;
        _bankRoot = bankRoot;
    }

    /// <summary>
    /// Load all question banks from the root directory.
    /// </summary>
    public void LoadAll()
    {
        if (!Directory.Exists(_bankRoot))
        {
            Console.WriteLine($"题库目录不存在: {_bankRoot}");
            return;
        }

        var categoryDirs = Directory.GetDirectories(_bankRoot);
        foreach (var dir in categoryDirs)
        {
            var category = Path.GetFileName(dir);
            _categories.Add(category);

            var questions = new List<Question>();
            var mdFiles = Directory.GetFiles(dir, "*.md", SearchOption.AllDirectories);

            foreach (var file in mdFiles)
            {
                try
                {
                    var fileQuestions = _parser.ParseFile(file, category);
                    questions.AddRange(fileQuestions);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"解析文件失败: {file}, 错误: {ex.Message}");
                }
            }

            _categoryQuestions[category] = questions;
            Console.WriteLine($"加载题库: {category} - {questions.Count} 题");
        }
    }

    /// <summary>
    /// Get category info for display.
    /// </summary>
    public CategoryInfo GetCategoryInfo(string category)
    {
        var info = new CategoryInfo { Name = category };
        if (_categoryQuestions.TryGetValue(category, out var questions))
        {
            info.TotalQuestions = questions.Count;
            info.SingleChoiceCount = questions.Count(q => q.Type == QuestionType.SingleChoice);
            info.MultiChoiceCount = questions.Count(q => q.Type == QuestionType.MultiChoice);
            info.TrueFalseCount = questions.Count(q => q.Type == QuestionType.TrueFalse);
            info.FillInBlankCount = questions.Count(q => q.Type == QuestionType.FillInBlank);
            info.CaseAnalysisCount = questions.Count(q => q.Type == QuestionType.CaseAnalysis);
        }
        return info;
    }

    public List<CategoryInfo> GetAllCategoryInfos()
    {
        return _categories.Select(GetCategoryInfo).ToList();
    }

    /// <summary>
    /// Get random questions from multiple categories, filtered by types.
    /// No duplicates within a session.
    /// </summary>
    public List<Question> GetRandomQuestions(List<string> categories, List<string> typeFilters, int count)
    {
        // Collect from all requested categories
        var allQuestions = new List<Question>();
        foreach (var cat in categories)
        {
            if (_categoryQuestions.TryGetValue(cat, out var catQuestions))
                allQuestions.AddRange(catQuestions);
        }

        if (allQuestions.Count == 0)
            return new List<Question>();

        return FilterAndShuffle(allQuestions, typeFilters, count);
    }

    /// <summary>
    /// Get random questions from a single category (backward compat).
    /// </summary>
    public List<Question> GetRandomQuestions(string category, List<string> typeFilters, int count)
    {
        if (!_categoryQuestions.TryGetValue(category, out var allQuestions))
            return new List<Question>();

        return FilterAndShuffle(allQuestions, typeFilters, count);
    }

    private List<Question> FilterAndShuffle(IReadOnlyList<Question> source, List<string> typeFilters, int count)
    {
        // Resolve type filters
        var typeSet = new HashSet<QuestionType>();
        if (typeFilters.Count > 0 && !typeFilters.Contains("all"))
        {
            foreach (var tf in typeFilters)
            {
                switch (tf.ToLower())
                {
                    case "single": typeSet.Add(QuestionType.SingleChoice); break;
                    case "multi": typeSet.Add(QuestionType.MultiChoice); break;
                    case "judge": typeSet.Add(QuestionType.TrueFalse); break;
                    case "case": typeSet.Add(QuestionType.CaseAnalysis); break;
                        case "fill": typeSet.Add(QuestionType.FillInBlank); break;
                }
            }
        }

        // If no type filter or "all", treat as single pool (backward compat)
        if (typeSet.Count == 0)
        {
            var all = source.ToList();
            if (all.Count == 0) return new List<Question>();
            Shuffle(all);
            return all.Take(Math.Min(count, all.Count)).ToList();
        }

        // Group by type
        var byType = new Dictionary<QuestionType, List<Question>>();
        foreach (var q in source)
        {
            if (!typeSet.Contains(q.Type)) continue;
            if (!byType.ContainsKey(q.Type))
                byType[q.Type] = new List<Question>();
            byType[q.Type].Add(q);
        }

        // Calculate proportional distribution
        var activeTypes = byType.Keys.ToList();
        if (activeTypes.Count == 0)
            return new List<Question>();

        int perType = count / activeTypes.Count;
        int remainder = count % activeTypes.Count;

        var result = new List<Question>();
        var rng = new Random();

        // Phase 1: take quota from each type
        var unmetTypes = new List<(QuestionType type, int shortage)>();
        foreach (var type in activeTypes)
        {
            var pool = byType[type];
            var quota = perType + (remainder > 0 ? 1 : 0);
            if (remainder > 0) remainder--;

            Shuffle(pool);
            var taken = pool.Take(Math.Min(quota, pool.Count)).ToList();
            result.AddRange(taken);

            if (taken.Count < quota)
                unmetTypes.Add((type, quota - taken.Count));
        }

        // Phase 2: redistribute unmet quotas to other types that have surplus
        if (unmetTypes.Count > 0)
        {
            var totalShortfall = unmetTypes.Sum(x => x.shortage);
            foreach (var type in activeTypes)
            {
                if (totalShortfall <= 0) break;
                if (unmetTypes.Any(u => u.type == type)) continue; // skip types that were already short

                var pool = byType[type];
                var alreadyTaken = result.Count(q => q.Type == type);
                var available = pool.Count - alreadyTaken;
                if (available <= 0) continue;

                var extra = Math.Min(totalShortfall, available);
                // Get questions not yet taken
                var takenIds = new HashSet<string>(result.Where(q => q.Type == type).Select(q => q.Id));
                var remaining = pool.Where(q => !takenIds.Contains(q.Id)).ToList();
                Shuffle(remaining);
                result.AddRange(remaining.Take(extra));
                totalShortfall -= extra;
            }
        }

        // Final shuffle so types are interleaved
        Shuffle(result);
        return result;
    }

    private static void Shuffle<T>(IList<T> list)
    {
        var rng = new Random();
        var n = list.Count;
        while (n > 1)
        {
            n--;
            var k = rng.Next(n + 1);
            (list[k], list[n]) = (list[n], list[k]);
        }
    }

    /// <summary>
    /// Get system configuration from 题库/题库类型.md.
    /// </summary>
    public Dictionary<string, string> GetSystemConfig()
    {
        var config = new Dictionary<string, string>();
        var configPath = Path.Combine(_bankRoot, "题库类型.md");

        if (!File.Exists(configPath))
        {
            config["title"] = "一级建造师模拟答题系统";
            config["subtitle"] = "专业工程管理与实务 · 在线练习平台";
            return config;
        }

        try
        {
            var lines = File.ReadAllLines(configPath, System.Text.Encoding.UTF8);
            string? currentKey = null;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                if (trimmed.StartsWith("## "))
                {
                    currentKey = trimmed[3..].Trim();
                }
                else if (currentKey != null && !string.IsNullOrWhiteSpace(trimmed) && !trimmed.StartsWith("#") && !trimmed.StartsWith(">"))
                {
                    var fieldName = currentKey switch
                    {
                        "系统标题" => "title",
                        "副标题" => "subtitle",
                        "题库说明" => "description",
                        _ => null
                    };

                    if (fieldName != null && !config.ContainsKey(fieldName))
                        config[fieldName] = trimmed;
                }
            }
        }
        catch
        {
            config["title"] = "一级建造师模拟答题系统";
        }

        if (!config.ContainsKey("title"))
            config["title"] = "一级建造师模拟答题系统";

        return config;
    }

    /// <summary>
    /// Get a question by ID.
    /// </summary>
    public Question? GetQuestionById(string id)
    {
        foreach (var kvp in _categoryQuestions)
        {
            var q = kvp.Value.FirstOrDefault(x => x.Id == id);
            if (q != null) return q;
        }
        return null;
    }
}
