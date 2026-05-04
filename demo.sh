#!/bin/bash
# Dynamic Log Level Filtering -- Sunum Demo Scripti
# Kullanim: bash demo.sh

# set -eu  # demo script icin devre disi

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
BOLD='\033[1m'
RESET='\033[0m'

API_DIR="$(cd "$(dirname "$0")/src/API" && pwd)"
BASE_URL="http://localhost:5078"
LOG_FILE="$API_DIR/logs/app-log-$(date +%Y%m%d).txt"
API_PID=""

header() {
  echo ""
  echo -e "${BOLD}${BLUE}================================================${RESET}"
  echo -e "${BOLD}${BLUE}  $1${RESET}"
  echo -e "${BOLD}${BLUE}================================================${RESET}"
}

step()  { echo -e "\n${CYAN}>>  $1${RESET}"; }
ok()    { echo -e "${GREEN}[OK]  $1${RESET}"; }
warn_() { echo -e "${YELLOW}[!!]  $1${RESET}"; }
err_()  { echo -e "${RED}[XX]  $1${RESET}"; }

pause() {
  echo ""
  echo -e "${BOLD}${YELLOW}  -------- ENTER'a bas, devam et --------${RESET}"
  read -r _D || true
}

wait_api() {
  local retries=20
  printf "      Bekleniyor"
  while ! curl -s "$BASE_URL/health" > /dev/null 2>&1; do
    sleep 1
    retries=$((retries - 1))
    printf "."
    if [ "$retries" -le 0 ]; then
      echo ""
      err_ "API baslamadi!"
      exit 1
    fi
  done
  echo " hazir."
}

start_api() {
  local env=$1
  if [ -n "$API_PID" ]; then
    kill "$API_PID" 2>/dev/null || true
    API_PID=""
  fi
  lsof -ti :5078 2>/dev/null | xargs kill -9 2>/dev/null || true
  sleep 1

  mkdir -p "$API_DIR/logs"

  step "API baslatiliyor -- Ortam: ${BOLD}${env}${RESET}"
  ASPNETCORE_ENVIRONMENT=$env \
    dotnet run --no-build --configuration Release \
      --project "$API_DIR" --launch-profile http 2>/dev/null &
  API_PID=$!
  wait_api

  # API tamamen ayaga kalktiktan SONRA log dosyasini sifirla
  : > "$LOG_FILE"
  ok "API hazir -> $BASE_URL   (PID: $API_PID)"
}

show_log() {
  echo ""
  echo -e "${BOLD}  LOG DOSYASI -- Musteri islemleri:${RESET}"
  echo    "  ------------------------------------------------"
  if [ ! -s "$LOG_FILE" ]; then
    echo -e "  ${YELLOW}(log dosyasi henuz bos)${RESET}"
    echo    "  ------------------------------------------------"
    return
  fi
  python3 - "$LOG_FILE" <<'PYEOF'
import sys, json

log_file = sys.argv[1]

COLORS = {
    "Information": "\033[0;32m",
    "Warning":     "\033[1;33m",
    "Error":       "\033[0;31m",
    "Fatal":       "\033[0;35m",
}
RESET = "\033[0m"
BOLD  = "\033[1m"

keywords = [
    "musteri","Musteri",
    "olustu","oluştu",
    "güncelle","sil","pasif",
    "bulunam","Duplicate","Validasyon","reddedildi",
    "Ahmet","ahmet@demo","bozuk"
]

found = False
with open(log_file, encoding="utf-8") as f:
    for line in f:
        line = line.strip()
        if not line:
            continue
        try:
            obj   = json.loads(line)
            level = obj.get("@l", "Information")
            mt    = obj.get("@mt", "")
            props = {k: v for k, v in obj.items() if not k.startswith("@")}
            text  = mt
            for k, v in props.items():
                text = text.replace("{" + k + "}", str(v))
            if not any(kw.lower() in text.lower() for kw in keywords):
                continue
            color = COLORS.get(level, "")
            print("  {}{:13}{} {}".format(
                color + BOLD, "[" + level + "]", RESET, text[:95]))
            found = True
        except Exception:
            pass

if not found:
    print("  \033[1;33m[!!] Bu ortamda bu seviyede musteri logu YOK.\033[0m")
    print("  \033[0;37m     (Whitelist bu seviyeleri filtredi.)\033[0m")
PYEOF
  echo "  ------------------------------------------------"
}

make_requests() {
  curl -s -X POST "$BASE_URL/api/customer" \
    -H "Content-Type: application/json" \
    -d '{"firstName":"Ahmet","lastName":"Yilmaz","email":"ahmet@demo.com","phone":"5551234567"}' \
    > /dev/null

  curl -s -X POST "$BASE_URL/api/customer" \
    -H "Content-Type: application/json" \
    -d '{"firstName":"Ali","lastName":"Veli","email":"ahmet@demo.com","phone":"5559999999"}' \
    > /dev/null

  curl -s -X POST "$BASE_URL/api/customer" \
    -H "Content-Type: application/json" \
    -d '{"firstName":"","lastName":"","email":"bozuk","phone":""}' \
    > /dev/null

  curl -s "$BASE_URL/api/customer/00000000-0000-0000-0000-000000000000" > /dev/null

  local cid
  cid=$(curl -s "$BASE_URL/api/customer" \
    | python3 -c "import sys,json; lst=json.load(sys.stdin); print(lst[0]['id'] if lst else '')" 2>/dev/null || echo "")
  if [ -n "$cid" ]; then
    curl -s -X DELETE "$BASE_URL/api/customer/$cid" > /dev/null
  fi
}

