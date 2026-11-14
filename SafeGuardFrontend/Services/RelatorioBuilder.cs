using System.Globalization;

namespace SafeGuardFrontend.Services;

public sealed record ItemQtd(string Rotulo, int Qtd);

public sealed class RelatorioDados
{
    public List<InfracaoDto> Lista { get; init; } = new();
    public int Total => Lista.Count;
    public List<ItemQtd> PorEpi { get; init; } = new();
    public List<ItemQtd> PorDia { get; init; } = new();
    public List<ItemQtd> PorCamera { get; init; } = new();
    public double ConfiancaMedia { get; init; }
    public int DiasNoPeriodo { get; init; }
    public string Texto { get; init; } = "";
}

/// <summary>Calcula os números do relatório e escreve o resumo descritivo em português.</summary>
public static class RelatorioBuilder
{
    private static readonly CultureInfo Pt = new("pt-BR");

    public static RelatorioDados Montar(IEnumerable<InfracaoDto> permitidas, DateOnly inicio, DateOnly fim, string camera)
    {
        var lista = PainelDados.FiltrarIntervalo(permitidas, inicio, fim, camera);
        var dias = fim.DayNumber - inicio.DayNumber + 1;

        var porEpi = lista.SelectMany(x => EpiFormatter.Dividir(x.EpisFaltantes))
            .GroupBy(x => x).Select(g => new ItemQtd(g.Key, g.Count()))
            .OrderByDescending(x => x.Qtd).ThenBy(x => x.Rotulo).ToList();
        var porDia = lista.GroupBy(x => DateOnly.FromDateTime(x.DataHora.ToLocalTime()))
            .OrderBy(g => g.Key).Select(g => new ItemQtd(D(g.Key), g.Count())).ToList();
        var porCamera = lista.GroupBy(x => x.CameraId)
            .Select(g => new ItemQtd(g.Key, g.Count()))
            .OrderByDescending(x => x.Qtd).ThenBy(x => x.Rotulo).ToList();
        var confianca = lista.Count == 0 ? 0 : (double)lista.Average(x => x.NivelConfianca);

        var dados = new RelatorioDados
        {
            Lista = lista, PorEpi = porEpi, PorDia = porDia, PorCamera = porCamera,
            ConfiancaMedia = confianca, DiasNoPeriodo = dias
        };
        return new RelatorioDados
        {
            Lista = lista, PorEpi = porEpi, PorDia = porDia, PorCamera = porCamera,
            ConfiancaMedia = confianca, DiasNoPeriodo = dias,
            Texto = Descrever(dados, inicio, fim, camera)
        };
    }

    public static string D(DateOnly d) => d.ToString("dd/MM/yyyy", Pt);

    private static string Plural(int n, string singular, string plural) => n == 1 ? singular : plural;

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpper(s[0], Pt) + s[1..];

    private static string Descrever(RelatorioDados d, DateOnly inicio, DateOnly fim, string camera)
    {
        var periodo = inicio == fim ? $"em {D(inicio)}" : $"no período de {D(inicio)} a {D(fim)}";
        var escopo = camera != "" ? $"na câmera {camera}" : "em todas as câmeras";

        if (d.Total == 0)
            return $"{Cap(periodo)}, o SafeGuard IA não registrou nenhuma infração de EPI {escopo}.";

        var frases = new List<string>
        {
            $"{Cap(periodo)}, o SafeGuard IA registrou {d.Total} {Plural(d.Total, "infração", "infrações")} de EPI {escopo}."
        };

        if (camera == "" && d.PorCamera.Count > 1)
            frases.Add("Por câmera: " + string.Join(", ", d.PorCamera.Select(c => $"{c.Rotulo} ({c.Qtd})")) + ".");

        if (d.PorEpi.Count > 0)
        {
            var top = d.PorEpi[0];
            var pct = (double)top.Qtd / d.Total;
            frases.Add($"O equipamento mais frequentemente ausente foi {top.Rotulo}, apontado em {top.Qtd} " +
                       $"{Plural(top.Qtd, "ocorrência", "ocorrências")} ({pct.ToString("P1", Pt)} das infrações).");
            if (d.PorEpi.Count > 1)
                frases.Add("Também houve ausência de: " +
                           string.Join(", ", d.PorEpi.Skip(1).Select(e => $"{e.Rotulo} ({e.Qtd})")) + ".");
        }

        if (d.PorDia.Count > 0)
        {
            var pico = d.PorDia.OrderByDescending(x => x.Qtd).First();
            frases.Add($"O dia com mais ocorrências foi {pico.Rotulo} ({pico.Qtd} {Plural(pico.Qtd, "registro", "registros")}).");
            if (d.DiasNoPeriodo > 1)
            {
                var media = (double)d.Total / d.DiasNoPeriodo;
                frases.Add($"Houve ocorrências em {d.PorDia.Count} de {d.DiasNoPeriodo} dias do período, " +
                           $"com média de {media.ToString("0.0#", Pt)} infrações por dia.");
            }
        }

        var hora = d.Lista.GroupBy(x => x.DataHora.ToLocalTime().Hour)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First();
        frases.Add($"O horário de maior incidência foi entre {hora.Key:00}h e {(hora.Key + 1) % 24:00}h " +
                   $"({hora.Count()} {Plural(hora.Count(), "ocorrência", "ocorrências")}).");

        frases.Add($"A confiança média das detecções da IA foi de {d.ConfiancaMedia.ToString("P1", Pt)}.");
        return string.Join(" ", frases);
    }
}
