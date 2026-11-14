namespace SafeGuardBackend.Services;

/// <summary>
/// Define onde as fotos de evidência ficam no disco.
/// Padrão: wwwroot/evidencias dentro do projeto. Para manter as fotos ao trocar de pasta/versão do
/// projeto, defina "Evidencias:Pasta" no appsettings.json (caminho absoluto ou relativo ao projeto).
/// </summary>
public static class EvidenciasPasta
{
    public static string Resolver(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configurada = configuration["Evidencias:Pasta"];
        if (string.IsNullOrWhiteSpace(configurada))
            return Path.Combine(environment.ContentRootPath, "wwwroot", "evidencias");
        var caminho = Path.IsPathRooted(configurada)
            ? configurada
            : Path.Combine(environment.ContentRootPath, configurada);
        return Path.GetFullPath(caminho);
    }
}
