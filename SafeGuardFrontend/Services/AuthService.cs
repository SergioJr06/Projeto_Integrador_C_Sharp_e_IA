using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SafeGuardFrontend.Services;

public static class Perfis
{
    public const string Admin = "admin";
    public const string Usuario = "usuario";

    public static string Rotulo(string perfil) => perfil == Admin ? "Administrador" : "Usuário";
}

public sealed class UsuarioConfig
{
    public string Usuario { get; set; } = "";
    public string Nome { get; set; } = "";
    public string SenhaHash { get; set; } = "";
    /// <summary>"admin" (tudo) ou "usuario" (só visualiza a própria câmera).</summary>
    public string Perfil { get; set; } = "";
    /// <summary>Câmera sob responsabilidade do usuário (ex.: cam_0). Vazio para administradores.</summary>
    public string CameraId { get; set; } = "";
}

/// <summary>
/// Cadastro de usuários do painel. Fica em config/usuarios.json (senha sempre como hash PBKDF2).
/// Na primeira execução o arquivo é criado a partir de "Auth:Usuarios" do appsettings,
/// e esses usuários iniciais entram como administradores. Para recuperar o acesso, apague o
/// usuarios.json e reinicie o painel: ele é recriado a partir do appsettings.
/// </summary>
public sealed class AuthService(IConfiguration configuration, IWebHostEnvironment environment)
{
    private const int Iteracoes = 100_000;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly Regex LoginValido = new("^[A-Za-z0-9._-]{3,32}$", RegexOptions.Compiled);

    private readonly object trava = new();
    private List<UsuarioConfig>? cache;
    private DateTime cacheData;

    public string Caminho => Path.GetFullPath(Path.Combine(
        environment.ContentRootPath, configuration["UsuariosPath"] ?? "../config/usuarios.json"));

    // ---------- Consulta ----------

    public UsuarioConfig? Validar(string? usuario, string? senha)
    {
        if (string.IsNullOrWhiteSpace(usuario) || string.IsNullOrEmpty(senha)) return null;
        var encontrado = Obter(usuario);
        return encontrado is not null && Conferir(senha, encontrado.SenhaHash) ? encontrado : null;
    }

    public UsuarioConfig? Obter(string? usuario)
    {
        if (string.IsNullOrWhiteSpace(usuario)) return null;
        lock (trava)
        {
            var achado = Carregar().FirstOrDefault(x =>
                string.Equals(x.Usuario, usuario.Trim(), StringComparison.OrdinalIgnoreCase));
            return achado is null ? null : Copiar(achado);
        }
    }

