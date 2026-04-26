using System.Security.Cryptography;
using System.Text;

namespace Simcag.AIService.Application.Configuration;

/// <summary>
/// Chaves de cache de inferência LLM (hash do prompt + operação + modelo).
/// </summary>
public static class LlmInferenceCacheKeys
{
    public static string ForPrompt(string operation, string model, string prompt)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(prompt));
        var hash = Convert.ToHexString(bytes);
        return $"ai-service:llm:{operation}:{model}:{hash}";
    }
}
