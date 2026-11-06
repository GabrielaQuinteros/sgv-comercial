#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DOTNET_BIN="${DOTNET_BIN:-dotnet}"
OFFSET="${SGV_NATIVE_OFFSET:-0}"
PID_FILE="$ROOT/.local/fiscal-$OFFSET.pids"
mkdir -p "$ROOT/.local/logs"
if [[ "${1:-start}" == "stop" ]]; then
  if [[ -f "$PID_FILE" ]]; then
    while read -r pid; do
      cmd="$(ps -p "$pid" -o command= 2>/dev/null || true)"
      if [[ "$cmd" == *"$ROOT/"*".dll"* ]]; then kill "$pid" 2>/dev/null || true; fi
    done < "$PID_FILE"
    rm -f "$PID_FILE"
  fi
  exit 0
fi
if [[ -f "$PID_FILE" ]]; then printf 'Los servicios ya tienen un registro de procesos. Ejecuta stop antes de reiniciar.\n'; exit 1; fi
: "${ConnectionStrings__Sgv:?Configura ConnectionStrings__Sgv}"
: "${SGV_JWT_KEY:?Configura SGV_JWT_KEY}"
: "${SGV_ADMIN_PASSWORD:?Configura SGV_ADMIN_PASSWORD}"
: "${DTE_SERVICE_KEY:?Configura DTE_SERVICE_KEY}"
export Services__DteUrl="http://localhost:$((5081+OFFSET))/"
export Receiver__Url="http://localhost:$((5082+OFFSET))/api/receptor-pruebas/recepciondte"
export FaultInjection__Enabled=true
export Demo__Enabled=true
for entry in "receptor|dte-service/SGV.ReceptorPruebas/SGV.ReceptorPruebas|$((5082+OFFSET))" "dte|dte-service/SGV.Dte/SGV.Dte|$((5081+OFFSET))" "api|backend/SGV.Api/SGV.Api|$((5080+OFFSET))"; do
  IFS='|' read -r name project port <<< "$entry"
  assembly="$ROOT/${project%/*}/bin/Release/net8.0/${project##*/}.dll"
  nohup "$DOTNET_BIN" "$assembly" --urls "http://127.0.0.1:$port" > "$ROOT/.local/logs/$name-$OFFSET.log" 2>&1 < /dev/null &
  printf '%s\n' "$!" >> "$PID_FILE"
done
for port in "$((5080+OFFSET))" "$((5081+OFFSET))" "$((5082+OFFSET))"; do
  ready=0
  for attempt in {1..60}; do if curl -fsS "http://localhost:$port/health" >/dev/null 2>&1; then ready=1; break; fi; sleep 1; done
  if [[ "$ready" != 1 ]]; then printf 'No inició el servicio en %s; consulta .local/logs\n' "$port"; exit 1; fi
done
printf 'API, firma y receptor de pruebas iniciados. API: http://localhost:%s\n' "$((5080+OFFSET))"
