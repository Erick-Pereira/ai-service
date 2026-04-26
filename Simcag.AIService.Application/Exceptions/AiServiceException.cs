namespace Simcag.AIService.Application.Exceptions;

/// <summary>
/// Erro previsível da camada de integração com o motor de IA (Ollama) ou condições operacionais equivalentes.
/// </summary>
public sealed class AiServiceException : Exception
{
    public AiServiceException(string message) : base(message)
    {
    }

    public AiServiceException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
