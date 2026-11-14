using System.Text.Json;
using System.Text.Json.Nodes;

namespace SafeGuardFrontend.Services;

public sealed class EpiOpcao
{
    public string Chave { get; init; } = "";
    public string Rotulo { get; init; } = "";
    /// <summary>False quando o modelo YOLO atual não possui a classe "sem ..." desse EPI.</summary>
    public bool ModeloDetecta { get; init; } = true;
}

public sealed class CameraConfig
{
    public string Id { get; set; } = "";
    public string Nome { get; set; } = "";
    public string Fonte { get; set; } = "0";
    public Dictionary<string, bool> Epis { get; set; } = new();
}

/// <summary>Estado do motor de IA, publicado por ele em config/status_motor.json.</summary>
public sealed class StatusMotor
{
    public bool Online { get; init; }
    public string Estado { get; init; } = "";
    public string Camera { get; init; } = "";
    public string Nome { get; init; } = "";
    public string Source { get; init; } = "";
    public string Revisao { get; init; } = "";
    public List<string> Epis { get; init; } = new();
}

/// <summary>
/// Lê e grava config/config.json, o mesmo arquivo que o motor Python relê a cada ~1 s.
/// Campos desconhecidos do arquivo são preservados.
/// </summary>
public sealed class CameraConfigService(IConfiguration configuration, IWebHostEnvironment environment)
{
    public static readonly IReadOnlyList<EpiOpcao> Opcoes = new List<EpiOpcao>
    {
        new() { Chave = "require_helmet", Rotulo = "Capacete" },
        new() { Chave = "require_glasses", Rotulo = "Óculos" },
        new() { Chave = "require_gloves", Rotulo = "Luvas" },
        new() { Chave = "require_boots", Rotulo = "Botas" },
        new() { Chave = "require_ear_protection", Rotulo = "Protetor auricular" },
        new() { Chave = "require_vest", Rotulo = "Colete" },
        new() { Chave = "require_mask", Rotulo = "Máscara" }
    };

    private static readonly JsonSerializerOptions Escrita = new() { WriteIndented = true };
    private static readonly SemaphoreSlim Trava = new(1, 1);

    public string Caminho => Path.GetFullPath(Path.Combine(
        environment.ContentRootPath, configuration["CamerasConfigPath"] ?? "../config/config.json"));

    public static CameraConfig NovaCamera(string id, string nome, string fonte)
    {
        var cam = new CameraConfig { Id = id, Nome = nome, Fonte = fonte };
        foreach (var op in Opcoes) cam.Epis[op.Chave] = false;
        return cam;
    }

    public async Task<List<CameraConfig>> ListarAsync()
    {
        var raiz = await LerAsync();
        var lista = new List<CameraConfig>();
        if (raiz["cameras"] is JsonObject cameras)
        {
            foreach (var par in cameras)
            {
                if (par.Value is not JsonObject obj) continue;
                var cam = NovaCamera(par.Key, obj["nome"]?.ToString() ?? par.Key, obj["source"]?.ToString() ?? "0");
                foreach (var op in Opcoes)
                    cam.Epis[op.Chave] = obj[op.Chave] is JsonValue v && v.TryGetValue<bool>(out var ativo) && ativo;
                lista.Add(cam);
            }
        }
        return lista;
    }

