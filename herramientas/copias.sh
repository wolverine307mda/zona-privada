#!/bin/sh
# Contenedor «copias»: copia de seguridad diaria, copia cifrada fuera del PC y simulacro semanal de restauración.
# Mira cada 10 minutos (si el equipo estaba apagado de madrugada, lo hace al encenderse).
#
#  1. Cada día, pasadas las 3, un pg_dump en /copias (se guardan las 14 últimas).
#  2. Si hay COPIAS_AGE_PUBLICA, el dump y el volumen de claves se cifran con age y se copian a /nube
#     (la carpeta de OneDrive: se sube sola a la nube). Sin la clave privada nadie puede leerlos;
#     esa clave la guardas tú FUERA del PC. 14 diarias + 8 semanales (domingos).
#  3. Cada semana, la última copia se restaura en una base vacía y el resultado queda en /copias/.ultimo-simulacro;
#     la API lo manda a Telegram («copia verificada» o «NO se pudo restaurar»).

CLAVE="${COPIAS_AGE_PUBLICA:-}"
if [ -n "$CLAVE" ]; then
  apk add --no-cache age tar >/dev/null 2>&1 && echo "age listo: las copias se cifran y se suben a /nube" || { echo "no se pudo instalar age: no se cifrará nada"; CLAVE=""; }
else
  echo "COPIAS_AGE_PUBLICA vacía: solo copia local (sin copia cifrada fuera del PC)"
fi

subir() {  # $1 = dump; cifra y copia a /nube
  [ -n "$CLAVE" ] && [ -d /nube ] || return 0
  base="zonaprivada-$hoy"
  age -r "$CLAVE" -o "/nube/$base.dump.age" "$1" || { echo "fallo al cifrar el dump"; return 1; }
  tar -C /claves -cf - . | age -r "$CLAVE" -o "/nube/$base-claves.tar.age" || { echo "fallo al cifrar las claves"; return 1; }
  if [ "$(date +%u)" = 7 ]; then
    mkdir -p /nube/semanal && cp "/nube/$base.dump.age" "/nube/$base-claves.tar.age" /nube/semanal/
    ls -1t /nube/semanal/zonaprivada-*.dump.age 2>/dev/null | tail -n +9 | xargs -r rm -f
    ls -1t /nube/semanal/zonaprivada-*-claves.tar.age 2>/dev/null | tail -n +9 | xargs -r rm -f
  fi
  ls -1t /nube/zonaprivada-*.dump.age 2>/dev/null | tail -n +15 | xargs -r rm -f
  ls -1t /nube/zonaprivada-*-claves.tar.age 2>/dev/null | tail -n +15 | xargs -r rm -f
  echo "copia cifrada subida: $base"
}

simulacro() {
  semana=$(date +%G%V)
  [ "$(date +%H)" -ge 4 ] && [ ! -e "/copias/.simulacro-$semana" ] || return 0
  ultimo=$(ls -1t /copias/zonaprivada-*.dump 2>/dev/null | head -n 1)
  [ -n "$ultimo" ] || return 0
  nombre=$(basename "$ultimo")
  psql -h db -U mda -d postgres -qc 'DROP DATABASE IF EXISTS simulacro' -c 'CREATE DATABASE simulacro' >/dev/null 2>&1
  if pg_restore -h db -U mda -d simulacro --no-owner "$ultimo" >/tmp/restauracion.log 2>&1; then
    usuarios=$(psql -h db -U mda -d simulacro -tAc 'select count(*) from "AspNetUsers"' 2>/dev/null || echo 0)
    tablas=$(psql -h db -U mda -d simulacro -tAc "select count(*) from information_schema.tables where table_schema='public'" 2>/dev/null || echo 0)
    if [ "${usuarios:-0}" -ge 1 ] && [ "${tablas:-0}" -ge 10 ]; then estado=ok; else estado=fallo; fi
  else
    usuarios=0; tablas=0; estado=fallo
  fi
  psql -h db -U mda -d postgres -qc 'DROP DATABASE IF EXISTS simulacro' >/dev/null 2>&1
  echo "$estado|${usuarios:-0}|${tablas:-0}|$nombre|$(date -u +%Y-%m-%dT%H:%M:%SZ)" > /copias/.ultimo-simulacro
  touch "/copias/.simulacro-$semana"
  ls -1a /copias/.simulacro-* 2>/dev/null | head -n -4 | xargs -r rm -f
  echo "simulacro de restauración: $estado ($usuarios usuarios, $tablas tablas)"
}

while true; do
  hoy=$(date +%Y%m%d)
  if [ "$(date +%H)" -ge 3 ] && ! ls /copias/zonaprivada-$hoy-*.dump >/dev/null 2>&1; then
    if pg_dump -h db -U mda -d zonaprivada -Fc -f /copias/.tmp.dump; then
      destino="/copias/zonaprivada-$hoy-$(date +%H%M).dump"
      mv /copias/.tmp.dump "$destino" && echo "copia hecha: $hoy"
      ls -1t /copias/zonaprivada-*.dump | tail -n +15 | xargs -r rm -f
      subir "$destino"
    else
      echo "pg_dump falló"
    fi
  fi
  simulacro
  sleep 600
done
