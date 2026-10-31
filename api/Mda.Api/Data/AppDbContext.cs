using Mda.Api.Agenda;
using Mda.Api.Avisos;
using Mda.Api.Conexiones;
using Mda.Api.Gastos;
using Mda.Api.Notas;
using Mda.Api.Seguridad;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Data;

// Tablas de usuarios de Identity y de cada módulo (gastos…). Los módulos nuevos añaden aquí sus DbSet.
public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<Movimiento> Movimientos => Set<Movimiento>();
    public DbSet<Recurrente> Recurrentes => Set<Recurrente>();
    public DbSet<Deuda> Deudas => Set<Deuda>();
    public DbSet<Nota> Notas => Set<Nota>();
    public DbSet<Evento> Eventos => Set<Evento>();
    public DbSet<Conexion> Conexiones => Set<Conexion>();
    public DbSet<SesionAbierta> Sesiones => Set<SesionAbierta>();
    public DbSet<Acceso> Accesos => Set<Acceso>();
    public DbSet<Ajuste> Ajustes => Set<Ajuste>();
    public DbSet<Hucha> Huchas => Set<Hucha>();
    public DbSet<Presupuesto> Presupuestos => Set<Presupuesto>();
    public DbSet<Mantenimiento> Mantenimientos => Set<Mantenimiento>();
    public DbSet<Adjunto> Adjuntos => Set<Adjunto>();
    public DbSet<PushSuscripcion> Push => Set<PushSuscripcion>();

    // PostgreSQL guarda «timestamp with time zone» en UTC y Npgsql no acepta otro desfase:
    // toda fecha con hora pasa a UTC al escribirla o al usarla en una consulta
    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        c.Properties<DateTimeOffset>().HaveConversion<UtcConverter>();
        c.Properties<DateTimeOffset?>().HaveConversion<UtcConverter>();
    }

    private class UtcConverter() : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTimeOffset, DateTimeOffset>(d => d.ToUniversalTime(), d => d);

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
            e.HasOne<Deuda>().WithMany().HasForeignKey(x => x.DeudaId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Deuda>(e =>
        {
            e.ToTable("deudas");
            e.Property(x => x.Nombre).HasMaxLength(80);
            e.Property(x => x.Total).HasPrecision(12, 2);
            e.Property(x => x.Categoria).HasMaxLength(40);
            e.Property(x => x.Notas).HasMaxLength(500);
        });

        b.Entity<Nota>(e =>
        {
            e.ToTable("notas");
            e.Property(x => x.Titulo).HasMaxLength(200);
            e.Property(x => x.Color).HasMaxLength(20);
            e.Property(x => x.Etiquetas).HasMaxLength(900);
            e.HasIndex(x => new { x.Archivada, x.Fijada, x.Modificado });
        });

        b.Entity<Evento>(e =>
        {
            e.ToTable("eventos");
            e.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(14);
            e.Property(x => x.Repetir).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Titulo).HasMaxLength(200);
            e.Property(x => x.Notas).HasMaxLength(4000);
            e.Property(x => x.Lugar).HasMaxLength(200);
            e.Property(x => x.Color).HasMaxLength(20);
            e.Property(x => x.GoogleId).HasMaxLength(1024);
            e.HasIndex(x => x.Inicio);
            e.HasIndex(x => x.ProximoAviso);
        });

        b.Entity<Conexion>(e =>
        {
            e.ToTable("conexiones");
            e.HasKey(x => x.Proveedor);
            e.Property(x => x.Proveedor).HasMaxLength(20);
            e.Property(x => x.Cuenta).HasMaxLength(200);
        });

        b.Entity<SesionAbierta>(e =>
        {
            e.ToTable("sesiones");
            e.Property(x => x.Id).HasMaxLength(32);
            e.Property(x => x.UsuarioId).HasMaxLength(450);
            e.Property(x => x.Ip).HasMaxLength(64);
            e.Property(x => x.Agente).HasMaxLength(400);
            e.HasIndex(x => x.UltimaVez);
        });

        b.Entity<Acceso>(e =>
        {
            e.ToTable("accesos");
            e.Property(x => x.Ip).HasMaxLength(64);
            e.Property(x => x.Agente).HasMaxLength(400);
            e.Property(x => x.Motivo).HasMaxLength(80);
        });

        b.Entity<Ajuste>(e =>
        {
            e.ToTable("ajustes");
            e.HasKey(x => x.Clave);
            e.Property(x => x.Clave).HasMaxLength(60);
            e.Property(x => x.Valor).HasMaxLength(2000);
        });

        b.Entity<Hucha>(e =>
        {
            e.ToTable("huchas");
            e.Property(x => x.Nombre).HasMaxLength(80);
            e.Property(x => x.Objetivo).HasPrecision(12, 2);
            e.Property(x => x.Ahorrado).HasPrecision(12, 2);
            e.Property(x => x.Color).HasMaxLength(20);
            e.Property(x => x.Emoji).HasMaxLength(16);
        });

        b.Entity<Presupuesto>(e =>
        {
            e.ToTable("presupuestos");
            e.HasKey(x => x.Categoria);
            e.Property(x => x.Categoria).HasMaxLength(40);
            e.Property(x => x.Limite).HasPrecision(12, 2);
            e.Property(x => x.AvisadoMes).HasMaxLength(7);
        });

        b.Entity<Mantenimiento>(e =>
        {
            e.ToTable("mantenimientos");
            e.Property(x => x.Nombre).HasMaxLength(80);
            e.Property(x => x.Notas).HasMaxLength(500);
        });

        b.Entity<Adjunto>(e =>
        {
            e.ToTable("adjuntos");
            e.HasKey(x => x.Clave);
            e.Property(x => x.Clave).HasMaxLength(60);
            e.Property(x => x.Tipo).HasMaxLength(40);
        });

        b.Entity<PushSuscripcion>(e =>
        {
            e.ToTable("push");
            e.Property(x => x.Endpoint).HasMaxLength(1000);
            e.Property(x => x.P256dh).HasMaxLength(200);
            e.Property(x => x.Auth).HasMaxLength(100);
            e.Property(x => x.Dispositivo).HasMaxLength(120);
            e.HasIndex(x => x.Endpoint).IsUnique();
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