stop_api() {
  if [ -n "$API_PID" ]; then
    kill "$API_PID" 2>/dev/null || true
    API_PID=""
  fi
  lsof -ti :5078 2>/dev/null | xargs kill -9 2>/dev/null || true
  sleep 1
}

trap stop_api EXIT

# =============================================================================
#  SUNUM AKISI
# =============================================================================

clear
echo -e "${BOLD}${BLUE}"
echo "  +--------------------------------------------------+"
echo "  |   Dynamic Log Level Filtering  --  PoC Sunumu   |"
echo "  |   .NET 9  +  Serilog  +  Clean Architecture     |"
echo "  +--------------------------------------------------+"
echo -e "${RESET}"
echo ""
echo -e "  Atilacak 5 istek (her iki senaryoda ayni):"
echo -e "    POST /customer  (basarili kayit)        -> ${GREEN}[Information]${RESET}"
echo -e "    POST /customer  (duplicate e-posta)     -> ${YELLOW}[Warning]${RESET}"
echo -e "    POST /customer  (validasyon hatasi)     -> ${RED}[Error]${RESET}"
echo -e "    GET  /customer/{olmayan-id}             -> ${YELLOW}[Warning]${RESET}"
echo -e "    DELETE /customer/{aktif-kayit}          -> ${RED}[Error]${RESET}"

pause

# -------------------------------------------------------------------
#  SENARYO 1 -- Dev ortami, sadece Information
# -------------------------------------------------------------------
header "SENARYO 1  --  Dev Ortami"
echo ""
echo -e "  ${BOLD}appsettings.Dev.json:${RESET}"
echo -e "    ${YELLOW}ActiveLevels: [ \"Information\" ]${RESET}"
echo ""
echo -e "  Beklenti: Sadece ${GREEN}[Information]${RESET} loglar dosyaya yazilmali."
echo -e "            Warning ve Error ${RED}DOSYAYA YAZILMAMALI.${RESET}"

pause

start_api "Dev"

step "5 istek atiliyor..."
make_requests
sleep 1
ok "Istekler tamamlandi."

step "Log dosyasi okunuyor..."
show_log

echo ""
warn_ "Warning + Error kodda tetiklendi FAKAT whitelist'te yok -> dosyada YOK."
ok    "Sadece [Information] satirlari log dosyasinda gorunuyor."

pause

# -------------------------------------------------------------------
#  SENARYO 2 -- Prod ortami, sadece Error + Fatal
# -------------------------------------------------------------------
header "SENARYO 2  --  Prod Ortami"
echo ""
echo -e "  ${BOLD}appsettings.Prod.json:${RESET}"
echo -e "    ${RED}ActiveLevels: [ \"Error\", \"Fatal\" ]${RESET}"
echo ""
echo -e "  Beklenti: Sadece ${RED}[Error]${RESET} loglar dosyaya yazilmali."
echo -e "            Information ve Warning ${YELLOW}DOSYAYA YAZILMAMALI.${RESET}"

pause

start_api "Prod"

step "Ayni 5 istek tekrar atiliyor..."
make_requests
sleep 1
ok "Istekler tamamlandi."

step "Log dosyasi okunuyor..."
show_log

echo ""
warn_ "Information + Warning kodda tetiklendi FAKAT whitelist'te yok -> dosyada YOK."
ok    "Sadece [Error] satirlari log dosyasinda gorunuyor."

pause

# -------------------------------------------------------------------
#  OZET
# -------------------------------------------------------------------
header "OZET  --  Whitelist Mantigi"
echo ""
echo -e "  ${BOLD}Ortam        ActiveLevels                  Dosyaya dusen${RESET}"
echo    "  ---------------------------------------------------------------"
echo -e "  Development  Verbose,Debug,Info,Warn,Error   ${GREEN}Hepsi${RESET}"
echo -e "  Dev          Information                      ${GREEN}Sadece Info${RESET}"
echo -e "  Test         Info,Warning,Error,Fatal         ${CYAN}Info+Warn+Error${RESET}"
echo -e "  PreProd      Warning,Error,Fatal              ${YELLOW}Warn+Error${RESET}"
echo -e "  Prod         Error,Fatal                      ${RED}Sadece Error${RESET}"
echo ""
echo -e "  ${BOLD}Degistirilen tek sey:${RESET}"
echo    "  appsettings.{Ortam}.json -> LogSettings.ActiveLevels"
echo -e "  ${BOLD}Sifir kod degisikligi.${RESET}"
echo ""

stop_api
ok "Demo tamamlandi. API kapatildi."
echo ""