    public List<UsuarioConfig> Listar()
    {
        lock (trava) return Carregar().Select(Copiar).OrderBy(x => x.Nome, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    // ---------- Alterações (retornam a mensagem de erro, ou null quando deu certo) ----------

    public string? Adicionar(string? nome, string? usuario, string? senha, string? perfil, string? cameraId)
    {
        nome = (nome ?? "").Trim();
        usuario = (usuario ?? "").Trim();
        perfil = NormalizarPerfil(perfil);
        cameraId = perfil == Perfis.Admin ? "" : (cameraId ?? "").Trim();

        var erro = ValidarNome(nome) ?? ValidarSenha(senha, obrigatoria: true);
        if (erro is not null) return erro;
        if (!LoginValido.IsMatch(usuario))
            return "O usuário deve ter de 3 a 32 caracteres: letras, números, ponto, hífen ou sublinhado.";
        if (perfil == Perfis.Usuario && cameraId == "")
            return "Escolha a câmera pela qual este usuário será responsável.";

        lock (trava)
        {
            var lista = Carregar().Select(Copiar).ToList();
            if (lista.Any(x => string.Equals(x.Usuario, usuario, StringComparison.OrdinalIgnoreCase)))
                return "Já existe um usuário com esse login.";
            lista.Add(new UsuarioConfig
            {
                Usuario = usuario, Nome = nome, SenhaHash = Hash(senha!),
                Perfil = perfil, CameraId = cameraId
            });
            Gravar(lista);
            return null;
        }
    }

    /// <param name="novaSenha">Vazio mantém a senha atual.</param>
    public string? Atualizar(string usuario, string? nome, string? perfil, string? cameraId, string? novaSenha)
    {
        nome = (nome ?? "").Trim();
        perfil = NormalizarPerfil(perfil);
        cameraId = perfil == Perfis.Admin ? "" : (cameraId ?? "").Trim();

        var erro = ValidarNome(nome) ?? ValidarSenha(novaSenha, obrigatoria: false);
        if (erro is not null) return erro;
        if (perfil == Perfis.Usuario && cameraId == "")
            return "escolha a câmera pela qual este usuário será responsável.";

        lock (trava)
        {
            var lista = Carregar().Select(Copiar).ToList();
            var alvo = lista.FirstOrDefault(x => string.Equals(x.Usuario, usuario, StringComparison.OrdinalIgnoreCase));
            if (alvo is null) return "usuário não encontrado.";
            if (alvo.Perfil == Perfis.Admin && perfil != Perfis.Admin && lista.Count(x => x.Perfil == Perfis.Admin) == 1)
                return "é o único administrador e não pode virar usuário comum.";

            alvo.Nome = nome;
            alvo.Perfil = perfil;
            alvo.CameraId = cameraId;
            if (!string.IsNullOrEmpty(novaSenha)) alvo.SenhaHash = Hash(novaSenha);
            Gravar(lista);
            return null;
        }
    }

    public string? Remover(string usuario, string quemPede)
    {
        if (string.Equals(usuario, quemPede, StringComparison.OrdinalIgnoreCase))
            return "Você não pode remover o próprio usuário.";
        lock (trava)
        {
            var lista = Carregar().Select(Copiar).ToList();
            var alvo = lista.FirstOrDefault(x => string.Equals(x.Usuario, usuario, StringComparison.OrdinalIgnoreCase));
            if (alvo is null) return "Usuário não encontrado.";
            if (alvo.Perfil == Perfis.Admin && lista.Count(x => x.Perfil == Perfis.Admin) == 1)
                return "Não é possível remover o único administrador.";
            lista.Remove(alvo);
            Gravar(lista);
            return null;
        }
    }

    // ---------- Senhas ----------

    public static string Hash(string senha)
    {
        var sal = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(senha, sal, Iteracoes, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2${Iteracoes}${Convert.ToBase64String(sal)}${Convert.ToBase64String(hash)}";
    }

    private static bool Conferir(string senha, string armazenado)
    {
        var partes = armazenado.Split('$');
        if (partes.Length != 4 || partes[0] != "pbkdf2" || !int.TryParse(partes[1], out var iteracoes))
            return false;
        try
        {
            var salt = Convert.FromBase64String(partes[2]);
            var esperado = Convert.FromBase64String(partes[3]);
            var calculado = Rfc2898DeriveBytes.Pbkdf2(senha, salt, iteracoes, HashAlgorithmName.SHA256, esperado.Length);
            return CryptographicOperations.FixedTimeEquals(calculado, esperado);
        }
        catch (FormatException) { return false; }
    }

    // ---------- Validação e armazenamento ----------

    private static string NormalizarPerfil(string? perfil) =>
        string.Equals(perfil, Perfis.Admin, StringComparison.OrdinalIgnoreCase) ? Perfis.Admin : Perfis.Usuario;

    private static string? ValidarNome(string nome) =>
        nome.Length is < 2 or > 60 ? "Informe o nome (de 2 a 60 caracteres)." : null;

    private static string? ValidarSenha(string? senha, bool obrigatoria)
    {
        if (string.IsNullOrEmpty(senha)) return obrigatoria ? "Informe uma senha de pelo menos 8 caracteres." : null;
        return senha.Length is < 8 or > 128 ? "A senha deve ter de 8 a 128 caracteres." : null;
    }

    private static UsuarioConfig Copiar(UsuarioConfig x) => new()
    {
        Usuario = x.Usuario, Nome = x.Nome, SenhaHash = x.SenhaHash, Perfil = x.Perfil, CameraId = x.CameraId
    };

    private List<UsuarioConfig> Semente()
    {
        var lista = configuration.GetSection("Auth:Usuarios").Get<List<UsuarioConfig>>() ?? new();
        // Quem vem do appsettings sem perfil é o(s) administrador(es) inicial(is).
        foreach (var u in lista)
            u.Perfil = string.IsNullOrWhiteSpace(u.Perfil) ? Perfis.Admin : NormalizarPerfil(u.Perfil);
        return lista;
    }

    /// <summary>Chamar sempre dentro de lock. Não altere a lista devolvida: use Copiar.</summary>
    private List<UsuarioConfig> Carregar()
    {
        var caminho = Caminho;
        if (!File.Exists(caminho))
        {
            var semente = Semente();
            try { Gravar(semente); } catch (IOException) { }
            return semente;
        }
        var data = File.GetLastWriteTimeUtc(caminho);
        if (cache is not null && data == cacheData) return cache;
        try
        {
            var lista = JsonSerializer.Deserialize<List<UsuarioConfig>>(File.ReadAllText(caminho), Json) ?? new();
            foreach (var u in lista) u.Perfil = NormalizarPerfil(u.Perfil);
            cache = lista;
            cacheData = data;
            return lista;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return Semente(); // arquivo ilegível: o administrador do appsettings continua conseguindo entrar
        }
    }

    private void Gravar(List<UsuarioConfig> lista)
    {
        var caminho = Caminho;
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        // Grava em arquivo temporário e troca, para nunca deixar o cadastro pela metade.
        var temp = caminho + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(lista, Json));
        File.Move(temp, caminho, overwrite: true);
        cache = lista;
        cacheData = File.GetLastWriteTimeUtc(caminho);
    }
}
