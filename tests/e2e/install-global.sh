#!/bin/sh
# Cenário E2E do escopo global no Linux, executado como root dentro de um container limpo e descartável.
# O binário dotnetscope deve estar montado em /usr/local/bin/dotnetscope. Variáveis:
#   E2E_SDK      especificação do SDK (padrão: lts)
#   E2E_RUNTIME  especificação do runtime instalado lado a lado (padrão: 9.0)
set -eu

SDK_SPEC="${E2E_SDK:-lts}"
RUNTIME_SPEC="${E2E_RUNTIME:-9.0}"
ROOT=/usr/local/share/dotnet
case "$(uname -m)" in
  x86_64) ARCH=x64 ;;
  aarch64) ARCH=arm64 ;;
  *) printf 'FALHA: arquitetura não suportada\n' >&2; exit 1 ;;
esac
LOCATION="/etc/dotnet/install_location_$ARCH"

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
etc_snapshot() {
  for f in /etc/environment /etc/profile /etc/profile.d/* /etc/dotnet/*; do
    [ -f "$f" ] && sha256sum "$f"
  done | sort
}

step "Dependências nativas do .NET"
if command -v apt-get >/dev/null 2>&1; then
  export DEBIAN_FRONTEND=noninteractive
  apt-get update -qq
  icu=$(apt-cache search --names-only '^libicu[0-9]+$' | awk '{print $1}' | sort -V | tail -1)
  ssl=$(apt-cache search --names-only '^libssl3(t64)?$' | awk '{print $1}' | head -1)
  apt-get install -y -qq --no-install-recommends ca-certificates libstdc++6 libgcc-s1 "$icu" "$ssl" >/dev/null
elif command -v dnf >/dev/null 2>&1; then
  dnf install -y -q ca-certificates libicu libstdc++ libgcc openssl-libs util-linux shadow-utils >/dev/null
else
  fail "gerenciador de pacotes não suportado"
fi
useradd -m -s /bin/bash e2e
etc_snapshot >/tmp/etc.before

step "Sem root, o escopo global é recusado sem efeitos"
expect_code 7 runuser -u e2e -- dotnetscope install sdk "$SDK_SPEC" --scope global --yes
grep -q sudo /tmp/out.log || fail "mensagem sem orientação de sudo"
[ ! -e "$ROOT" ] || fail "raiz criada sem root"

step "Raiz gravável por outros ou de outro usuário é recusada"
mkdir -p /opt/inseguro && chmod 0777 /opt/inseguro
expect_code 7 dotnetscope install sdk "$SDK_SPEC" --scope global --install-dir /opt/inseguro/dotnet --yes
mkdir -p /opt/alheio && chown e2e /opt/alheio
expect_code 7 dotnetscope install sdk "$SDK_SPEC" --scope global --install-dir /opt/alheio/dotnet --yes
expect_code 6 dotnetscope install sdk "$SDK_SPEC" --scope global --install-dir /usr/lib/dotnet --yes
if [ -e /opt/inseguro/dotnet ] || [ -e /opt/alheio/dotnet ]; then fail "raiz insegura foi criada"; fi
rm -rf /opt/inseguro /opt/alheio

step "install_location de terceiro é conflito antes do download"
mkdir -p /etc/dotnet
printf '/opt/terceiro\n' >"$LOCATION"
expect_code 6 dotnetscope install sdk "$SDK_SPEC" --scope global --yes
[ ! -e "$ROOT/sdk" ] || fail "conflito instalou o SDK"
rm -f "$LOCATION"; rmdir /etc/dotnet

step "Instalação global do SDK $SDK_SPEC (CA do ambiente ignorada)"
# Com SSL_CERT_FILE/DIR inexistentes, o TLS falharia se fossem usados: o escopo global vale só a CA do sistema.
SSL_CERT_FILE=/nonexistent/ca.pem SSL_CERT_DIR=/nonexistent \
  dotnetscope install sdk "$SDK_SPEC" --scope global --yes --output json >/tmp/install.json 2>/tmp/install.err
grep -q 'SSL_CERT_FILE é ignorada' /tmp/install.err || fail "aviso de SSL_CERT_FILE ausente: $(cat /tmp/install.err)"
grep -q '"status": "installed"' /tmp/install.json || fail "status: $(cat /tmp/install.json)"
grep -q '"scope": "global"' /tmp/install.json || fail "escopo não registrado"
grep -q '"succeeded": true' /tmp/install.json || fail "validação falhou"
SDK_VERSION=$(sed -n 's/^  "version": "\(.*\)",$/\1/p' /tmp/install.json)
[ -n "$SDK_VERSION" ] || fail "versão ausente no JSON"
[ "$(stat -c '%u %a' "$ROOT")" = "0 755" ] || fail "raiz com dono/modo $(stat -c '%u %a' "$ROOT")"
[ "$(stat -c '%u %a' /etc/profile.d/dotnetscope.sh)" = "0 644" ] || fail "profile.d com dono/modo inesperado"
[ "$(cat "$LOCATION")" = "$ROOT" ] || fail "install_location: $(cat "$LOCATION")"

step "Shell de login de um usuário comum enxerga o SDK global"
# shellcheck disable=SC2016 # expandido pelo shell de login do usuário, não por este script.
runuser -l e2e -c 'command -v dotnet; printf "%s\n" "$DOTNET_ROOT"; dotnet --list-sdks' >/tmp/login.log
[ "$(sed -n 1p /tmp/login.log)" = "$ROOT/dotnet" ] || fail "dotnet resolve para $(sed -n 1p /tmp/login.log)"
[ "$(sed -n 2p /tmp/login.log)" = "$ROOT" ] || fail "DOTNET_ROOT: $(sed -n 2p /tmp/login.log)"
grep -q "^$SDK_VERSION \[$ROOT/sdk\]" /tmp/login.log || fail "SDK não listado: $(cat /tmp/login.log)"

step "Idempotência, runtime $RUNTIME_SPEC, inventário e doctor"
dotnetscope install sdk "$SDK_VERSION" --scope global --yes --output json >/tmp/again.json
grep -q '"status": "alreadyInstalled"' /tmp/again.json || fail "reinstalação: $(cat /tmp/again.json)"
grep -q '"status": "unchanged"' /tmp/again.json || fail "ambiente alterado na reinstalação"
dotnetscope install runtime "$RUNTIME_SPEC" --scope global --yes --output json >/tmp/runtime.json
RUNTIME_VERSION=$(sed -n 's/^  "version": "\(.*\)",$/\1/p' /tmp/runtime.json)
dotnetscope list --installed --output json >/tmp/list.json
compact=$(tr -d '\n ' </tmp/list.json)
for pair in "sdk:$SDK_VERSION" "runtime:$RUNTIME_VERSION"; do
  printf '%s' "$compact" | grep -q "\"component\":\"${pair%%:*}\",\"version\":\"${pair#*:}\",\"origin\":\"dotnetscope\"" \
    || fail "list --installed sem ${pair%%:*} ${pair#*:} com origem dotnetscope"
done
runuser -u e2e -- dotnetscope list --installed >/dev/null || fail "list --installed falhou para usuário comum"
dotnetscope doctor --scope global >/dev/null || fail "doctor --scope global encontrou erros"

step "env remove restaura /etc; env apply reaplica"
dotnetscope env remove --scope global >/dev/null
etc_snapshot >/tmp/etc.after
[ "$(sha256sum </tmp/etc.before)" = "$(sha256sum </tmp/etc.after)" ] \
  || { diff /tmp/etc.before /tmp/etc.after >&2 || true; fail "env remove não restaurou /etc"; }
# Simula sudo -E: HOME e SHELL do usuário comum, cujo perfil um processo dele poderia ter adulterado.
printf 'touch /tmp/perfil-do-usuario-executado-como-root\n' >>/home/e2e/.profile
HOME=/home/e2e SHELL=/bin/bash XDG_CONFIG_HOME=/home/e2e/.config \
  dotnetscope env apply --scope global --output json >/tmp/apply.json
grep -q '"status": "applied"' /tmp/apply.json || fail "env apply não reaplicou"
[ ! -e /tmp/perfil-do-usuario-executado-como-root ] || fail "o perfil do usuário foi executado como root"
grep -q '"matches": true' /tmp/apply.json || fail "verificação de /etc/profile.d não confirmou a raiz: $(cat /tmp/apply.json)"

step "Desinstalação completa restaura /etc e remove a raiz gerenciada"
dotnetscope uninstall runtime "$RUNTIME_VERSION" --scope global >/dev/null
dotnetscope uninstall sdk "$SDK_VERSION" --scope global --output json >/tmp/uninstall.json
grep -q '"environment": "removed"' /tmp/uninstall.json || fail "ambiente não removido: $(cat /tmp/uninstall.json)"
etc_snapshot >/tmp/etc.after
[ "$(sha256sum </tmp/etc.before)" = "$(sha256sum </tmp/etc.after)" ] \
  || { diff /tmp/etc.before /tmp/etc.after >&2 || true; fail "uninstall não restaurou /etc"; }
if [ -e "$ROOT/sdk/$SDK_VERSION" ] || [ -e "$ROOT/shared/Microsoft.NETCore.App/$RUNTIME_VERSION" ] || [ -e "$ROOT/dotnet" ]; then
  fail "componentes permaneceram em $ROOT"
fi

printf '\nE2E global aprovado.\n'
