#!/usr/bin/env bash
#
# Dynamic Log Level Filtering PoC — loglama sunumu (güncel akış)
# Serilog MinimumLevel + LogContext enrich + müşteri senaryosu
#
# Kullanım: bash log-sunumu.sh
# Önkoşul: src/API altında dotnet build (Release önerilir)

set -o pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
API_DIR="$ROOT/src/API"
BASE_URL="${BASE_URL:-http://localhost:5078}"
LOG_FILE="$API_DIR/logs/app-log-$(date +%Y%m%d).txt"
API_PID=""

# Tek tırnakta \033 yazmak ESC üretmez; $'...' gerekir. Pipe/redirect ise renk yok.
if [[ -t 1 ]]; then
  readonly C_R=$'\033[0;31m' C_G=$'\033[0;32m' C_Y=$'\033[1;33m'
  readonly C_B=$'\033[0;34m' C_C=$'\033[0;36m' C_M=$'\033[0;35m'
  readonly B=$'\033[1m' Z=$'\033[0m'
else
  readonly C_R='' C_G='' C_Y='' C_B='' C_C='' C_M='' B='' Z=''
fi

title() {
  printf '\n%s┌────────────────────────────────────────────────────────────┐%s\n' "$C_B" "$Z"
  printf '%s│  %-58s │%s\n' "$C_B" "$1" "$Z"
  printf '%s└────────────────────────────────────────────────────────────┘%s\n' "$C_B" "$Z"
}

say()   { printf '\n%s▶ %s%s\n' "$C_C" "$1" "$Z"; }
ok()    { printf '%s[OK]%s %s\n' "$C_G" "$Z" "$1"; }
warn()  { printf '%s[!!]%s %s\n' "$C_Y" "$Z" "$1"; }
die()   { printf '%s[HATA]%s %s\n' "$C_R" "$Z" "$1" >&2; exit 1; }

pause() {
  printf '\n%s── Devam için ENTER ──%s\n' "$B$C_Y" "$Z" >&2
  read -r _ </dev/tty 2>/dev/null || read -r _
}

wait_for_health() {
  local n=30
  printf '%sAPI bekleniyor%s' "$C_M" "$Z"
  while (( n > 0 )); do
    if curl -fsS "$BASE_URL/health" >/dev/null 2>&1; then
      printf ' %sok%s\n' "$C_G" "$Z"
      return 0
    fi
    sleep 1
    printf .
    (( n-- )) || true
  done
  printf '\n'
  die "Health yanıt vermedi: $BASE_URL/health"
}

shutdown_api() {
  if [[ -n "${API_PID:-}" ]] && kill -0 "$API_PID" 2>/dev/null; then
    kill "$API_PID" 2>/dev/null || true
  fi
  API_PID=""
  if command -v lsof >/dev/null 2>&1; then
    lsof -ti :5078 2>/dev/null | xargs kill -9 2>/dev/null || true
  fi
  sleep 1
}

trap shutdown_api EXIT INT TERM

boot_api() {
  local env_name="$1"
  shutdown_api
  mkdir -p "$API_DIR/logs"
  rm -f "$LOG_FILE"

  say "API başlatılıyor — ASPNETCORE_ENVIRONMENT=$env_name"
  (
    cd "$API_DIR" || exit 1
    ASPNETCORE_ENVIRONMENT="$env_name" \
      dotnet run --no-build --configuration Release --launch-profile http
  ) >/dev/null 2>&1 &
  API_PID=$!
  wait_for_health
  ok "Hazır: $BASE_URL (PID $API_PID) — günlük: $LOG_FILE (API başlamadan önce temizlendi)"
}

fetch_env_json() {
  curl -sS "$BASE_URL/api/Environment" || die "Environment endpoint okunamadı"
}

# Müşteri akışı: Information / Warning / Error üreten istekler (CustomerController + CustomerService)
run_customer_scenario() {
  local email="sunum-$(date +%s)@demo.local"
  local hdr_demo='X-Correlation-ID: sunum-korelasyon-1'

  say "1) POST müşteri (başarılı) — beklenen log: Information"
  code=$(curl -sS -o /dev/null -w '%{http_code}' -X POST "$BASE_URL/api/Customer" \
    -H "Content-Type: application/json" -H "$hdr_demo" \
    -d "{\"firstName\":\"Ayşe\",\"lastName\":\"Demir\",\"email\":\"$email\",\"phone\":\"5550101\"}")
  [[ "$code" == "201" ]] || warn "Beklenen HTTP 201, gelen: $code"

  say "2) POST aynı e-posta (duplicate) — beklenen log: Warning"
  code=$(curl -sS -o /dev/null -w '%{http_code}' -X POST "$BASE_URL/api/Customer" \
    -H "Content-Type: application/json" \
    -d "{\"firstName\":\"Ali\",\"lastName\":\"Veli\",\"email\":\"$email\",\"phone\":\"5550202\"}")
  [[ "$code" == "409" ]] || warn "Beklenen HTTP 409, gelen: $code"

  say "3) POST geçersiz gövde — beklenen log: Error"
  code=$(curl -sS -o /dev/null -w '%{http_code}' -X POST "$BASE_URL/api/Customer" \
    -H "Content-Type: application/json" \
    -d '{"firstName":"","lastName":"","email":"bozuk","phone":""}')
  [[ "$code" == "400" ]] || warn "Beklenen HTTP 400, gelen: $code"

  say "4) GET olmayan müşteri — beklenen log: Warning"
  code=$(curl -sS -o /dev/null -w '%{http_code}' \
    "$BASE_URL/api/Customer/00000000-0000-0000-0000-000000000000")
  [[ "$code" == "404" ]] || warn "Beklenen HTTP 404, gelen: $code"

  local cid
  cid=$(curl -sS "$BASE_URL/api/Customer" \
    | python3 -c "import sys,json; d=json.load(sys.stdin); print(d[0]['id'] if d else '')" 2>/dev/null) || true

  if [[ -z "$cid" ]]; then
    warn "Liste boş; aktif silme adımı atlandı"
    return 0
  fi

  say "5) DELETE aktif müşteri (iş kuralı) — beklenen log: Error"
  code=$(curl -sS -o /dev/null -w '%{http_code}' -X DELETE "$BASE_URL/api/Customer/$cid")
  [[ "$code" == "422" ]] || warn "Beklenen HTTP 422, gelen: $code"
}

