using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Simcag.AIService.Application.Configuration;
using Simcag.AIService.Application.Contracts;
using Simcag.AIService.Application.Exceptions;
using Simcag.AIService.Application.Interfaces;
using Simcag.AIService.Application.Security;
using Simcag.AIService.Application.Utilities;

namespace Simcag.AIService.Application.UseCases.Insights;

public interface INarrateOperationalInsightsUseCase
{
    Task<NarrateOperationalInsightsResult> ExecuteAsync(NarrateOperationalInsightsInput input, CancellationToken ct);
}

public sealed class NarrateOperationalInsightsUseCase : INarrateOperationalInsightsUseCase
{
    private const int MaxItems = 12;
    private const int MaxFieldChars = 600;
    private const int MaxEvidenceJsonChars = 1200;
    /// <summary>Limite de espera pela narração LLM antes de devolver fallback determinístico.</summary>
    private const int NarrationTimeoutSeconds = 45;

    private readonly IOllamaClient _ollama;
    private readonly ILogger<NarrateOperationalInsightsUseCase> _logger;
    private readonly string _modelName;

    public NarrateOperationalInsightsUseCase(IOllamaClient ollama, ILogger<NarrateOperationalInsightsUseCase> logger)
    {
        _ollama = ollama;
        _logger = logger;
        _modelName = AiServiceEnvironment.ModelName;
    }

