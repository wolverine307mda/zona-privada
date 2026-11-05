# Nuevas funciones (Plan 6)

Lo que se añadió en el [plan 6](checklist-plan6.md), con sus rutas y dónde está cada cosa. Todo es gratis y sin claves nuevas.

## Presupuestos: el aviso dice cuánto queda

- Ya avisaba al pasar del 80 % y del 100 % al **apuntar** un gasto (`Cartera.ComprobarPresupuesto`).
- Ahora el texto añade el ritmo: «Te quedan 16,00 € para 9 días: unos 1,78 € al día» (`Cartera.Ritmo`, función pura), y solo en el mes en curso.
- El `Vigilante` barre cada 15 minutos todos los presupuestos (`Cartera.ComprobarTodos`) para que también avisen los gastos que no pasan por la API (fijos que se apuntan solos, importaciones). Sigue siendo una vez por nivel y mes (`Presupuesto.AvisadoNivel`).

## Previsión de fin de mes con fijos y nóminas

- `Gastos/Prevision.cs`: saldo de hoy − lo variable que sueles gastar (media de 60 días) × días que quedan − **fijos sin apuntar** + **nóminas sin apuntar** (`Calculos.PendientesDe`, los mismos que enseña «Fijos pendientes»; los cubiertos, aunque estén fechados en otro mes, no cuentan dos veces).
- `GET /api/gastos/cartera/prevision` ahora devuelve también `fijosPendientes`, `ingresosPendientes` y `serie` (saldo previsto día a día con peor y mejor caso).
- Un fijo cuyo día ya pasó y sigue sin apunte se cuenta **hoy**.
- Front: `gastos/grafica-prevision.ts` (curva con ECharts) y el desglose bajo «Cerrarás el mes con».

## Accesos nuevos

- `Seguridad/Equipos.cs`: tabla `equipos_conocidos` (huella = sistema + navegador + país, sin versiones). Al entrar, si el equipo no estaba, se avisa («equipo nuevo»), y si además el país es nuevo, el asunto lo dice («país nuevo»).
- La **primera vez** que se usa (tabla vacía) aprende en silencio: tus equipos de siempre no avisan.
- Ajuste «Solo si es un equipo o un país nuevo» (`avisar-solo-nuevos`, activo por defecto). Con él apagado, avisa de todos los inicios como antes. Solo avisa si «Avisar cuando alguien entre» está encendido.
- Rutas: `GET /api/seguridad/equipos`, `PUT /api/seguridad/equipos/{id}/confianza`, `DELETE /api/seguridad/equipos/{id}` (olvidarlo: la próxima entrada vuelve a avisar). `GET /api/seguridad/accesos` suma `lugar` («Madrid, ES») con `GeoIp`. `PUT /api/seguridad/ajustes` acepta `soloNuevos`.
- Front: `seguridad/equipos.ts` («Equipos conocidos», en Vigilancia) y el interruptor en Cuenta.
- Límite: la geolocalización es la de DB-IP Lite (aproximada); con VPN el país puede ser otro, por eso la huella mezcla equipo y país.

## Salud del PC: «se llena en N días»

- Ya existía el histórico diario (`SaludDia`, 90 días), las reglas y los avisos. `Pc/TendenciaDisco.cs` añade una recta de mínimos cuadrados sobre el espacio libre de los últimos 30 días (7 muestras mínimo y 7 días de recorrido; menos de 1 MB/día es ruido; más de 10 años se ignora).
- Regla nueva `disco-llenando:{unidad}` (🟡) cuando quedan 30 días o menos y el disco aún no está casi lleno (si lo está, manda la regla de siempre). Aviso de «resuelto» incluido.
- `GET /api/pc/llenado` → `[{ nombre, dias }]` de los discos que se están llenando. Front: nota «Se llenará en ~N días» en la tendencia de cada disco (Mi PC).

## Buscador (Ctrl K) que actúa

- **Vista previa**: una frase que parece un apunte («12 café») se interpreta con `POST /api/asistente/entender` (la misma que el asistente) y la primera opción dice lo entendido. **Intro** la guarda (gasto o evento) con «Deshacer»; **Mayús+Intro** abre el asistente para retocarla. Con dudas o preguntas se comporta como antes.
- **Acciones** con `>` (o por su nombre al buscar): color, fondo, modo día/noche/sistema, bloquear/suspender el PC, mantener despierto y escenas del PC. Las órdenes al PC piden «confirmar que eres tú» (403 `reautenticar`) como en Mi PC. Registro en `shell/acciones.ts` (con tests).
- **Contenido**: `GET /api/buscar?q=` busca en eventos de la agenda (título y lugar) y movimientos (concepto): cinco de cada, los más recientes primero, mínimo 2 letras. Las notas siguen en `/api/notas?q=` (van cifradas).
