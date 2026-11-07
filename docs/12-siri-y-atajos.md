# 12. Siri y Atajos del iPhone (recetas)

Todo lo que el iPhone puede hacer con la zona privada, sin app nativa y sin pagar nada: la app **Atajos** de iOS llama a la API con un token. Cada token lleva solo los permisos que le des
(Seguridad › Claves y atajos), no abre la app y se revoca con un toque. La dirección es la de tu Funnel (`URL_PUBLICA`), en los ejemplos `https://equipo-de-casa.tail77819b.ts.net`.

**Receta base de cualquier atajo**: acción **Obtener contenido de URL** → dirección → método → cabecera `Authorization` = `Bearer mda_…` → (si es POST) cuerpo **JSON**.
Para que Siri lo diga, añade **Mostrar resultado** o **Pronunciar texto** con el campo `mensaje` de la respuesta (`Obtener valor del diccionario › mensaje`).
Para ejecutarlo con la voz: ponle un nombre corto al atajo («Oye Siri, enciende la tele»).

| Quiero… | Método y ruta | Permiso | Cuerpo JSON | Respuesta útil |
|---|---|---|---|---|
| apuntar un gasto con la voz | `POST /api/atajos/frase` | gasto | `{"texto":"12 euros en el Mercadona"}` | `mensaje` |
| apuntar un gasto con importe y concepto | `POST /api/atajos/movimiento` | gasto | `{"importe":12.5,"concepto":"Café"}` | — |
| saber cuánto me queda y qué tengo hoy | `GET /api/atajos/resumen` | lectura | — | `mensaje` |
| widget de «hoy» (Scriptable) | `GET /api/atajos/hoy` | lectura | — | `lineas` (ver `herramientas/ios/LEEME.md`) |
| saber si todo está bien | `GET /api/atajos/estado` | lectura | — | `mensaje` |
| guardar una nota dictada | `POST /api/atajos/nota` | nota | `{"texto":"llamar al fontanero"}` | — |
| avisar de un pago de Apple Pay | `POST /api/atajos/pago` | pago | `{"comercio":"Mercadona","importe":12.5}` | — |
| «llegué a casa» / «me voy» | `POST /api/atajos/casa` | casa | `{"estado":"llegada"}` o `{"estado":"salida"}` | `mensaje` (al salir, con la comprobación: puertas abiertas y aparatos encendidos) |
| «me voy a dormir» / «despierto» | `POST /api/atajos/dormir` | casa | `{"estado":"duermo"}` o `{"estado":"despierto"}` | `mensaje` (agenda, tiempo, hora de despertar y lo que queda abierto) |
| «ropa tendida» / «ropa recogida» | `POST /api/atajos/ropa` | casa | `{"estado":"tendida"}` o `{"estado":"recogida"}` | `mensaje` |
| añadir un contacto desde su ficha (hoja de compartir) | `POST /api/atajos/persona` | personas | `{"nombre":"María","dia":3,"mes":3,"telefono":"600…"}` | `mensaje` (si ya estaba, lo dice y no lo repite) |
| poner un momento musical | `POST /api/atajos/momento` | casa | `{"nombre":"Dormir"}` | `mensaje` |
| poner una escena del PC | `POST /api/atajos/escena` | casa | `{"nombre":"Cine"}` | `mensaje` |
| despertar un aparato de la red (tele, NAS, otro PC) | `POST /api/atajos/despertar` | pc | `{"nombre":"NAS"}` | `mensaje` |
| el pánico | `POST /api/atajos/panico` | panico | — | `mensaje` |

## Automatizaciones (sin tocar nada)

En Atajos › **Automatización › +** (personal), con «Ejecutar inmediatamente» (así no pregunta):

- **Llegar** a tu casa → `/api/atajos/casa` con `{"estado":"llegada"}`. Además de poner el PC a punto, **apunta que estás en casa y dispara tus escenas por contexto** (pantalla «Escenas solas»).
- **Salir** de casa → lo mismo con `{"estado":"salida"}`.
- **Foco «Dormir»** (al activarse) → `/api/atajos/dormir` con `{"estado":"duermo"}`; al desactivarse, `{"estado":"despierto"}`. Aplica tus reglas de «Escenas solas» con evento «Cuando me vaya a dormir» / «Cuando me despierte» (escena de casa, escena del PC —las que apagan el PC nunca solas— y aviso) y contesta con lo de mañana: **Pronunciar texto** con `mensaje` lo lee en voz alta. En Telegram: `/buenasnoches` y `/buenosdias`.
- **Ropa tendida**: un atajo con `{"estado":"tendida"}` (y otro con `"recogida"`). Mientras esté tendida te aviso ~30 min antes de que llueva. En Telegram: `/ropa` y `/ropa recogida`.
- **Transacción** (Apple Pay) → `/api/atajos/pago` (el gasto entra «por confirmar»).
- **Hora del día** → lo que quieras (p. ej. `/api/atajos/resumen` y **Pronunciar texto** cada mañana).
- **Foco** (Trabajo, Descanso…) → `/api/atajos/escena` con el nombre de una escena del PC.
- **Bluetooth** (el coche) → una nota o un aviso (`/api/atajos/nota`) para registrar el trayecto.

## «Enciende el PC»: lo que se puede y lo que no

La zona privada **corre en el propio PC de casa**: si el PC está apagado o suspendido, la API tampoco está y ningún atajo puede despertarlo (no hay a quién llamar). Lo que sí funciona:

- Despertar **otros** aparatos de la red con `POST /api/atajos/despertar` (Wake-on-LAN desde el PC que sí está encendido).
- Para despertar **el propio PC** desde fuera hace falta algo que siga encendido: un Raspberry Pi o el router con Wake-on-LAN, o la opción de «activar al recibir un paquete» desde Tailscale en otro equipo.
  Mientras tanto, «Mantener despierto» (Mi PC) evita que se suspenda.

## Para llevar al iPhone

- Un token **por atajo** y con el mínimo permiso (así revocar uno no rompe los demás). Aparecen en Seguridad con su último uso y su IP; si usas uno desde una red nueva, te avisa por Telegram.
- Las respuestas siempre llevan `mensaje` en español para que Siri lo lea tal cual.
