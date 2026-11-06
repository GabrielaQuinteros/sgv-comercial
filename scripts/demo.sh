#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ENV_FILE="$ROOT/.local/demo.env"
prepare() {
  node "$ROOT/scripts/demo-config.mjs" prepare
}
compose() { docker compose --project-directory "$ROOT" --env-file "$ENV_FILE" "$@"; }
case "${1:-up}" in
  prepare) prepare; printf 'Configuración privada preparada en .local/demo.env\n' ;;
  credentials) prepare; node "$ROOT/scripts/demo-config.mjs" credentials ;;
  down) prepare; compose down ;;
  reset) prepare; compose down --volumes; printf 'Volumen de SgvDemo eliminado. Ejecuta up para recrear los datos.\n' ;;
  up)
    prepare
    compose --profile tools build
    compose --profile tools run --rm --no-deps setup certificates /certificates
    compose up -d sqlserver --wait
    compose --profile tools run --rm --no-deps setup migrate
    compose up -d receptor dte api frontend
    for port in 5080 5081 5082; do
      ready=0
      for attempt in {1..60}; do if curl -fsS "http://localhost:$port/health" >/dev/null; then ready=1; break; fi; sleep 2; done
      if [[ "$ready" != 1 ]]; then printf 'No inició el servicio en el puerto %s\n' "$port"; exit 1; fi
    done
    if [[ "${2:-}" != "--no-warmup" ]]; then
      token="$(node "$ROOT/scripts/demo-config.mjs" login-payload | curl -fsS http://localhost:5080/api/auth/login -H 'Content-Type: application/json' --data-binary @- | node -e 'let s="";process.stdin.on("data",x=>s+=x);process.stdin.on("end",()=>{const d=JSON.parse(s);if(!d.accessToken)process.exit(1);process.stdout.write(d.accessToken)})')"
      curl -fsS --max-time 120 http://localhost:5080/api/facturacion/preparar-demostracion -X POST -H "Authorization: Bearer $token" >/dev/null
    fi
    printf '\nSGV-Comercial disponible en http://localhost:5173\nUsuario: admin\nConsulta la contraseña con: bash scripts/demo.sh credentials\n'
    ;;
  *) printf 'Uso: bash scripts/demo.sh [up [--no-warmup]|prepare|credentials|down|reset]\n'; exit 1 ;;
esac
