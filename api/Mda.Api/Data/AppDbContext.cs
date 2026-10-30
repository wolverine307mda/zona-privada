using Mda.Api.Gastos;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Data;

// Tablas de usuarios de Identity y de cada módulo (gastos…). Los módulos nuevos añaden aquí sus DbSet.
public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<Movimiento> Movimientos => Set<Movimiento>();
    public DbSet<Recurrente> Recurrentes => Set<Recurrente>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Movimiento>(e =>
        {
            e.ToTable("movimientos");
            e.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Importe).HasPrecision(12, 2);
            e.Property(x => x.Litros).HasPrecision(8, 2);
            e.Property(x => x.PrecioLitro).HasPrecision(6, 3);
            e.Property(x => x.Categoria).HasMaxLength(40);
            e.Property(x => x.Concepto).HasMaxLength(120);
            e.Property(x => x.Notas).HasMaxLength(500);
            e.HasIndex(x => x.Fecha);
            e.HasIndex(x => new { x.Categoria, x.Fecha });
            // si se borra un fijo, sus apuntes se quedan (sin enlace)
            e.HasOne(x => x.Recurrente).WithMany().HasForeignKey(x => x.RecurrenteId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Recurrente>(e =>
        {
            e.ToTable("recurrentes");
            e.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Frecuencia).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.Importe).HasPrecision(12, 2);
            e.Property(x => x.Nombre).HasMaxLength(80);
            e.Property(x => x.Categoria).HasMaxLength(40);
            e.Ignore(x => x.Mensual);
        });
    }
}
