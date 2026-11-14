using Microsoft.EntityFrameworkCore;
using SafeGuardBackend.Models;

namespace SafeGuardBackend.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Infracao> Infracoes { get; set; }
}