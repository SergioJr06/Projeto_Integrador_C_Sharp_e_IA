using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using SafeGuardBackend.Data;
using SafeGuardBackend.Services;
using System.Security.Cryptography;
using System.Text;

// A IA envia números com ponto (0.87). Em Windows pt-BR a leitura padrão de formulários usa
// vírgula como decimal e interpretaria "0.87" como 87, gerando erro 400. Fixa o formato internacional.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddCors(options => options.AddPolicy("BlazorFrontend", policy =>
    policy.WithOrigins("http://localhost:5080", "https://localhost:7080", "http://localhost:5168", "https://localhost:7205")
          .AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    // Em desenvolvimento o motor Python envia para http://localhost:5168; um redirecionamento
    // para HTTPS quebraria o POST (certificado de desenvolvimento não confiável).
    app.UseHttpsRedirection();
}
app.UseCors("BlazorFrontend");

var chaveApi = app.Configuration["Api:Chave"];
if (!string.IsNullOrWhiteSpace(chaveApi))
{
    var chaveEsperada = Encoding.UTF8.GetBytes(chaveApi);
    app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsOptions(context.Request.Method))
        {
            var enviada = Encoding.UTF8.GetBytes(context.Request.Headers["X-Api-Key"].ToString());
            // Comparação em tempo constante, para não vazar a chave por diferença de tempo.
            if (!CryptographicOperations.FixedTimeEquals(chaveEsperada, enviada))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { mensagem = "Chave de API ausente ou inválida." });
                return;
            }
        }
        await next();
    });
}

// Serve as fotos de evidência da pasta configurada (padrão: wwwroot/evidencias).
var pastaEvidencias = EvidenciasPasta.Resolver(app.Configuration, app.Environment);
Directory.CreateDirectory(pastaEvidencias);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(pastaEvidencias),
    RequestPath = "/evidencias"
});
app.UseStaticFiles();
app.UseAuthorization();
app.MapControllers();
app.Run();
