using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExamApp.Services;

public class DeepSeekAiService
{
    private readonly HttpClient _httpClient;
    private readonly SettingsService _settings;
    private const string DefaultApiUrl = "https://api.deepseek.com/v1/chat/completions";
    private const string Model = "deepseek-v4-pro";

    public DeepSeekAiService(HttpClient httpClient, SettingsService settings)
    {
        _httpClient = httpClient;
        _settings = settings;
        _httpClient.Timeout = TimeSpan.FromSeconds(60);
    }

    /// <summary>
    /// Check if API is configured and reachable.
    /// </summary>
    public async Task<(bool ok, string message)> VerifyConnectionAsync()
    {
        var s = _settings.Load();
        if (string.IsNullOrWhiteSpace(s.ApiKey) || s.ApiKey.Length < 10)
            return (false, "API密钥未设置，请在设置页面配置。");

        var apiUrl = !string.IsNullOrWhiteSpace(s.ApiUrl) ? s.ApiUrl : DefaultApiUrl;

        try
        {
            var requestBody = new
            {
                model = Model,
                messages = new[]
                {
                    new { role = "user", content = "hi" }
                },
                max_tokens = 5
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, apiUrl) { Content = content };
            request.Headers.Add("Authorization", $"Bearer {s.ApiKey}");

            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
                return (true, "API连接正常");

            var errorBody = await response.Content.ReadAsStringAsync();
            return (false, $"API返回错误 ({(int)response.StatusCode}): {TruncateText(errorBody, 200)}");
        }
        catch (Exception ex)
        {
            return (false, $"连接失败: {ex.Message}");
        }
    }

    /// <summary>
    /// Score a case analysis / short answer question using DeepSeek AI.
    /// Returns a score (0-100) and feedback.
    /// </summary>
    public async Task<AiScoreResult> ScoreAnswerAsync(
        string questionContent,
        string referenceAnswer,
        string userAnswer)
    {
        if (string.IsNullOrWhiteSpace(userAnswer))
        {
            return new AiScoreResult { Score = 0, Feedback = "未作答。" };
        }

        // Load settings dynamically
        var s = _settings.Load();
        var apiUrl = !string.IsNullOrWhiteSpace(s.ApiUrl) ? s.ApiUrl : DefaultApiUrl;
        var apiKey = s.ApiKey;

        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length < 10)
        {
            return new AiScoreResult
            {
                Score = 0,
                Feedback = "AI评分未配置：请在设置页面填写API密钥后重试。"
            };
        }

        var systemPrompt = @"你是一位专业的一级建造师考试评卷老师。你的任务是根据参考答案对考生的作答进行评分。

评分规则：
1. 满分100分（即100%正确率）
2. 根据考生答案与参考答案的匹配程度进行评分
3. 如果考生答案涵盖了所有关键点，给90-100分
4. 如果考生答案涵盖了大部分关键点但有遗漏，给60-89分
5. 如果考生答案只涵盖小部分关键点，给30-59分
6. 如果考生答案完全错误或无关，给0-29分
7. 评分要客观、公正，基于专业知识判断
8. 关键术语有误应适当扣分

请以JSON格式返回评分结果：
{
  ""score"": 数字(0-100),
  ""feedback"": ""评语和扣分说明，包括考生答对了哪些点，遗漏了哪些点""
}

只返回JSON，不要包含其他内容。";

        var userPrompt = $@"请对以下一级建造师考试答案进行评分：

【题目】
{TruncateText(questionContent, 2000)}

【参考答案】
{TruncateText(referenceAnswer, 2000)}

【考生作答】
{TruncateText(userAnswer, 2000)}";

        try
        {
            var requestBody = new
            {
                model = Model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                temperature = 0.1,
                max_tokens = 1000
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, apiUrl)
            {
                Content = content
            };
            request.Headers.Add("Authorization", $"Bearer {apiKey}");

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[AI] DeepSeek API error {(int)response.StatusCode}: {errorBody}");
                return new AiScoreResult
                {
                    Score = 0,
                    Feedback = $"AI评分服务暂时不可用 ({(int)response.StatusCode})。请稍后重试。",
                    Error = errorBody
                };
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            var aiResponse = JsonSerializer.Deserialize<DeepSeekResponse>(responseJson);

            if (aiResponse?.Choices == null || aiResponse.Choices.Length == 0)
            {
                return new AiScoreResult { Score = 0, Feedback = "AI评分未返回结果。" };
            }

            var resultText = aiResponse.Choices[0].Message.Content.Trim();

            try
            {
                var jsonStart = resultText.IndexOf('{');
                var jsonEnd = resultText.LastIndexOf('}');
                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    var jsonResult = resultText[jsonStart..(jsonEnd + 1)];
                    var scoreResult = JsonSerializer.Deserialize<AiScoreResult>(jsonResult);
                    if (scoreResult != null && scoreResult.Score >= 0)
                        return scoreResult;
                }
            }
            catch { }

            var scoreMatch = System.Text.RegularExpressions.Regex.Match(resultText, @"""score""\s*:\s*(\d+)");
            if (scoreMatch.Success)
            {
                return new AiScoreResult
                {
                    Score = int.Parse(scoreMatch.Groups[1].Value),
                    Feedback = resultText
                };
            }

            return new AiScoreResult { Score = 50, Feedback = resultText };
        }
        catch (Exception ex)
        {
            return new AiScoreResult
            {
                Score = 0,
                Feedback = $"AI评分服务异常: {ex.Message}",
                Error = ex.ToString()
            };
        }
    }

    private static string TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return "";
        if (text.Length <= maxLength) return text;
        return text[..maxLength] + "...";
    }
}

public class AiScoreResult
{
    [JsonPropertyName("score")]
    public int Score { get; set; }

    [JsonPropertyName("feedback")]
    public string Feedback { get; set; } = "";

    [JsonIgnore]
    public string? Error { get; set; }
}

public class DeepSeekResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("choices")]
    public DeepSeekChoice[]? Choices { get; set; }
}

public class DeepSeekChoice
{
    [JsonPropertyName("message")]
    public DeepSeekMessage Message { get; set; } = new();
}

public class DeepSeekMessage
{
    [JsonPropertyName("content")]
    public string Content { get; set; } = "";
}
