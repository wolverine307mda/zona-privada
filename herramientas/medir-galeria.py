"""Mide lo que tarda la galería pública de la zona privada, con una sola conexión HTTP/2 reutilizada (como un navegador).

Uso:  python herramientas/medir-galeria.py [https://equipo-de-casa.tail77819b.ts.net]
Sin dependencias: solo la biblioteca estándar (http.client no habla HTTP/2, así que se mide por conexión persistente HTTP/1.1;
el primer salto incluye el apretón TLS, que es lo que se quiere ver).
"""
import http.client
import json
import ssl
import sys
import time
import urllib.parse

base = (sys.argv[1] if len(sys.argv) > 1 else "https://equipo-de-casa.tail77819b.ts.net").rstrip("/")
u = urllib.parse.urlparse(base)
cabeceras = {"Origin": "https://wolverine307mda.github.io", "Accept-Encoding": "gzip"}

t0 = time.perf_counter()
con = http.client.HTTPSConnection(u.netloc, context=ssl.create_default_context(), timeout=30)
con.connect()
t_con = time.perf_counter() - t0


def pedir(ruta):
    t = time.perf_counter()
    con.request("GET", ruta, headers=cabeceras)
    r = con.getresponse()
    primer_byte = time.perf_counter() - t
    cuerpo = r.read()
    return r.status, primer_byte, time.perf_counter() - t, cuerpo


estado, ttfb, total, cuerpo = pedir("/api/publico/galeria")
if estado != 200:
    sys.exit(f"La galería no responde ({estado}): ¿está activa y el equipo encendido?")
import gzip
datos = json.loads(gzip.decompress(cuerpo) if cuerpo[:2] == b"\x1f\x8b" else cuerpo)
fotos = datos["fotos"]
print(f"conexión TLS      {t_con * 1000:7.0f} ms")
print(f"lista ({len(fotos)} fotos) {ttfb * 1000:6.0f} ms hasta el primer byte, {total * 1000:.0f} ms completa, {len(cuerpo) / 1024:.1f} KB")

tiempos, bytes_ = [], 0
t_ini = time.perf_counter()
for i, f in enumerate(fotos):
    e, _, t, c = pedir(f["mini"])
    if e != 200:
        print("  fallo", e, f["mini"])
        continue
    tiempos.append(t)
    bytes_ += len(c)
    if i == 0:
        print(f"primera miniatura {t * 1000:6.0f} ms ({len(c) / 1024:.1f} KB)")
total_min = time.perf_counter() - t_ini
tiempos.sort()
if tiempos:
    print(f"{len(tiempos)} miniaturas en serie: {total_min:.1f} s, mediana {tiempos[len(tiempos) // 2] * 1000:.0f} ms, "
          f"peor {tiempos[-1] * 1000:.0f} ms, {bytes_ / 1024:.0f} KB en total")