# Compact JSON log: seviye özeti + son kayıtlar (keyword whitelist yok)
digest_log_file() {
  local path="$1"
  printf '\n%s── Log özeti: %s%s\n' "$B" "$path" "$Z"
  if [[ ! -s "$path" ]]; then
    warn "Dosya boş veya yok."
    return 0
  fi
  python3 - "$path" <<'PY'
import json, sys
from collections import Counter

path = sys.argv[1]
levels = Counter()
rows = []
with open(path, encoding="utf-8", errors="replace") as f:
    for line in f:
        line = line.strip()
        if not line:
            continue
        try:
            o = json.loads(line)
        except json.JSONDecodeError:
            continue
        # Compact formatter bazen varsayılan (Information) için @l yazmaz
        lvl = o.get("@l", "Information")
        levels[lvl] += 1
        mt = o.get("@mt", "")
        props = {k: v for k, v in o.items() if not str(k).startswith("@")}
        text = mt
        for k in sorted(props.keys()):
            text = text.replace("{" + k + "}", str(props[k]))
        cid = o.get("CorrelationId", "")
        rows.append((lvl, cid, text[:140]))

print("  Seviye sayıları:", dict(levels))
print("  Son 18 kayıt (seviye | CorrelationId | mesaj):")
for lvl, cid, text in rows[-18:]:
    print(f"  [{lvl:11}] {cid[:12]:12} {text}")
PY
}

clear
title "Dynamic Log PoC — Serilog ortam seviyesi + bağlamsal log"
printf '\n%sBu script şunu anlatır:%s\n' "$B" "$Z"
echo "  • appsettings.{Ortam}.json içindeki Serilog:MinimumLevel:Default eşiği"
echo "  • SerilogEnrichmentMiddleware → CorrelationId, MobileId, RequestPath…"
echo "  • RequestLoggingMiddleware → istek özeti (Information)"
echo "  • CustomerService → Information / Warning / Error iş logları"
printf '\n%sİstek sırası (her senaryoda aynı):%s\n' "$B" "$Z"
echo "  1 POST başarılı   2 POST duplicate   3 POST validasyon hatası"
echo "  4 GET yok         5 DELETE aktif (iş kuralı reddi)"
pause

# --- Senaryo A: Dev (Debug) — iş loglarının çoğu dosyaya düşer ---
title "Senaryo A — Dev (MinimumLevel = Debug)"
echo "appsettings.Dev.json → Default: Debug (Information, Warning, Error hepsi geçer)."
pause

boot_api "Dev"
say "Canlı yapılandırma: GET /api/Environment"
fetch_env_json | python3 -m json.tool || true
run_customer_scenario
sleep 2
digest_log_file "$LOG_FILE"
echo ""
ok "Dev ortamında dosyada Information + Warning + Error satırları görmeniz beklenir."
pause

# --- Senaryo B: Production (Error) — yalnızca Error ve üstü ---
title "Senaryo B — Prod (MinimumLevel = Error)"
echo "appsettings.Prod.json → Default: Error (Information ve Warning elenir)."
pause

boot_api "Prod"
say "Canlı yapılandırma: GET /api/Environment"
fetch_env_json | python3 -m json.tool || true
run_customer_scenario
sleep 2
digest_log_file "$LOG_FILE"
echo ""
warn "Prod’da Information ve Warning (ör. başarılı POST, duplicate, 404) dosyada olmamalı."
ok "Yalnızca Error düzeyindeki iş kuralları / validasyon satırları kalmalı."
pause

title "Özet"
echo "  Ortam        appsettings              MinimumLevel:Default (özet)"
echo "  ------------ ------------------------ ---------------------------"
echo "  Dev          Dev.json                 Debug   → Info+Warn+Error"
echo "  Test         Test.json                Information"
echo "  PreProd      PreProd.json             Warning"
echo "  Prod         Prod.json                Error"
echo ""
echo "  Opsiyonel: Swagger  →  $BASE_URL/swagger"
echo "  Log API (in-memory): →  $BASE_URL/api/Logging/info|warn|error"
echo ""

shutdown_api
ok "Sunum scripti bitti. API kapatıldı."
