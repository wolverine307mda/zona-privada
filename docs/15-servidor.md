# 15. Servidor: entrar con el iPhone, guardia, runbooks, despertar el PC y línea de tiempo

Lo que hace la zona privada cuando tú no miras, pensado para el mini PC que estará siempre encendido. Pantalla: **Servidor** (menú › Protección) y `/aprobar`.
Código: `api/Mda.Api/Servidor/`, `api/Mda.Api/Pc/Despertar.cs`; front `web/src/app/servidor/` y `web/src/app/entrar/aprobacion.ts`.

## 1. Entrar desde un ordenador nuevo aprobándolo con el iPhone

1. En la pantalla de entrar, **«Entrar aprobándolo desde el iPhone»**: el ordenador pide la entrada y enseña un **código de dos cifras**.
2. A ti te llega un aviso por Telegram (con un botón que abre `/aprobar` y otro «No he sido yo») y una notificación push.
3. En el iPhone, en `/aprobar` ves el equipo, el lugar y la IP; **tecleas el código** que enseña ese ordenador y confirmas con **Face ID**. Teclear el código impide aprobar a ciegas una petición ajena.
4. El ordenador, que pregunta cada 2 s, recoge la sesión **una sola vez**.

Salvaguardas: caduca a los 3 minutos; 3 códigos mal puestos la deniegan; como mucho 3 peticiones pendientes a la vez; el secreto con el que el ordenador recoge la sesión (192 bits) solo se guarda como huella; la entrada bloqueada por el pánico no se salta;
aprobar exige Face ID (o contraseña) justo antes, como cualquier acción delicada; queda en el registro de accesos y en la línea de tiempo.
Si no hay Telegram vinculado ni notificaciones, la petición se rechaza («no hay forma de avisarte»).

API: `POST /api/auth/aprobacion` (crear), `POST …/{id}/estado` y `…/{id}/entrar` (el ordenador, con su secreto), `GET …/pendientes`, `POST …/{id}/aprobar` y `…/{id}/denegar` (el iPhone, con sesión).

## 2. Modo guardia y alertas con botones

Con la guardia **activada** (Servidor › Guardia, o `/guardia on` en Telegram), cuando una vigilancia da la alarma —la API da 5 errores 5xx en 10 minutos, o la dirección pública deja de responder— el bot manda además un mensaje con botones:
**▶️ ejecutar un runbook de guardia** (hasta 3), **🔕 silenciar 1 h**, **👍 ignorar** y **🚨 pánico**. Apagada, las alarmas salen como siempre (solo texto). Cada alarma queda en la línea de tiempo.
«Mandarme una alarma de prueba» (Servidor › Guardia) enseña los botones sin esperar a que algo falle. Código: `GuardiaServicio` (se engancha en `AlertaErrores` y `VigilanteDireccion`).

## 3. Runbooks

Secuencias de pasos con nombre, que se lanzan desde la pantalla, desde Telegram (`/runbooks`, `/ejecuta comprobar todo`) o desde un botón de la guardia, y **se ven en vivo** (paso a paso, con su resultado).
Pasos: **comprobar** (base, agente del PC, dirección pública, vigilancias) · **avisar** · **esperar** (hasta 300 s) · **despertar** un aparato o el PC principal · **escena** · **llegué/me voy de casa** · **orden al PC** (bloquear, suspender, apagar, reiniciar, cancelar).
Cada paso puede **pedir permiso con Face ID** (el runbook se para y te avisa; 10 minutos para decidir) y/o **seguir si falla**. Suspender, apagar y reiniciar **obligan** a pedir permiso (se valida en el servidor). Un paso que falla detiene el runbook salvo que sea «seguir si falla».
Solo corre una ejecución de cada runbook a la vez. Al empezar de cero hay uno de partida, «Comprobar todo» (de guardia).
Las escenas que apagan el PC no se aplican desde un runbook (como en Atajos: solo desde la app, con Face ID).

## 4. Despertar el PC principal

Un PC apagado no puede despertarse a sí mismo; el mini PC, siempre encendido, sí. En Servidor › Guardia y PC principal eliges cuál es tu **PC principal** (un aparato de Red con MAC) y lo despiertas con un toque, con **`/enciende`** en Telegram o con el atajo de Siri **«Encender el PC» sin decir nombre**.
La propia API manda el paquete mágico (UDP a la difusión general y a la de cada red local, puertos 9 y 7); si el agente del PC está conectado, también se lo pide a él (por si la API corre donde no ve la red de casa, como en Docker). No se puede saber si el PC lo recibió: si no responde en un minuto, el mensaje recuerda activar Wake-on-LAN en la BIOS y en la tarjeta de red.

## 5. Línea de tiempo del servidor

Una sola vista, filtrable por tipo (accesos, entradas con el iPhone, guardia, alertas, runbooks, órdenes al PC, sistema), por rango (24 h, 7 y 30 días) y por texto. Junta lo que ya existía (accesos y órdenes al PC) con la tabla nueva `eventos_servidor`
(aprobaciones, guardia, alertas, runbooks, despertares), que guarda los últimos 5000. API: `GET /api/servidor/linea?desde&hasta&tipos&q&max` (el rango, hasta 120 días).
No sustituye a «la línea del día» del Inicio (`/api/inicio/linea`: gastos, fotos…), que es de tu vida y no del servidor.

## Probado y sin probar

- **Probado:** la lógica pura (paquete mágico, códigos, validación de runbooks, botones, composición de la línea) y 31 tests de la API con el flujo completo (pedir, aprobar con Face ID y código, recoger la sesión una vez, denegar, límite de pendientes, runbooks con fallos, permiso y cancelación, guardia, línea, PC principal, comandos y botones de Telegram); en el navegador, la entrada aprobada de punta a punta, las pantallas y la ejecución en vivo.
- **Sin probar:** que Telegram entregue los mensajes y los botones (no se ha mandado nada a tu bot), las notificaciones push al iPhone, Face ID de verdad, que un paquete Wake-on-LAN despierte un PC real, y las alarmas de la guardia ante un fallo real.