    public async Task<NarrateOperationalInsightsResult> ExecuteAsync(NarrateOperationalInsightsInput input, CancellationToken ct)
    {
        if (input.Items.Count == 0)
            throw new AiServiceException("Lista de insights vazia.");
        if (input.Items.Count > MaxItems)
            throw new AiServiceException($"No máximo {MaxItems} insights por pedido.");

        if (!await _ollama.IsAvailableAsync(ct).ConfigureAwait(false))
        {
            _logger.LogWarning("Ollama indisponível — narração determinística.");
            return BuildDeterministicFallback(input, llmUnavailable: true);
        }

        var payload = BuildPayloadJson(input);
        var prompt = BuildPrompt(input.Language ?? "pt", payload);

        if (!LlmPromptSafety.TryEvaluate(prompt, LlmPromptSafety.ShouldBlock, _logger, out var reject))
            throw new AiServiceException($"Pedido bloqueado por política de segurança ({reject}).");

        string raw;
        try
        {
            using var llmCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            llmCts.CancelAfter(TimeSpan.FromSeconds(NarrationTimeoutSeconds));
            raw = await _ollama.GenerateCompletionAsync(prompt, _modelName, llmCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao gerar narração de insights — fallback determinístico.");
            return BuildDeterministicFallback(input, llmUnavailable: true);
        }

        if (!LlmStructuredJsonParser.TryParseJsonObject(raw, "operational_insights_narrative", out var doc))
        {
            _logger.LogWarning("Resposta LLM sem JSON válido para narração de insights — fallback determinístico.");
            return BuildDeterministicFallback(input, llmUnavailable: false);
        }

        try
        {
            using (doc)
            {
                return ParseResult(doc.RootElement, input);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "JSON de narração incompleto — fallback determinístico.");
            return BuildDeterministicFallback(input, llmUnavailable: false);
        }
    }

    private static string BuildPayloadJson(NarrateOperationalInsightsInput input)
    {
        var items = input.Items.Select(i => new
        {
            id = Truncate(i.Id, 128),
            kind = Truncate(i.Kind, 80),
            title = Truncate(i.Title, MaxFieldChars),
            summary = Truncate(i.Summary, MaxFieldChars),
            severity = Truncate(i.Severity, 32),
            impactScore = i.ImpactScore,
            simpleExplanation = Truncate(i.SimpleExplanation, MaxFieldChars),
            evidence = i.Evidence != null
                ? JsonSerializer.Serialize(
                    i.Evidence.OrderBy(kv => kv.Key).Take(10)
                        .ToDictionary(kv => kv.Key, kv => Truncate(kv.Value, 160)))
                : null
        });

        var sb = new StringBuilder();
        sb.Append("{\"items\":");
        sb.Append(JsonSerializer.Serialize(items));
        sb.Append('}');
        var s = sb.ToString();
        return s.Length <= MaxEvidenceJsonChars * 2 ? s : s[..Math.Min(s.Length, MaxEvidenceJsonChars * 2)];
    }

    private static string BuildPrompt(string lang, string payloadJson) =>
        string.Concat(
            "És um assistente para síndicos e conselhos de condomínio em Portugal. Língua: ",
            lang,
            ".\n",
            "Recebes JSON com insights numéricos já calculados. Não inventes números nem alteres factos.\n",
            "Produz APENAS um único objecto JSON (sem markdown, sem texto antes ou depois) com as chaves:\n",
            "executiveSummary (string), items (array de objectos com id, simpleExplanation, whyItMatters, whatToDo, detailedExplanation).\n",
            "Regras: evita acrónimos de TI; não cites nomes de bases de dados; foca decisão e risco operacional.\n",
            "Dados de entrada (JSON):\n",
            payloadJson);

    private static NarrateOperationalInsightsResult ParseResult(JsonElement root, NarrateOperationalInsightsInput input)
    {
        var exec = "";
        if (root.TryGetProperty("executiveSummary", out var exEl) && exEl.ValueKind == JsonValueKind.String)
            exec = Truncate(exEl.GetString() ?? "", 1200);

        var byId = new Dictionary<string, NarrateOperationalInsightItemNarrative>(StringComparer.Ordinal);
        if (root.TryGetProperty("items", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in arr.EnumerateArray())
            {
                if (!el.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String)
                    continue;
                var id = idEl.GetString() ?? "";
                if (string.IsNullOrWhiteSpace(id))
                    continue;
                byId[id] = new NarrateOperationalInsightItemNarrative
                {
                    Id = id,
                    SimpleExplanation = ReadStr(el, "simpleExplanation"),
                    WhyItMatters = ReadStr(el, "whyItMatters"),
                    WhatToDo = ReadStr(el, "whatToDo"),
                    DetailedExplanation = ReadStr(el, "detailedExplanation")
                };
            }
        }

        var ordered = input.Items
            .Select(i => byId.TryGetValue(i.Id, out var n)
                ? n
                : new NarrateOperationalInsightItemNarrative
                {
                    Id = i.Id,
                    SimpleExplanation = i.SimpleExplanation,
                    WhyItMatters = "",
                    WhatToDo = "",
                    DetailedExplanation = ""
                })
            .ToList();

        return new NarrateOperationalInsightsResult
        {
            ExecutiveSummary = string.IsNullOrWhiteSpace(exec)
                ? "Resumo IA não devolvido pelo modelo — use o resumo executivo determinístico do servidor."
                : exec,
            Items = ordered
        };
    }

    private static string ReadStr(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.String)
            return "";
        return Truncate(p.GetString() ?? "", MaxFieldChars);
    }

    private static NarrateOperationalInsightsResult BuildDeterministicFallback(
        NarrateOperationalInsightsInput input,
        bool llmUnavailable)
    {
        var highlights = input.Items
            .Take(5)
            .Select(i => string.IsNullOrWhiteSpace(i.SimpleExplanation) ? i.Title : i.SimpleExplanation)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        var exec = highlights.Count > 0
            ? (llmUnavailable
                ? "Narração automática (IA indisponível ou demorada): "
                : "Narração automática (resposta IA incompleta): ")
              + string.Join(" · ", highlights)
            : llmUnavailable
                ? "IA temporariamente indisponível. Utilize o resumo executivo determinístico acima."
                : "Não foi possível interpretar a resposta da IA. Utilize o resumo executivo determinístico acima.";

        var items = input.Items
            .Select(i => new NarrateOperationalInsightItemNarrative
            {
                Id = i.Id,
                SimpleExplanation = string.IsNullOrWhiteSpace(i.SimpleExplanation) ? i.Summary : i.SimpleExplanation,
                WhyItMatters = "",
                WhatToDo = "",
                DetailedExplanation = ""
            })
            .ToList();

        return new NarrateOperationalInsightsResult
        {
            ExecutiveSummary = Truncate(exec, 1200),
            Items = items
        };
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max)
            return s;
        return s[..max];
    }
}
