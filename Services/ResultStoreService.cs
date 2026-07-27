using System.Collections.Concurrent;
using ExamApp.Models;

namespace ExamApp.Services;

/// <summary>
/// Thread-safe in-memory store for exam results.
/// Results expire after 30 minutes.
/// </summary>
public class ResultStoreService
{
    private readonly ConcurrentDictionary<string, StoredResult> _results = new();
    private readonly TimeSpan _expiry = TimeSpan.FromMinutes(30);

    public string Store(ExamResult result)
    {
        var id = Guid.NewGuid().ToString("N")[..12];
        _results[id] = new StoredResult
        {
            Result = result,
            StoredAt = DateTime.UtcNow
        };

        // Cleanup expired entries periodically
        var expired = _results.Where(kv => DateTime.UtcNow - kv.Value.StoredAt > _expiry)
                              .Select(kv => kv.Key).ToList();
        foreach (var key in expired)
            _results.TryRemove(key, out _);

        return id;
    }

    public ExamResult? Get(string id)
    {
        if (_results.TryGetValue(id, out var stored))
        {
            if (DateTime.UtcNow - stored.StoredAt > _expiry)
            {
                _results.TryRemove(id, out _);
                return null;
            }
            return stored.Result;
        }
        return null;
    }

    private class StoredResult
    {
        public ExamResult Result { get; set; } = new();
        public DateTime StoredAt { get; set; }
    }
}
