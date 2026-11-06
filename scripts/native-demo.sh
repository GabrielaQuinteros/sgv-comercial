#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DOTNET_BIN="${DOTNET_BIN:-dotnet}"
if [[ "${1:-up}" == "down" ]]; then exec bash "$ROOT/scripts/start-fiscal-services.sh" stop; fi
bash "$ROOT/scripts/demo.sh" prepare
set -a
source "$ROOT/.local/demo.env"
set +a
export ConnectionStrings__Sgv="Server=localhost,14338;Database=SgvDemo;User Id=sa;Password=$SGV_SQL_PASSWORD;Encrypt=True;TrustServerCertificate=True"
export DTE_CERTIFICATE_PATH="$ROOT/.local/certificates/firma.p12"
export DTE_PUBLIC_CERTIFICATE_PATH="$ROOT/.local/certificates/public.cer"
export SGV_ADMIN_USERNAME=admin
docker compose --project-directory "$ROOT" --env-file "$ROOT/.local/demo.env" up -d sqlserver --wait
"$DOTNET_BIN" build "$ROOT/SGV-Comercial.sln" --configuration Release
"$DOTNET_BIN" "$ROOT/tools/SGV.DemoSetup/bin/Release/net8.0/SGV.DemoSetup.dll" certificates "$ROOT/.local/certificates"
"$DOTNET_BIN" "$ROOT/tools/SGV.DemoSetup/bin/Release/net8.0/SGV.DemoSetup.dll" migrate
DOTNET_BIN="$DOTNET_BIN" bash "$ROOT/scripts/start-fiscal-services.sh"
printf 'Abre el frontend con npm run dev. Credenciales: bash scripts/demo.sh credentials\n'
