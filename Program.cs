using ExamApp.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddSingleton<MarkdownSafeRenderer>();
builder.Services.AddSingleton<MdParserService>();

// Determine question bank path (priority: config > project-local > Downloads)
var bankPath = builder.Configuration.GetValue<string>("QuestionBank:Path");
if (string.IsNullOrWhiteSpace(bankPath) || !Directory.Exists(bankPath))
{
    var appDir = AppContext.BaseDirectory;
    var cwd = Directory.GetCurrentDirectory();
    var defaultPaths = new[]
    {
        // 1. Project-local "题库" (for portable distribution)
        Path.Combine(cwd, "题库"),
        Path.Combine(appDir, "题库"),
        // 2. Parent directory (when running from publish/)
        Path.Combine(cwd, "..", "题库"),
        Path.Combine(appDir, "..", "题库"),
        // 3. Downloads fallback
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "题库")
    };

    bankPath = defaultPaths.FirstOrDefault(Directory.Exists)
        ?? Path.Combine(cwd, "题库"); // create if missing
}

Console.WriteLine($"题库路径: {bankPath}");

var bankService = new QuestionBankService(
    new MdParserService(new MarkdownSafeRenderer()),
    bankPath
);
bankService.LoadAll();
builder.Services.AddSingleton(bankService);
builder.Services.AddSingleton<ResultStoreService>();

// Settings & AI service
var settingsPath = Directory.GetCurrentDirectory();
builder.Services.AddSingleton(new SettingsService(settingsPath));
builder.Services.AddHttpClient<DeepSeekAiService>();

// CORS for development
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

var app = builder.Build();

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();

// API fallback to index.html for SPA routing
app.MapFallbackToFile("index.html");

Console.WriteLine($"\n============================================");
Console.WriteLine("  一级建造师模拟答题系统");
Console.WriteLine($"  题库类别: {string.Join(", ", bankService.Categories)}");
Console.WriteLine($"  服务地址: http://localhost:5000");
Console.WriteLine($"  按 Ctrl+C 停止服务");
Console.WriteLine($"============================================\n");

app.Run();
