#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ENV_FILE="$ROOT/.local/demo.env"
prepare() {
  umask 077
  mkdir -p "$ROOT/.local/certificates"
  if [[ ! -f "$ENV_FILE" ]]; then
    { printf 'SGV_SQL_PASSWORD=Q!%s\n' "$(openssl rand -hex 20)";
      printf 'SGV_JWT_KEY=%s\n' "$(openssl rand -hex 32)";
      printf 'SGV_ADMIN_PASSWORD=Q!%s\n' "$(openssl rand -hex 10)";
      printf 'DTE_SERVICE_KEY=%s\n' "$(openssl rand -hex 32)";
      printf 'DTE_CERTIFICATE_PASSWORD=%s\n' "$(openssl rand -hex 24)";
    } > "$ENV_FILE"
  fi
}
compose() { docker compose --project-directory "$ROOT" --env-file "$ENV_FILE" "$@"; }
case "${1:-up}" in
  prepare) prepare; printf 'Configuración privada preparada en .local/demo.env\n' ;;
  credentials) prepare; printf 'Usuario: admin\n'; node -e 'const fs=require("fs");const s=fs.readFileSync(process.argv[1],"utf8");console.log("Contraseña: "+s.split("\n").find(x=>x.startsWith("SGV_ADMIN_PASSWORD=")).split("=").slice(1).join("="))' "$ENV_FILE" ;;
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
      token="$(node -e 'const fs=require("fs");const rows=fs.readFileSync(process.argv[1],"utf8").split("\n");const p=rows.find(x=>x.startsWith("SGV_ADMIN_PASSWORD=")).split("=").slice(1).join("=");process.stdout.write(JSON.stringify({username:"admin",password:p}));' "$ENV_FILE" | curl -fsS http://localhost:5080/api/auth/login -H 'Content-Type: application/json' --data-binary @- | node -e 'let s="";process.stdin.on("data",x=>s+=x);process.stdin.on("end",()=>{const d=JSON.parse(s);if(!d.accessToken)process.exit(1);process.stdout.write(d.accessToken)})')"
      curl -fsS --max-time 120 http://localhost:5080/api/facturacion/preparar-demostracion -X POST -H "Authorization: Bearer $token" >/dev/null
    fi
    printf '\nSGV-Comercial disponible en http://localhost:5173\nUsuario: admin\nConsulta la contraseña con: bash scripts/demo.sh credentials\n'
    ;;
  *) printf 'Uso: bash scripts/demo.sh [up [--no-warmup]|prepare|credentials|down|reset]\n'; exit 1 ;;
esac
