using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Simcag.Shared.Telemetry;

namespace Simcag.AIService.Application.Security;

/// <summary>
/// Heurísticas leves para sinais de prompt injection (não substitui revisão humana nem modelo seguro).
/// Ativar bloqueio: <c>SIMCAG_AI_BLOCK_PROMPT_INJECTION=true</c>.
/// </summary>
public static class LlmPromptSafety
{
    private static readonly string[] SignalPhrases =
    [
        "ignore previous", "ignore all previous", "disregard previous", "forget previous",
        "system override", "override instructions", "jailbreak", "developer mode",
        "you are now", "act as if", "new instructions:", "ignore the above",
        "simulate a", "reveal your", "show your system", "print your prompt",
        "```system", "[system]", "{{system}}", "</s>",
    ];

    public static bool ShouldBlock =>
        string.Equals(Environment.GetEnvironmentVariable("SIMCAG_AI_BLOCK_PROMPT_INJECTION"), "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Environment.GetEnvironmentVariable("SIMCAG_AI_BLOCK_PROMPT_INJECTION"), "1", StringComparison.Ordinal);

    public static int CountSignals(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return 0;
        var hits = 0;
        foreach (var phrase in SignalPhrases)
        {
            if (prompt.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                hits++;
        }

        return hits;
    }

    /// <summary>False quando bloqueado por política; true para prosseguir.</summary>
    public static bool TryEvaluate(string? prompt, bool block, ILogger? logger, [NotNullWhen(false)] out string? rejectReason)
    {
        rejectReason = null;
        var n = CountSignals(prompt);
        if (n == 0)
            return true;

        SimcagMeters.AiPromptInjectionSignals.Add(n);
        logger?.LogWarning("LLM prompt safety: {Signals} heuristic signal(s) detected.", n);
        if (!block)
            return true;

        rejectReason = "prompt_injection_signals";
        return false;
    }
}
