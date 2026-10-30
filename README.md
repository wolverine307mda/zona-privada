# Zona privada · MDA

Mi app personal, alojada en el equipo de casa y accesible desde la web
(`wolverine307mda.github.io` → icono de persona → «Entrar»).

| Parte | Tecnología |
|---|---|
| Front | Angular 22 (standalone, signals, zoneless), Three.js, ECharts |
| API | ASP.NET Core 10 (C#, Minimal APIs), SignalR |
| Datos | PostgreSQL 17 + Entity Framework Core |
| Usuarios | ASP.NET Core Identity: cookie `HttpOnly` + `SameSite=Strict`, 2FA, bloqueo tras 5 fallos, sin registro público |
| Registros | Serilog + Seq |
| Despliegue | Docker Compose + Cloudflare Tunnel (+ Cloudflare Access) |

## Estructura

```
api/Mda.Api          API: Program.cs, Auth/, Sistema/ (monitor en tiempo real), Data/ (EF Core + migraciones)
api/Mda.Api.Tests    xUnit: lectores del sistema y la API completa en memoria
web/                 Angular: entrar/ (candado 3D), panel/ (estado del servidor), core/ (sesión y guards)
compose.yaml         db, api, seq, copias diarias y el túnel (perfil «tunel»)
```

## Desarrollo

Requisitos: .NET SDK 10, Node 24+, Docker Desktop.

```bash
# 1. Base de datos y Seq
cp .env.example .env        # y rellena las contraseñas
docker compose up -d db seq

# 2. Secretos de la API en desarrollo (no van al repo)
cd api
dotnet user-secrets --project Mda.Api set "ConnectionStrings:Postgres" "Host=localhost;Port=5432;Database=zonaprivada;Username=mda;Password=LA_DE_.ENV"
dotnet user-secrets --project Mda.Api set "Admin:Usuario" "tu-usuario"
dotnet user-secrets --project Mda.Api set "Admin:Email" "tu@email.com"
dotnet user-secrets --project Mda.Api set "Admin:Password" "una-contraseña-larga"
dotnet run --project Mda.Api            # http://localhost:5195

# 3. Front con recarga en caliente (pide /api y /hubs a la API por el proxy)
cd ../web && npm start                  # http://localhost:4200
```

> Para el desarrollo, la base de datos necesita el puerto 5432: añade `ports: ["127.0.0.1:5432:5432"]` al servicio `db`
> en un `compose.override.yaml` (ignorado por git si quieres).

**Sin Docker** (prueba rápida con base de datos en memoria; se borra al parar):

```bash
cd web && npx ng build && cp -r dist/web/browser ../api/Mda.Api/wwwroot && cd ..
Datos__EnMemoria=true Admin__Email=yo@prueba.local Admin__Password=una-clave-larga-1   dotnet run --project api/Mda.Api --urls http://localhost:5195   # http://localhost:5195
```

Tests: `dotnet test ZonaPrivada.slnx` (API) y `npx ng test --watch=false` en `web/`.

Nueva migración: `cd api && dotnet ef migrations add Nombre --project Mda.Api --output-dir Data/Migraciones`
(se aplican solas al arrancar).

## Producción en el equipo de casa

```bash
docker compose up -d --build                    # prueba local en http://localhost:8080
docker compose --profile tunel up -d --build    # con el túnel: sale a Internet con HTTPS
```

> En Docker Desktop (Windows) el panel muestra la máquina virtual de WSL que ejecuta los contenedores
> (su memoria y su disco), no Windows directamente. La sesión sobrevive a reiniciar la API:
> las claves de la cookie se guardan en el volumen `claves`.

### Cloudflare (una vez)

1. Dominio propio añadido a Cloudflare (p. ej. `mariodedomingo.dev`).
2. **Zero Trust → Networks → Tunnels → Create tunnel** (tipo *Cloudflared*), copia el token en `.env`
   (`CLOUDFLARE_TUNNEL_TOKEN`).
3. En el túnel, **Public hostname**: `app.mariodedomingo.dev` → servicio `http://api:8080`.
4. **Zero Trust → Access → Applications → Self-hosted** para `app.mariodedomingo.dev` con una política
   que solo permita tu email. Así nadie llega a la API sin pasar antes por Cloudflare.
5. En la web pública, el botón «Entrar» de `acceso.html` pasa a apuntar a `https://app.mariodedomingo.dev`.

### Seguridad

- Ningún puerto abierto en el router: la API solo escucha en `127.0.0.1` y el túnel sale hacia Cloudflare.
- Secretos en `.env` (local) y en *user secrets* (desarrollo); nunca en el repo.
- Cabeceras de seguridad (CSP sin scripts de terceros, sin iframes) y límite de 10 intentos de login por minuto e IP.
- Contraseña de 8+ caracteres a tu elección; la cuenta se bloquea 15 min tras 5 fallos. Con una contraseña
  corta, activa sí o sí Cloudflare Access y el 2FA antes de abrir la app a Internet.
- Activa el 2FA de tu usuario: `POST /api/auth/manage/2fa` (lo añadiré al panel).
- Copia de la base de datos cada día en `./copias` (14 últimas). Guárdalas también en otro disco.
