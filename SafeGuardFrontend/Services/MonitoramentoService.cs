using System.Net.Http.Json;

namespace SafeGuardFrontend.Services;

public sealed class InfracaoDto
{
    public int Id { get; set; }
    public string CameraId { get; set; } = "";
    public string EpisFaltantes { get; set; } = "";
    public decimal NivelConfianca { get; set; }
    public string CaminhoEvidencia { get; set; } = "";
    public DateTime DataHora { get; set; }
}

public sealed class MonitoramentoService(HttpClient http)
{
    public async Task<List<InfracaoDto>?> ListarAsync(CancellationToken cancellationToken = default)
    {
        try { return await http.GetFromJsonAsync<List<InfracaoDto>>("api/monitoramento", cancellationToken); }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }
    }

    /// <summary>Busca a foto de evidência na API (usado pelo painel para entregá-la ao navegador).</summary>
    public async Task<Stream?> ObterEvidenciaAsync(string arquivo, CancellationToken cancellationToken = default)
    {
        try
        {
            var resposta = await http.GetAsync($"evidencias/{arquivo}", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!resposta.IsSuccessStatusCode) { resposta.Dispose(); return null; }
            return await resposta.Content.ReadAsStreamAsync(cancellationToken);
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }
    }
}
