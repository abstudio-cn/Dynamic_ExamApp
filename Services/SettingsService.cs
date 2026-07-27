using System.Text;
using System.Text.Json;

namespace ExamApp.Services;

public class SettingsService
{
    private readonly string _settingsPath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public SettingsService(string basePath)
    {
        _settingsPath = Path.Combine(basePath, "settings.md");
    }

    public AppSettings Load()
    {
        try
        {
            _lock.Wait();
            if (!File.Exists(_settingsPath))
                return new AppSettings();

            var lines = File.ReadAllLines(_settingsPath, Encoding.UTF8);
            var settings = new AppSettings();
            string? currentKey = null;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("## "))
                {
                    currentKey = trimmed[3..].Trim();
                }
                else if (currentKey != null && !string.IsNullOrWhiteSpace(trimmed) &&
                         !trimmed.StartsWith("#") && !trimmed.StartsWith(">"))
                {
                    switch (currentKey)
                    {
                        case "API地址":
                            settings.ApiUrl = trimmed;
                            break;
                        case "API密钥":
                            if (trimmed != "未设置" && trimmed.Length > 10)
                                settings.ApiKey = trimmed;
                            break;
                    }
                }
            }

            return settings;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            _lock.Wait();
            var sb = new StringBuilder();
            sb.AppendLine("# 系统设置");
            sb.AppendLine("> 修改以下内容后保存，服务会自动读取最新配置");
            sb.AppendLine();
            sb.AppendLine("## API地址");
            sb.AppendLine(string.IsNullOrWhiteSpace(settings.ApiUrl)
                ? "https://api.deepseek.com/v1/chat/completions"
                : settings.ApiUrl);
            sb.AppendLine();
            sb.AppendLine("## API密钥");
            sb.AppendLine(string.IsNullOrWhiteSpace(settings.ApiKey)
                ? "未设置"
                : settings.ApiKey);
            sb.AppendLine();

            File.WriteAllText(_settingsPath, sb.ToString(), Encoding.UTF8);
        }
        finally
        {
            _lock.Release();
        }
    }

    public bool IsConfigured()
    {
        var s = Load();
        return !string.IsNullOrWhiteSpace(s.ApiKey) && s.ApiKey.Length > 10;
    }
}

public class AppSettings
{
    public string ApiUrl { get; set; } = "https://api.deepseek.com/v1/chat/completions";
    public string ApiKey { get; set; } = "";
}
