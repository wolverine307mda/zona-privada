# Lo de hoy en el iPhone

Un widget propio de la pantalla de inicio no se puede hacer con una app web (iOS no lo permite), pero estas tres cosas sí, y son gratis:

## 1. La pantalla «Hoy»

Abre `/hoy` en la app (o busca «Hoy» con Ctrl K): gasto de hoy, presupuestos que aprietan, agenda, hábitos pendientes (un toque los apunta) y la mejor salida de la semana. Se actualiza sola al volver a la app. Puedes añadirla a la pantalla de inicio como acceso directo desde Safari (Compartir → Añadir a pantalla de inicio) si la quieres como segundo icono.

## 2. Un widget con Scriptable

1. Instala **Scriptable** (gratis) desde la App Store.
2. En la zona privada: **Seguridad → Atajos**, crea un token con el permiso **lectura** y cópialo (se enseña una sola vez).
3. En Scriptable, crea un script nuevo, pega el contenido de `hoy-widget.js` y ponle de nombre «Hoy».
4. Ejecútalo una vez dentro de Scriptable: te pide la **dirección** de tu zona privada y el **token** y los guarda en el llavero del iPhone (no quedan en el script). Verás el widget de prueba.
5. En la pantalla de inicio: mantén pulsado → **+** → Scriptable → elige el tamaño → «Editar widget» → *Script*: «Hoy».

El widget llama a `GET /api/atajos/hoy` con el token como `Authorization: Bearer …`. Ese token **solo sirve para leer** (no apunta gastos ni abre la app) y lo puedes revocar desde Seguridad cuando quieras.

### Widgets de la pantalla de bloqueo (iOS 16 o más)

El mismo script dibuja tres **accesorios** para la pantalla de bloqueo:

1. Bloquea el iPhone, mantén pulsada la pantalla de bloqueo → **Personalizar** → **Bloqueada**.
2. Toca el recuadro de widgets (debajo de la hora) o la línea de encima de la hora.
3. Elige **Scriptable**, y dentro: *Rectangular* (saldo y siguiente cita), *Circular* (anillo con el % del presupuesto más apretado) o *En línea* («Disponible 523 €»).
4. Toca el widget colocado → **Script**: «Hoy».

Son blanco y negro (así los dibuja iOS en el bloqueo). Si el servidor es antiguo y no manda los datos nuevos (`compacto`), enseñan el gasto de hoy en su lugar.

**Caché y sin conexión**: la respuesta se guarda 15 minutos en el iPhone y no se vuelve a pedir antes; si no hay red (o el equipo de casa está apagado) se enseña lo último con su antigüedad («hace 40 min»). iOS refresca los widgets cuando le parece (a veces tarda más de 15 minutos): es cosa suya y no se puede forzar.

**Probar sin iPhone**: `node herramientas/ios/probar-widget.js` ejecuta el script con unos Scriptable de mentira y datos falsos y comprueba cada tamaño, la caché y el modo sin conexión.

## 3. El número sobre el icono

Si tienes la app instalada en la pantalla de inicio y has permitido las notificaciones (iOS 16.4 o más), cada aviso que llega suma uno al globo del icono (cuenta los avisos que aún no has mirado) y se quita al abrir la app.

## Lo que no se puede

- Un widget nativo con datos «vivos» propio de la app: haría falta una app de iOS de verdad (cuenta de desarrollador de pago).
- Los accesos directos del manifest (mantener pulsado el icono) funcionan en Android; en iOS se ignoran sin romper nada.
