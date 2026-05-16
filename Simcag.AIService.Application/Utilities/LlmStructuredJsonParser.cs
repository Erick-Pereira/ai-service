using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Simcag.Shared.Telemetry;

namespace Simcag.AIService.Application.Utilities;

/// <summary>
/// Extrai e faz parse de um único objeto JSON a partir de respostas LLM (markdown, texto extra, etc.).
/// </summary>
public static class LlmStructuredJsonParser
{
    /// <summary>Tenta obter <see cref="JsonDocument"/> de raiz objeto; incrementa métrica em falha total.</summary>
    public static bool TryParseJsonObject(string raw, string parseKind, [NotNullWhen(true)] out JsonDocument? doc)
    {
        doc = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            RecordFailure(parseKind, "empty");
            return false;
        }

        foreach (var candidate in BuildJsonCandidates(raw))
        {
            try
            {
                doc = JsonDocument.Parse(candidate);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    doc.Dispose();
                    doc = null;
                    continue;
                }

                return true;
            }
            catch (JsonException)
            {
                doc?.Dispose();
                doc = null;
            }
        }

        RecordFailure(parseKind, "no_valid_json");
        return false;
    }

    public static bool TryGetStringProperty(JsonElement root, string propertyName, [NotNullWhen(true)] out string? value)
    {
        value = null;
        if (!root.TryGetProperty(propertyName, out var el))
            return false;
        if (el.ValueKind != JsonValueKind.String)
            return false;
        value = el.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static void RecordFailure(string parseKind, string reason)
    {
        SimcagMeters.AiLlmParseFailures.Add(1,
            new KeyValuePair<string, object?>("kind", parseKind),
            new KeyValuePair<string, object?>("reason", reason));
    }

    private static IEnumerable<string> BuildJsonCandidates(string raw)
    {
        var t = raw.Trim();
        yield return t;
        yield return StripMarkdownCodeFence(t);
        var sliced = ExtractFirstJsonObjectSlice(t);
        if (!string.IsNullOrWhiteSpace(sliced) && !string.Equals(sliced, t, StringComparison.Ordinal))
            yield return sliced;
        var sliced2 = ExtractFirstJsonObjectSlice(StripMarkdownCodeFence(t));
        if (!string.IsNullOrWhiteSpace(sliced2))
            yield return sliced2;
    }

    private static string StripMarkdownCodeFence(string response)
    {
        var t = response.Trim();
        if (!t.StartsWith("```", StringComparison.Ordinal))
            return t;

        var firstNl = t.IndexOf('\n');
        if (firstNl > 0)
            t = t[(firstNl + 1)..];
        var end = t.LastIndexOf("```", StringComparison.Ordinal);
        if (end > 0)
            t = t[..end];
        return t.Trim();
    }

    /// <summary>Quando o modelo devolve preâmbulo antes do JSON.</summary>
    private static string? ExtractFirstJsonObjectSlice(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;
        return text[start..(end + 1)];
    }
}
