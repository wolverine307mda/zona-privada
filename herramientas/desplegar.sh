#!/bin/sh
# Lo mismo que desplegar.ps1 para Linux/macOS/WSL: guarda la imagen actual como «anterior», reconstruye, espera a que la API esté sana y, si no, vuelve atrás.
#   herramientas/desplegar.sh          despliega
#   herramientas/desplegar.sh volver   vuelve a la imagen «anterior»
set -u
cd "$(dirname "$0")/.." || exit 1

esperar() {
  i=0
  while [ "$i" -lt 40 ]; do
    id=$(docker compose ps -q api)
    if [ -n "$id" ]; then
      estado=$(docker inspect --format '{{.State.Health.Status}}' "$id" 2>/dev/null)
      [ "$estado" = healthy ] && return 0
      [ "$estado" = unhealthy ] && return 1
    fi
    i=$((i + 1)); sleep 3
  done
  return 1
}

atras() {
  echo "Volviendo a la versión anterior ($1:anterior)..."
  docker tag "$1:anterior" "$1:latest" && docker compose up -d --no-build api
  if esperar; then echo "Versión anterior en marcha."; else echo "La anterior tampoco arranca: mira «docker compose logs api»."; fi
}

contenedor=$(docker compose ps -q api)
imagen=""
if [ -n "$contenedor" ]; then
  imagen=$(docker inspect --format '{{.Config.Image}}' "$contenedor" | cut -d: -f1)
  [ "${1:-}" = volver ] && { atras "$imagen"; exit 0; }
  docker tag "$(docker inspect --format '{{.Image}}' "$contenedor")" "$imagen:anterior"
  echo "Guardada la versión actual como $imagen:anterior"
elif [ "${1:-}" = volver ]; then echo "No hay contenedor de la API."; exit 1; fi

docker compose up -d --build api
if ! esperar; then
  echo "La API nueva no está sana tras el despliegue."
  docker compose logs --tail 40 api
  if [ -n "$imagen" ]; then atras "$imagen"; else echo "No había versión anterior a la que volver."; fi
  exit 1
fi
echo "Desplegado y sano."
