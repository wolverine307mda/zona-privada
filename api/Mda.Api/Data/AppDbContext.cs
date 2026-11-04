using Mda.Api.Agenda;
using Mda.Api.Atajos;
using Mda.Api.Avisos;
using Mda.Api.Conexiones;
using Mda.Api.Galeria;
using Mda.Api.Pc;
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
    public DbSet<Mda.Api.Auth.PasskeyInfo> PasskeysInfo => Set<Mda.Api.Auth.PasskeyInfo>();
    public DbSet<Ajuste> Ajustes => Set<Ajuste>();
    public DbSet<Hucha> Huchas => Set<Hucha>();
    public DbSet<Mda.Api.Pc.SaludDia> SaludDias => Set<Mda.Api.Pc.SaludDia>();
    public DbSet<Mda.Api.Pc.DispositivoRed> DispositivosRed => Set<Mda.Api.Pc.DispositivoRed>();
    public DbSet<Mda.Api.Pc.ElementoPortapapeles> Portapapeles => Set<Mda.Api.Pc.ElementoPortapapeles>();
    public DbSet<Presupuesto> Presupuestos => Set<Presupuesto>();
    public DbSet<Mantenimiento> Mantenimientos => Set<Mantenimiento>();
    public DbSet<Adjunto> Adjuntos => Set<Adjunto>();
    public DbSet<PushSuscripcion> Push => Set<PushSuscripcion>();
    public DbSet<FotoPublica> FotosPublicas => Set<FotoPublica>();
    public DbSet<Senuelo> Senuelos => Set<Senuelo>();
    public DbSet<OrdenPc> OrdenesPc => Set<OrdenPc>();
    public DbSet<TokenAtajo> TokensAtajo => Set<TokenAtajo>();
    public DbSet<Secreto> Secretos => Set<Secreto>();
    public DbSet<Mda.Api.Pc.SaludBateria> SaludBaterias => Set<Mda.Api.Pc.SaludBateria>();
    public DbSet<Mda.Api.Pc.Escena> Escenas => Set<Mda.Api.Pc.Escena>();
    public DbSet<Mda.Api.Seguridad.SondaRegistrada> SondasRegistradas => Set<Mda.Api.Seguridad.SondaRegistrada>();
    public DbSet<Mda.Api.Gastos.ReglaCategoria> ReglasCategoria => Set<Mda.Api.Gastos.ReglaCategoria>();
    public DbSet<Mda.Api.Domotica.Estancia> Estancias => Set<Mda.Api.Domotica.Estancia>();
    public DbSet<Mda.Api.Domotica.Aparato> Aparatos => Set<Mda.Api.Domotica.Aparato>();
    public DbSet<Mda.Api.Domotica.Entidad> Entidades => Set<Mda.Api.Domotica.Entidad>();
    public DbSet<Mda.Api.Domotica.LecturaCasa> Lecturas => Set<Mda.Api.Domotica.LecturaCasa>();
    public DbSet<Mda.Api.Domotica.PanelCasa> Paneles => Set<Mda.Api.Domotica.PanelCasa>();
    public DbSet<Mda.Api.Domotica.ReglaCasa> Reglas => Set<Mda.Api.Domotica.ReglaCasa>();
    public DbSet<Mda.Api.Domotica.RegistroCasa> RegistrosCasa => Set<Mda.Api.Domotica.RegistroCasa>();
    public DbSet<Mda.Api.Luz.PuntoFoto> PuntosFoto => Set<Mda.Api.Luz.PuntoFoto>();
    public DbSet<Mda.Api.Luz.SalidaFoto> SalidasFoto => Set<Mda.Api.Luz.SalidaFoto>();
    public DbSet<Mda.Api.Pc.MedidaVelocidad> MedidasVelocidad => Set<Mda.Api.Pc.MedidaVelocidad>();
    public DbSet<Mda.Api.Pc.LecturaRed> LecturasRed => Set<Mda.Api.Pc.LecturaRed>();
    public DbSet<Mda.Api.Pc.LecturaHora> LecturasHora => Set<Mda.Api.Pc.LecturaHora>();

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
            e.Property(x => x.Moneda).HasMaxLength(3);
            e.Property(x => x.Periodo).HasMaxLength(7);
            e.Property(x => x.ImporteOriginal).HasPrecision(14, 2);
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
            e.Property(x => x.Titulo).Cifrada("notas"); // cifrados con la clave maestra: sin tope de columna (el cifrado la alarga)
            e.Property(x => x.Contenido).Cifrada("notas");
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

        b.Entity<Mda.Api.Auth.PasskeyInfo>(e =>
        {
            e.ToTable("passkeys_info");
            e.Property(x => x.Id).HasMaxLength(400);
            e.Property(x => x.UsuarioId).HasMaxLength(450);
            e.Property(x => x.AltaIp).HasMaxLength(64);
            e.Property(x => x.Dispositivo).HasMaxLength(80);
            e.Property(x => x.Tipo).HasMaxLength(12);
            e.Property(x => x.UserAgent).HasMaxLength(400);
            e.Property(x => x.Idioma).HasMaxLength(20);
            e.Property(x => x.Zona).HasMaxLength(60);
            e.Property(x => x.Pantalla).HasMaxLength(20);
            e.Property(x => x.UltimaIp).HasMaxLength(64);
            e.Property(x => x.UltimoDispositivo).HasMaxLength(80);
            e.Property(x => x.UltimoMotivo).HasMaxLength(20);
        });

        b.Entity<Mda.Api.Pc.DispositivoRed>(e =>
        {
            e.ToTable("dispositivos_red");
            e.Property(x => x.Nombre).HasMaxLength(60);
            e.Property(x => x.Host).HasMaxLength(120);
            e.Property(x => x.Icono).HasMaxLength(20);
            e.Property(x => x.Mac).HasMaxLength(17);
        });

        b.Entity<Mda.Api.Pc.ElementoPortapapeles>(e =>
        {
            e.ToTable("portapapeles");
            e.Property(x => x.Texto).Cifrada("portapapeles"); // puede llevar contraseñas copiadas
            e.Property(x => x.Origen).HasMaxLength(40);
            e.HasIndex(x => x.Creado);
        });

        b.Entity<Mda.Api.Pc.SaludDia>(e =>
        {
            e.ToTable("salud_dias");
            e.HasKey(x => x.Fecha);
            e.Property(x => x.Datos).HasMaxLength(6000);
        });

        b.Entity<Ajuste>(e =>
        {
            e.ToTable("ajustes");
            e.HasKey(x => x.Clave);
            e.Property(x => x.Clave).HasMaxLength(60);
            e.Property(x => x.Valor).HasMaxLength(2000);
        });

        b.Entity<Mda.Api.Pc.LecturaRed>(e =>
        {
            e.ToTable("red_lecturas");
            e.HasIndex(x => new { x.DispositivoId, x.Hora });
        });

        b.Entity<Mda.Api.Pc.LecturaHora>(e =>
        {
            e.ToTable("red_horas");
            e.HasKey(x => new { x.DispositivoId, x.Hora });
        });

        b.Entity<Mda.Api.Pc.MedidaVelocidad>(e =>
        {
            e.ToTable("velocidades");
            e.HasIndex(x => x.Hora);
        });

        b.Entity<Mda.Api.Seguridad.SondaRegistrada>(e =>
        {
            e.ToTable("sondas_registradas");
            e.Property(x => x.Ip).HasMaxLength(45);
            e.Property(x => x.Ruta).HasMaxLength(200);
            e.Property(x => x.Pais).HasMaxLength(60);
            e.Property(x => x.Ciudad).HasMaxLength(100);
            e.HasIndex(x => x.Hora);
        });

        // ---- Casa (domótica propia) ----
        b.Entity<Mda.Api.Domotica.Estancia>(e =>
        {
            e.ToTable("casa_estancias");
            e.Property(x => x.Nombre).HasMaxLength(30);
            e.Property(x => x.Icono).HasMaxLength(16);
        });
        b.Entity<Mda.Api.Domotica.Aparato>(e =>
        {
            e.ToTable("casa_aparatos");
            e.Property(x => x.Nombre).HasMaxLength(40);
            e.Property(x => x.Tipo).HasMaxLength(16);
            e.Property(x => x.Driver).HasMaxLength(16);
            e.Property(x => x.Config).Cifrada("casa"); // IP, ids y claves locales: cifradas con la clave maestra
            e.HasOne<Mda.Api.Domotica.Estancia>().WithMany().HasForeignKey(x => x.EstanciaId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<Mda.Api.Domotica.Entidad>(e =>
        {
            e.ToTable("casa_entidades");
            e.Property(x => x.Clave).HasMaxLength(24);
            e.Property(x => x.Valor).HasMaxLength(64);
            e.HasIndex(x => new { x.AparatoId, x.Clave }).IsUnique();
            e.HasOne<Mda.Api.Domotica.Aparato>().WithMany().HasForeignKey(x => x.AparatoId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Mda.Api.Domotica.LecturaCasa>(e =>
        {
            e.ToTable("casa_lecturas");
            e.HasIndex(x => new { x.EntidadId, x.Fecha });
            e.HasOne<Mda.Api.Domotica.Entidad>().WithMany().HasForeignKey(x => x.EntidadId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Mda.Api.Domotica.ReglaCasa>(e =>
        {
            e.ToTable("casa_reglas");
            e.Property(x => x.Nombre).HasMaxLength(40);
            e.Property(x => x.SensorClave).HasMaxLength(24);
            e.Property(x => x.Operador).HasMaxLength(1);
            e.Property(x => x.Accion).HasMaxLength(10);
            e.Property(x => x.Modo).HasMaxLength(10);
        });
        b.Entity<Mda.Api.Domotica.RegistroCasa>(e =>
        {
            e.ToTable("casa_registro");
            e.Property(x => x.Texto).HasMaxLength(300);
            e.HasIndex(x => x.Fecha);
        });
        b.Entity<Mda.Api.Domotica.PanelCasa>(e =>
        {
            e.ToTable("casa_paneles");
            e.Property(x => x.Clave).HasMaxLength(24);
            e.HasIndex(x => x.Clave).IsUnique();
        });

        b.Entity<Mda.Api.Gastos.ReglaCategoria>(e =>
        {
            e.ToTable("reglas_categoria");
            e.HasKey(x => x.Comercio);
            e.Property(x => x.Comercio).HasMaxLength(120);
            e.Property(x => x.Categoria).HasMaxLength(40);
            e.Property(x => x.Nombre).HasMaxLength(120);
        });

        b.Entity<Mda.Api.Luz.PuntoFoto>(e =>
        {
            e.ToTable("puntos_foto");
            e.Property(x => x.Nombre).HasMaxLength(60);
            e.Property(x => x.Orientacion).HasMaxLength(3);
            e.Property(x => x.Notas).HasMaxLength(500);
        });

        b.Entity<Mda.Api.Luz.SalidaFoto>(e =>
        {
            e.ToTable("salidas_foto");
            e.Property(x => x.Tipo).HasMaxLength(10);
            e.Property(x => x.Nota).HasMaxLength(500);
            e.HasIndex(x => x.Fecha);
        });

        b.Entity<Mda.Api.Pc.Escena>(e =>
        {
            e.ToTable("escenas");
            e.Property(x => x.Nombre).HasMaxLength(40);
            e.Property(x => x.Icono).HasMaxLength(16);
            e.Property(x => x.Energia).HasMaxLength(20);
        });

        b.Entity<Mda.Api.Pc.SaludBateria>(e =>
        {
            e.ToTable("salud_bateria");
            e.HasKey(x => x.Fecha);
        });

        b.Entity<Secreto>(e =>
        {
            e.ToTable("secretos");
            e.HasKey(x => new { x.Servicio, x.Nombre });
            e.Property(x => x.Servicio).HasMaxLength(20);
            e.Property(x => x.Nombre).HasMaxLength(30);
            e.Property(x => x.Valor).Cifrada("secretos");
        });

        b.Entity<TokenAtajo>(e =>
        {
            e.ToTable("tokens_atajo");
            e.Property(x => x.Nombre).HasMaxLength(40);
            e.Property(x => x.Hash).HasMaxLength(64);
            e.Property(x => x.Alcances).HasMaxLength(40);
            e.Property(x => x.UltimaIp).HasMaxLength(64);
            e.Property(x => x.Redes).HasMaxLength(400);
            e.HasIndex(x => x.Hash).IsUnique();
        });

        b.Entity<OrdenPc>(e =>
        {
            e.ToTable("ordenes_pc");
            e.Property(x => x.Tipo).HasMaxLength(20);
            e.Property(x => x.Parametro).HasMaxLength(300); // una dirección web para «abrir en el PC»
            e.Property(x => x.Estado).HasMaxLength(12);
            e.Property(x => x.Resultado).HasMaxLength(200);
            e.Property(x => x.Desde).HasMaxLength(200);
        });

        b.Entity<Senuelo>(e =>
        {
            e.ToTable("senuelos");
            e.Property(x => x.Raiz).HasMaxLength(20);
            e.Property(x => x.Ruta).HasMaxLength(500);
            e.Property(x => x.Plantilla).HasMaxLength(20);
            e.HasIndex(x => new { x.Raiz, x.Ruta }).IsUnique();
        });

        b.Entity<FotoPublica>(e =>
        {
            e.ToTable("fotos_publicas");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(16);
            e.Property(x => x.Raiz).HasMaxLength(20);
            e.Property(x => x.Ruta).HasMaxLength(500);
            e.Property(x => x.Titulo).HasMaxLength(80);
            e.Property(x => x.Descripcion).HasMaxLength(400);
            e.Property(x => x.Alt).HasMaxLength(200);
            e.Property(x => x.Categoria).HasMaxLength(30);
            e.Property(x => x.Tono).HasMaxLength(9);
            e.Property(x => x.Hash).HasMaxLength(20);
            e.HasIndex(x => x.Ruta).IsUnique();
            e.HasIndex(x => x.Orden);
        });

        b.Entity<Hucha>(e =>
        {
            e.ToTable("huchas");
            e.Property(x => x.Nombre).HasMaxLength(80);
            e.Property(x => x.Objetivo).HasPrecision(12, 2);
            e.Property(x => x.Ahorrado).HasPrecision(12, 2).IsConcurrencyToken(); // dos aportaciones a la vez no se pisan
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
            e.Property(x => x.P256dh).Cifrada("push");
            e.Property(x => x.Auth).Cifrada("push");
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