    /// <returns>Código da revisão gravada; o motor de IA o devolve no status quando aplica a configuração.</returns>
    public async Task<string> SalvarAsync(IEnumerable<CameraConfig> cameras, string? ativa)
    {
        await Trava.WaitAsync();
        try
        {
            var raiz = await LerAsync();
            var antigas = raiz["cameras"] as JsonObject ?? new JsonObject();
            var novas = new JsonObject();
            foreach (var cam in cameras)
            {
                var obj = antigas[cam.Id] is JsonObject existente
                    ? (JsonObject)existente.DeepClone()
                    : new JsonObject();
                obj["nome"] = cam.Nome.Trim();
                var fonte = cam.Fonte.Trim();
                // "0", "1"... = índice de webcam (número); qualquer outra coisa = endereço RTSP/arquivo (texto)
                obj["source"] = int.TryParse(fonte, out var indice) && indice >= 0
                    ? JsonValue.Create(indice)
                    : JsonValue.Create(fonte);
                foreach (var op in Opcoes)
                    obj[op.Chave] = cam.Epis.TryGetValue(op.Chave, out var ativo) && ativo;
                novas[cam.Id] = obj;
            }
            raiz["cameras"] = novas;
            if (!string.IsNullOrWhiteSpace(ativa) && novas.ContainsKey(ativa)) raiz["ativa"] = ativa;
            else raiz.Remove("ativa");
            var revisao = DateTime.UtcNow.Ticks.ToString();
            raiz["revisao"] = revisao;

            // Grava em arquivo temporário e troca, para o Python nunca ler um arquivo pela metade.
            var temp = Caminho + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(Caminho)!);
            await File.WriteAllTextAsync(temp, raiz.ToJsonString(Escrita));
            // No Windows a troca pode falhar por um instante se o Python estiver lendo o arquivo.
            for (var tentativa = 1; ; tentativa++)
            {
                try { File.Move(temp, Caminho, overwrite: true); break; }
                catch (IOException) when (tentativa < 8) { await Task.Delay(150); }
            }
            return revisao;
        }
        finally { Trava.Release(); }
    }

    public async Task<string> LerAtivaAsync()
    {
        var raiz = await LerAsync();
        return raiz["ativa"]?.ToString() ?? "";
    }

    public string CaminhoStatus => Path.Combine(Path.GetDirectoryName(Caminho)!, "status_motor.json");

    /// <summary>Lê o status do motor; considera "offline" se ele não atualizou o arquivo nos últimos 10 s.</summary>
    public StatusMotor LerStatus()
    {
        try
        {
            if (!File.Exists(CaminhoStatus)) return new StatusMotor();
            if (JsonNode.Parse(File.ReadAllText(CaminhoStatus)) is not JsonObject no) return new StatusMotor();
            var atualizadoEm = no["atualizado_em"]?.GetValue<double>() ?? 0;
            var agora = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            var online = (no["online"]?.GetValue<bool>() ?? false) && agora - atualizadoEm < 10;
            return new StatusMotor
            {
                Online = online,
                Estado = no["estado"]?.ToString() ?? "",
                Camera = no["camera"]?.ToString() ?? "",
                Nome = no["nome"]?.ToString() ?? "",
                Source = no["source"]?.ToString() ?? "",
                Revisao = no["revisao"]?.ToString() ?? "",
                Epis = (no["epis"] as JsonArray)?
                    .Select(x => x?.ToString() ?? "").Where(x => x != "")
                    .Select(chave => Opcoes.FirstOrDefault(o => o.Chave == chave)?.Rotulo ?? chave)
                    .ToList() ?? new()
            };
        }
        catch (Exception) { return new StatusMotor(); }
    }

    /// <summary>Espera o motor de IA confirmar que aplicou a revisão salva (ou o tempo acabar).</summary>
    public async Task<StatusMotor> AguardarAplicacaoAsync(string revisao, TimeSpan limite)
    {
        var fim = DateTime.UtcNow + limite;
        var status = LerStatus();
        if (!status.Online) return status; // motor desligado: não adianta esperar
        while (!(status.Online && status.Revisao == revisao) && DateTime.UtcNow < fim)
        {
            await Task.Delay(250);
            status = LerStatus();
        }
        return status;
    }

    private async Task<JsonObject> LerAsync()
    {
        if (!File.Exists(Caminho)) return new JsonObject { ["cameras"] = new JsonObject() };
        var texto = await File.ReadAllTextAsync(Caminho);
        return JsonNode.Parse(texto) as JsonObject ?? new JsonObject { ["cameras"] = new JsonObject() };
    }
}
