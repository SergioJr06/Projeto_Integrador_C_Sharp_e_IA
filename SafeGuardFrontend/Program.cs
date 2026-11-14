using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using SafeGuardFrontend.Components;
using SafeGuardFrontend.Services;

// Datas, horas e percentuais no padrão brasileiro.
var pt = new CultureInfo("pt-BR");
CultureInfo.DefaultThreadCurrentCulture = pt;
CultureInfo.DefaultThreadCurrentUICulture = pt;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddHttpClient<MonitoramentoService>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5168/");
    // Chave de API (opcional): a mesma "Api:Chave" configurada na API.
    var chave = builder.Configuration["Api:Chave"];
    if (!string.IsNullOrWhiteSpace(chave))
        client.DefaultRequestHeaders.Add("X-Api-Key", chave);
});

builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<CameraConfigService>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/acesso-negado";
        options.Cookie.Name = "safeguard.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        // A cada requisição confere o cadastro: usuário removido sai na hora, e mudança de perfil
        // ou de câmera vale imediatamente, sem precisar entrar de novo.
        options.Events.OnValidatePrincipal = async ctx =>
        {
            var usuarios = ctx.HttpContext.RequestServices.GetRequiredService<AuthService>();
            var atual = usuarios.Obter(ctx.Principal?.Identity?.Name);
            if (atual is null)
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }
            var novo = Acesso.CriarPrincipal(atual);
            if (!Acesso.MesmoAcesso(ctx.Principal!, novo))
            {
                ctx.ReplacePrincipal(novo);
                ctx.ShouldRenew = true;
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}
// "no-cache" = o navegador sempre confere se app.css/charts.js mudaram (evita gráficos/telas antigos após atualizar o projeto).
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache"
});
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
// Entrega blazor.web.js e os arquivos do wwwroot em qualquer ambiente (Development ou Production).
app.MapStaticAssets();

app.MapPost("/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
}).DisableAntiforgery();

// Entrega as fotos de evidência pelo próprio painel: exige login e funciona em outros
// dispositivos (celular), que não enxergam o "localhost" da API.
app.MapGet("/evidencias/{arquivo}", async (string arquivo, HttpContext http, MonitoramentoService api, CancellationToken ct) =>
{
    if (!Regex.IsMatch(arquivo, "^[a-f0-9]{32}\\.jpg$")) return Results.NotFound();
    // Usuário comum só vê a foto de uma infração da própria câmera.
    if (!http.User.EhAdmin())
    {
        var dados = await api.ListarAsync(ct);
        var dono = dados?.FirstOrDefault(x => x.CaminhoEvidencia.EndsWith(arquivo, StringComparison.OrdinalIgnoreCase));
        if (dono is null || !http.User.PodeVerCamera(dono.CameraId)) return Results.NotFound();
    }
    var stream = await api.ObterEvidenciaAsync(arquivo, ct);
    if (stream is null) return Results.NotFound();
    // O nome do arquivo é um GUID e a foto nunca muda: pode ficar em cache no navegador.
    http.Response.Headers.CacheControl = "private, max-age=86400";
    return Results.File(stream, "image/jpeg");
}).RequireAuthorization();

app.MapGet("/exportar.csv", async (int? periodo, string? camera, string? inicio, string? fim,
    HttpContext http, MonitoramentoService api, CancellationToken ct) =>
{
    // Admin escolhe a câmera; usuário comum sempre recebe só a dele (o filtro do endereço é ignorado).
    var usuario = http.User;
    var dados = usuario.Permitidas(await api.ListarAsync(ct) ?? PainelDados.Demo());
    var cam = usuario.EhAdmin() ? camera ?? "" : usuario.CameraDoUsuario();
    var formato = "yyyy-MM-dd";
    List<InfracaoDto> lista =
        DateOnly.TryParseExact(inicio, formato, CultureInfo.InvariantCulture, DateTimeStyles.None, out var di) &&
        DateOnly.TryParseExact(fim, formato, CultureInfo.InvariantCulture, DateTimeStyles.None, out var df)
            ? PainelDados.FiltrarIntervalo(dados, di <= df ? di : df, di <= df ? df : di, cam)
            : PainelDados.Filtrar(dados, periodo is 0 or 7 or 30 ? periodo.Value : 7, cam);
    var corpo = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(PainelDados.Csv(lista))).ToArray();
    return Results.File(corpo, "text/csv; charset=utf-8", $"safeguard-infracoes-{DateTime.Now:yyyyMMdd-HHmm}.csv");
}).RequireAuthorization();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
