namespace Simcag.AIService.Api.Controllers.Legacy;

/// <summary>
/// Controllers fora do bounded context de auditoria financeira (ex.: produto legado) devem residir nesta pasta
/// e usar este nome em <c>ApiExplorerSettings(GroupName = ...)</c> para separação no explorador / futuros documentos OpenAPI.
/// </summary>
public static class LegacyApiExplorerGroup
{
    public const string Name = "legacy-product";
}
