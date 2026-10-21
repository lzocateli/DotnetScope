#!/bin/sh
# Cenário E2E da estratégia apt do escopo global (Ubuntu), executado como root em um container limpo e descartável.
# O binário dotnetscope deve estar montado em /usr/local/bin/dotnetscope.
set -eu

step() { printf '\n==> %s\n' "$*"; }
fail() { printf 'FALHA: %s\n' "$*" >&2; exit 1; }
expect_code() {
  expected=$1; shift
  set +e
  "$@" >/tmp/out.log 2>&1
  code=$?
  set -e
  [ "$code" -eq "$expected" ] || { cat /tmp/out.log >&2; fail "$* retornou $code, esperado $expected"; }
}

export DEBIAN_FRONTEND=noninteractive
apt-get update -qq

step "Ambiente adulterado não influencia o apt executado como root"
cat >/tmp/apt-malicioso.conf <<'EOF'
DPkg::Pre-Invoke {"touch /tmp/apt-config-injetado";};
EOF
mkdir -p /tmp/bin-falso
printf '#!/bin/sh\ntouch /tmp/apt-get-falso-executado\nexit 0\n' >/tmp/bin-falso/apt-get
chmod +x /tmp/bin-falso/apt-get
export APT_CONFIG=/tmp/apt-malicioso.conf PATH="/tmp/bin-falso:$PATH"

step "--install-deps instala as dependências nativas pela tabela fixa"
dotnetscope install runtime 10.0 --scope global --source apt --install-deps --yes --verbose --output json >/tmp/deps.json 2>/tmp/deps.err
grep -q 'dependências instaladas com apt' /tmp/deps.err || fail "dependências não instaladas: $(cat /tmp/deps.err)"
dpkg -s ca-certificates >/dev/null 2>&1 || fail "ca-certificates não instalado"
grep -q '"status": "installed"' /tmp/deps.json || fail "runtime via apt: $(cat /tmp/deps.json)"
grep -q '"installDir": "/usr/lib/dotnet"' /tmp/deps.json || fail "raiz do pacote inesperada"
grep -q '"url": "apt:dotnet-runtime-10.0"' /tmp/deps.json || fail "estratégia apt não registrada"
RUNTIME_VERSION=$(sed -n 's/^  "version": "\(.*\)",$/\1/p' /tmp/deps.json)

step "SDK do canal pelo apt explícito"
dotnetscope install sdk 10.0 --scope global --source apt --yes --output json >/tmp/sdk.json
grep -q '"status": "installed"' /tmp/sdk.json || fail "SDK via apt: $(cat /tmp/sdk.json)"
grep -q '"succeeded": true' /tmp/sdk.json || fail "validação do host do pacote falhou"
grep -q '"status": "package"' /tmp/sdk.json || fail "ambiente deveria ser do pacote"
SDK_VERSION=$(sed -n 's/^  "version": "\(.*\)",$/\1/p' /tmp/sdk.json)
dpkg -s dotnet-sdk-10.0 >/dev/null 2>&1 || fail "pacote dotnet-sdk-10.0 não instalado"
[ ! -e /etc/profile.d/dotnetscope.sh ] || fail "o dotnetscope gravou ambiente para pacote"
/usr/bin/dotnet --list-sdks | grep -q "^$SDK_VERSION " || fail "SDK $SDK_VERSION não listado pelo host do pacote"
[ ! -e /tmp/apt-config-injetado ] || fail "APT_CONFIG do ambiente foi usado como root"
[ ! -e /tmp/apt-get-falso-executado ] || fail "apt-get do PATH do usuário foi executado"

step "Instalação por arquivo concorrendo com o pacote é recusada"
expect_code 6 dotnetscope install sdk lts --scope global --source archive --yes
[ ! -e /usr/local/share/dotnet/sdk ] || fail "archive instalado junto do pacote"

step "Inventário e doctor sem tocar a raiz do pacote"
compact=$(dotnetscope list --installed --output json | tr -d '\n ')
printf '%s' "$compact" | grep -q "\"component\":\"sdk\",\"version\":\"$SDK_VERSION\",\"origin\":\"dotnetscope\"" \
  || fail "list --installed sem o SDK do apt registrado"
dotnetscope doctor --scope global --repair >/dev/null || fail "doctor encontrou erros"
[ ! -e /usr/lib/dotnet/.dotnetscope.lock ] || fail "doctor criou lock na raiz do pacote"

step "Remoção que levaria dependentes junto é recusada"
expect_code 6 dotnetscope uninstall runtime "$RUNTIME_VERSION" --scope global
grep -q 'dotnet-sdk-10.0' /tmp/out.log || fail "recusa sem citar o SDK dependente: $(cat /tmp/out.log)"
dpkg -s dotnet-sdk-10.0 >/dev/null 2>&1 || fail "o SDK foi removido junto"

step "Desinstalação pelo apt"
dotnetscope uninstall sdk "$SDK_VERSION" --scope global --output json >/tmp/remove.json
grep -q '"origin": "apt"' /tmp/remove.json || fail "remoção não usou o apt: $(cat /tmp/remove.json)"
if dpkg -s dotnet-sdk-10.0 2>/dev/null | grep -q '^Status: install ok installed'; then fail "pacote ainda instalado"; fi
# O aspnetcore-runtime trazido pelo apt como dependência do SDK ainda depende do runtime: remoção recusada.
expect_code 6 dotnetscope uninstall runtime "$RUNTIME_VERSION" --scope global
# Ação do administrador, fora do ambiente adulterado deste teste.
env -u APT_CONFIG /usr/bin/apt-get remove -y -qq aspnetcore-runtime-10.0 >/dev/null
dotnetscope uninstall runtime "$RUNTIME_VERSION" --scope global >/dev/null
if dotnetscope list --installed --output json | tr -d '\n ' | grep -q '"origin":"dotnetscope"'; then
  fail "registros do dotnetscope permaneceram"
fi
if [ -e /tmp/apt-config-injetado ] || [ -e /tmp/apt-get-falso-executado ]; then fail "ambiente adulterado foi usado"; fi

printf '\nE2E apt aprovado.\n'
