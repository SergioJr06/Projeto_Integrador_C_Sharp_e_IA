using System.Text;

namespace SafeGuardFrontend.Services;

/// <summary>Regras de filtro, dados de demonstração e exportação usadas pelo painel.</summary>
public static class PainelDados
{
    public static List<InfracaoDto> Demo() => new()
    {
        new() { Id=1001, CameraId="cam_0", EpisFaltantes="sem capacete", NivelConfianca=.94m, CaminhoEvidencia="/images/evidencia-demo.svg", DataHora=DateTime.Now.AddHours(-1) },
        new() { Id=1002, CameraId="cam_1", EpisFaltantes="sem colete", NivelConfianca=.89m, CaminhoEvidencia="/images/evidencia-demo.svg", DataHora=DateTime.Now.AddHours(-3) },
        new() { Id=1003, CameraId="cam_0", EpisFaltantes="sem capacete, sem oculos", NivelConfianca=.91m, CaminhoEvidencia="/images/evidencia-demo.svg", DataHora=DateTime.Now.AddDays(-1) },
        new() { Id=1004, CameraId="cam_2", EpisFaltantes="sem protetor auricular", NivelConfianca=.86m, CaminhoEvidencia="/images/evidencia-demo.svg", DataHora=DateTime.Now.AddDays(-2) },
        new() { Id=1005, CameraId="cam_1", EpisFaltantes="sem capacete", NivelConfianca=.96m, CaminhoEvidencia="/images/evidencia-demo.svg", DataHora=DateTime.Now.AddDays(-3) },
        new() { Id=1006, CameraId="cam_0", EpisFaltantes="sem colete, sem capacete", NivelConfianca=.88m, CaminhoEvidencia="/images/evidencia-demo.svg", DataHora=DateTime.Now.AddDays(-4) },
        new() { Id=1007, CameraId="cam_2", EpisFaltantes="sem oculos", NivelConfianca=.93m, CaminhoEvidencia="/images/evidencia-demo.svg", DataHora=DateTime.Now.AddDays(-5) }
    };

    /// <param name="periodo">Dias (7, 30) ou 0 para todo o histórico.</param>
    public static List<InfracaoDto> Filtrar(IEnumerable<InfracaoDto> todos, int periodo, string camera)
    {
        var limite = periodo == 0 ? DateTime.MinValue : DateTime.Today.AddDays(-(periodo - 1));
        return todos
            .Where(x => x.DataHora.ToLocalTime() >= limite && (camera == "" || x.CameraId == camera))
            .OrderByDescending(x => x.DataHora)
            .ToList();
    }

    /// <summary>Filtra por intervalo de datas (inclusive, no horário local) e câmera.</summary>
    public static List<InfracaoDto> FiltrarIntervalo(IEnumerable<InfracaoDto> todos, DateOnly inicio, DateOnly fim, string camera)
    {
        return todos
            .Where(x =>
            {
                var dia = DateOnly.FromDateTime(x.DataHora.ToLocalTime());
                return dia >= inicio && dia <= fim && (camera == "" || x.CameraId == camera);
            })
            .OrderByDescending(x => x.DataHora)
            .ToList();
    }

    public static string Csv(IEnumerable<InfracaoDto> lista)
    {
        static string Q(string v) => "\"" + v.Replace("\"", "\"\"") + "\"";
        var sb = new StringBuilder();
        sb.AppendLine("Ocorrência;Câmera;EPIs não conformes;Confiança (%);Data e hora;Evidência");
        foreach (var x in lista)
        {
            sb.AppendLine(string.Join(";",
                x.Id.ToString(),
                Q(x.CameraId),
                Q(string.Join(", ", EpiFormatter.Dividir(x.EpisFaltantes))),
                (x.NivelConfianca * 100).ToString("F1"),
                x.DataHora.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss"),
                Q(x.CaminhoEvidencia)));
        }
        return sb.ToString();
    }
}
