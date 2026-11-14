using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SafeGuardBackend.Models;

[Table("infracoes")]
public class Infracao
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("camera_id")]
    public string CameraId { get; set; } = string.Empty;

    [Column("epis_faltantes")]
    public string EpisFaltantes { get; set; } = string.Empty;

    [Column("nivel_confianca")]
    public decimal NivelConfianca { get; set; }

    [Column("caminho_evidencia")]
    public string CaminhoEvidencia { get; set; } = string.Empty;

    [Column("data_hora")]
    public DateTime DataHora { get; set; } = DateTime.UtcNow;
}