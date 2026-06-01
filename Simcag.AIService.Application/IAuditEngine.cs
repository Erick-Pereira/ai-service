namespace Simcag.AIService.Application;

/// <summary>
/// Contrato para motores de auditoria de documentos e contratos (ADIA).
/// </summary>
public interface IAuditEngine
{
    /// <summary>
    /// Executa a análise de paridade entre diferentes fontes documentais.
    /// </summary>
    string RunAudit(string technicalContractContent, IReadOnlyList<string> baseSources);
}
