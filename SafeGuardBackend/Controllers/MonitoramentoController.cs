using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SafeGuardBackend.Data;
using SafeGuardBackend.Models;
using SafeGuardBackend.Services;

namespace SafeGuardBackend.Controllers;

[ApiController]
[Route("api/monitoramento")]
public class MonitoramentoController : ControllerBase
{
    private const long MaxImagemBytes = 10 * 1024 * 1024;
    private readonly AppDbContext _context;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<MonitoramentoController> _logger;

    private readonly IConfiguration _configuration;

    public MonitoramentoController(AppDbContext context, IWebHostEnvironment environment,
        ILogger<MonitoramentoController> logger, IConfiguration configuration)
    { _context = context; _environment = environment; _logger = logger; _configuration = configuration; }

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxImagemBytes + 1024 * 1024)]
    public async Task<IActionResult> Receber([FromForm] MonitoramentoRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CameraId))
            return BadRequest(new { mensagem = "CameraId é obrigatório." });
        if (string.IsNullOrWhiteSpace(request.EpisFaltantes))
            return BadRequest(new { mensagem = "EpisFaltantes é obrigatório." });
        if (request.NivelConfianca is < 0 or > 1)
            return BadRequest(new { mensagem = "NivelConfianca deve estar entre 0 e 1." });
        if (request.Imagem is null || request.Imagem.Length == 0)
            return BadRequest(new { mensagem = "Imagem é obrigatória." });
        if (request.Imagem.Length > MaxImagemBytes)
            return BadRequest(new { mensagem = "A imagem deve ter no máximo 10 MB." });

        // Não confiar apenas no nome/extensão enviada pelo cliente: validar assinatura JPEG.
        await using var input = request.Imagem.OpenReadStream();
        var assinatura = new byte[3];
        var lidos = await input.ReadAsync(assinatura.AsMemory(0, 3), cancellationToken);
        if (lidos != 3 || assinatura[0] != 0xFF || assinatura[1] != 0xD8 || assinatura[2] != 0xFF)
            return BadRequest(new { mensagem = "O arquivo enviado não parece ser um JPEG válido." });
        input.Position = 0;

        var pasta = EvidenciasPasta.Resolver(_configuration, _environment);
        Directory.CreateDirectory(pasta);
        var nome = $"{Guid.NewGuid():N}.jpg";
        var caminho = Path.Combine(pasta, nome);
        var caminhoRelativo = $"/evidencias/{nome}";
        try
        {
            await using (var output = new FileStream(caminho, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await input.CopyToAsync(output, cancellationToken);

            var infracao = new Infracao
            {
                CameraId = request.CameraId.Trim(),
                EpisFaltantes = request.EpisFaltantes.Trim(),
                NivelConfianca = request.NivelConfianca,
                CaminhoEvidencia = caminhoRelativo,
                DataHora = DateTime.UtcNow
            };
            _context.Infracoes.Add(infracao);
            await _context.SaveChangesAsync(cancellationToken);
            return CreatedAtAction(nameof(Consultar), new { id = infracao.Id }, new
            { infracao.Id, mensagem = "Detecção registrada.", caminhoEvidencia = caminhoRelativo });
        }
        catch (Exception ex)
        {
            if (System.IO.File.Exists(caminho)) System.IO.File.Delete(caminho);
            _logger.LogError(ex, "Falha ao registrar detecção da câmera {CameraId}", request.CameraId);
            throw;
        }
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Infracao>>> Listar(CancellationToken cancellationToken)
    {
        var registros = await _context.Infracoes.AsNoTracking()
            .OrderByDescending(x => x.DataHora).Take(500).ToListAsync(cancellationToken);
        return Ok(registros);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Infracao>> Consultar(int id, CancellationToken cancellationToken)
    {
        var registro = await _context.Infracoes.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return registro is null ? NotFound() : Ok(registro);
    }
}

public sealed class MonitoramentoRequest
{
    public string CameraId { get; set; } = string.Empty;
    public string EpisFaltantes { get; set; } = string.Empty;
    public decimal NivelConfianca { get; set; }
    public IFormFile? Imagem { get; set; }
}
