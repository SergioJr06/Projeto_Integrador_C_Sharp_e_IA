using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace SafeGuardFrontend.Services;

/// <summary>
/// Permissionamento: o administrador vê e altera tudo; o usuário comum só visualiza
/// (painel, gráficos, imagens e relatórios) da câmera pela qual é responsável.
/// </summary>
public static class Acesso
{
    public const string ClaimNome = "nome";
    public const string ClaimCamera = "camera";

    public static ClaimsPrincipal CriarPrincipal(UsuarioConfig u)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, u.Usuario),
            new(ClaimNome, string.IsNullOrWhiteSpace(u.Nome) ? u.Usuario : u.Nome),
            new(ClaimTypes.Role, u.Perfil)
        };
        if (!string.IsNullOrWhiteSpace(u.CameraId)) claims.Add(new Claim(ClaimCamera, u.CameraId));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    /// <summary>True quando perfil, câmera e nome do cookie ainda batem com o cadastro atual.</summary>
    public static bool MesmoAcesso(ClaimsPrincipal a, ClaimsPrincipal b) => Chave(a) == Chave(b);

    private static string Chave(ClaimsPrincipal p) => string.Join("|",
        p.FindFirst(ClaimTypes.Role)?.Value ?? "",
        p.FindFirst(ClaimCamera)?.Value ?? "",
        p.FindFirst(ClaimNome)?.Value ?? "");

    public static bool EhAdmin(this ClaimsPrincipal usuario) => usuario.IsInRole(Perfis.Admin);

    public static string CameraDoUsuario(this ClaimsPrincipal usuario) => usuario.FindFirst(ClaimCamera)?.Value ?? "";

    public static bool PodeVerCamera(this ClaimsPrincipal usuario, string cameraId)
    {
        if (usuario.EhAdmin()) return true;
        var minha = usuario.CameraDoUsuario();
        return minha != "" && string.Equals(minha, cameraId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Infrações que este usuário pode enxergar.</summary>
    public static List<InfracaoDto> Permitidas(this ClaimsPrincipal usuario, IEnumerable<InfracaoDto> todas) =>
        usuario.EhAdmin() ? todas.ToList() : todas.Where(x => usuario.PodeVerCamera(x.CameraId)).ToList();

    public static string Papel(this ClaimsPrincipal usuario)
    {
        if (usuario.EhAdmin()) return "Administrador";
        var cam = usuario.CameraDoUsuario();
        return cam == "" ? "Sem câmera atribuída" : "Responsável · " + cam;
    }
}
