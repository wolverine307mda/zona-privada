# 8. Desarrollo

## Convenciones

- **Todo en español**: código (clases, métodos, variables, CSS), comentarios, mensajes de commit y textos de la interfaz.
  Comentarios breves que explican el *porqué*, en minúscula, al estilo del resto del código.
- API: un módulo por carpeta con `MapXxx()`; validación con `Results.ValidationProblem`; lógica reutilizable en
  métodos `internal static` que devuelven `IResult` (los comparten HTTP, asistente y Telegram).
- Front: componentes standalone con signals; un servicio por módulo; sin librerías de UI; tokens CSS de `styles.css`.
- Fechas: `DateOnly` para días, `DateTimeOffset` (UTC en base) para momentos; cálculos de repetición en hora local (`Zona`).
- Commits: mensaje en español con título + viñetas de lo cambiado.

## Tests

```bash
dotnet test ZonaPrivada.slnx                              # todos (~130)
dotnet test ZonaPrivada.slnx --filter AsistenteTests      # una clase
cd web && npx ng test --watch=false
```

- `api/Mda.Api.Tests`: `WebApplicationFactory<Program>` con base en memoria, reloj falso, `AvisosDePrueba` (registra
  correos/WhatsApp/Telegram enviados), `RedDePrueba` (respuestas falsas para Telegram y Open-Meteo).
- Ficheros: `ApiTests` (login, sesiones, sistema), `GastosTests`, `CarteraTests`, `AsistenteTests` (frases → propuestas),
  `NuevosModulosTests` (notas, agenda…), `ArchivosTests`, `ClaveTests`, `DosPasosTests`, `PlantillaTests`,
  `TelegramTiempoTests`, `LectorSistemaTests`.
- **Al tocar el intérprete, añade casos a `AsistenteTests`** (es fácil romper una frase al arreglar otra).

## Migraciones (EF Core)

```bash
cd api
dotnet tool restore                      # dotnet-ef (api/dotnet-tools.json)
dotnet ef migrations add NombreEnPascal --project Mda.Api --output-dir Data/Migraciones
```

Se aplican solas al arrancar (`SemillaAdmin`). Para los tests y el modo memoria se usa `EnsureCreated`.

## Añadir un módulo nuevo (receta)

1. **API**: carpeta `api/Mda.Api/Cosa/` con el modelo, `DbSet` en `AppDbContext` (tabla en minúsculas en `OnModelCreating`),
   `CosaEndpoints.MapCosa()` con `MapGroup("/api/cosa").RequireAuthorization()`, llamada en `Program.cs`, migración, tests.
2. **Front**: carpeta `web/src/app/cosa/` con `cosa.service.ts` y `cosa.ts`, ruta lazy en `app.routes.ts`,
   entrada en `core/modulos.ts` (menú lateral; `pestana: true` para la barra del móvil), y si procede una caja en `inicio/panel.html`.
3. Si el asistente debe entenderlo: nueva clase en `Interprete.cs`, tarjeta en `shell/asistente.ts`, guardado en `Telegram/Conversacion.cs`.
4. Desplegar: `docker compose up -d --build api`.

## Probar en local con el navegador

- API en memoria en :5195 + `npm start` en :4200 (ver README). Usuario `wolve` / `clave-de-prueba`.
- Mide en tamaño móvil (375×812) además del escritorio: los diálogos son hojas y no debe haber scroll horizontal
  (`document.documentElement.scrollWidth === innerWidth`).
- iOS Safari tiene particularidades que Chrome no reproduce: zoom con campos < 16px, ancho propio de `input[type=date|time]`,
  la PWA se recarga al cambiar de app (no guardes estado importante solo en memoria).

## Trampas conocidas

- La ruta comodín del front debe ir después de los estáticos (orden del pipeline).
- `Npgsql` exige `DateTimeOffset` en UTC: lo resuelve el conversor global de `AppDbContext`.
- En CSS de componentes, Angular añade atributos a los selectores (más especificidad): una regla como `.panel > *`
  puede ganar a `.dinero`; pon los valores por defecto en la clase base.
- La fuente *Panchang* es muy ancha: para cifras que cambian (relojes, contadores) usa JetBrains Mono o `tabular-nums`.
- El service worker cachea estáticos: versiona URLs (`?v=N`) cuando cambie una imagen con el mismo nombre.
- Nunca registres URLs con tokens (Telegram): usa HttpClients sin loggers.
