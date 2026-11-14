namespace SafeGuardFrontend.Services;

/// <summary>
/// O motor de IA envia o nome da classe do modelo (ex.: "sem capacete", "sem oculos").
/// Aqui o texto vira um rótulo legível: "Capacete", "Óculos".
/// </summary>
public static class EpiFormatter
{
    private static readonly Dictionary<string, string> Nomes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["capacete"] = "Capacete",
        ["oculos"] = "Óculos",
        ["óculos"] = "Óculos",
        ["luvas"] = "Luvas",
        ["bota"] = "Bota",
        ["botas"] = "Botas",
        ["protetor auricular"] = "Protetor auricular",
        ["mascara"] = "Máscara",
        ["máscara"] = "Máscara",
        ["colete"] = "Colete"
    };

    public static string Formatar(string? bruto)
    {
        var texto = (bruto ?? "").Trim();
        if (texto.StartsWith("sem ", StringComparison.OrdinalIgnoreCase)) texto = texto[4..].Trim();
        if (texto.Length == 0) return "Não informado";
        if (Nomes.TryGetValue(texto, out var nome)) return nome;
        return char.ToUpper(texto[0]) + texto[1..];
    }

    public static IEnumerable<string> Dividir(string? lista) =>
        (lista ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Formatar)
            .Distinct();
}
