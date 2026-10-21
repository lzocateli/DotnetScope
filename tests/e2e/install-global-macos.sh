#!/bin/sh
# Cenário E2E do escopo global no macOS (instalador oficial .pkg), executado como root em runner efêmero.
# Uso: sudo sh install-global-macos.sh <caminho do dotnetscope>
set -eu

BIN="${1:?informe o caminho do dotnetscope}"
ROOT=/usr/local/share/dotnet
STATE=/var/db/dotnetscope
USER_NAME="${SUDO_USER:?execute com sudo}"

step() { printf '\n==> %s\n' "$*"; }
fail() { printf 'FALHA: %s\n' "$*" >&2; exit 1; }
expect_code() {
  expected=$1; shift
  set +e
  "$@" >/tmp/dotnetscope-out.log 2>&1
  code=$?
  set -e
  [ "$code" -eq "$expected" ] || { cat /tmp/dotnetscope-out.log >&2; fail "$* retornou $code, esperado $expected"; }
}

step "Sem root, o escopo global é recusado"
expect_code 7 sudo -u "$USER_NAME" "$BIN" install runtime 9.0 --scope global --yes
expect_code 2 "$BIN" install runtime 9.0 --scope global --source archive --yes

VERSION=""
for candidate in 9.0.1 9.0.2 9.0.3 9.0.4; do
  if [ ! -e "$ROOT/shared/Microsoft.NETCore.App/$candidate" ]; then VERSION=$candidate; break; fi
done
[ -n "$VERSION" ] || fail "nenhum runtime 9.0 ausente disponível para o teste"
echo "Runtime escolhido: $VERSION"

step "Instalação pelo .pkg oficial (SHA-512 e assinatura verificados)"
"$BIN" install runtime "$VERSION" --scope global --yes --verbose --output json >/tmp/dotnetscope-install.json 2>/tmp/dotnetscope-install.err \
  || { cat /tmp/dotnetscope-install.err >&2; fail "install falhou"; }
grep -q '"status": "installed"' /tmp/dotnetscope-install.json || fail "status: $(cat /tmp/dotnetscope-install.json)"
grep -q '"fileName": "dotnet-runtime-osx-.*\.pkg"' /tmp/dotnetscope-install.json || fail "artefato não é .pkg"
grep -q "\"installDir\": \"$ROOT\"" /tmp/dotnetscope-install.json || fail "raiz inesperada"
grep -q '"status": "package"' /tmp/dotnetscope-install.json || fail "ambiente deveria ser do instalador"
grep -q '"succeeded": true' /tmp/dotnetscope-install.json || fail "validação do host falhou"
grep -q 'assinatura do pacote verificados' /tmp/dotnetscope-install.err || fail "verificação de assinatura não registrada"
"$ROOT/dotnet" --list-runtimes | grep -q "^Microsoft.NETCore.App $VERSION " || fail "runtime não listado pelo host"
grep -ls "$ROOT" /etc/paths.d/* >/dev/null || fail "/etc/paths.d não aponta para $ROOT"

step "Estado global do root e staging limpo"
[ "$(stat -f '%u %Lp' "$STATE")" = "0 755" ] || fail "estado com dono/modo $(stat -f '%u %Lp' "$STATE")"
if [ -n "$(ls -A "$STATE/staging" 2>/dev/null)" ]; then fail "staging não foi removido"; fi

untrusted=""
for dir in /usr/local /usr/local/share; do
  if [ -d "$dir" ] && [ "$(stat -f '%u' "$dir")" != "0" ]; then untrusted="$untrusted $dir"; fi
done
if [ -n "$untrusted" ]; then
  step "Raiz não confiável ($untrusted): host não executado como root e .pkg executado de novo"
  grep -q 'grava nesse local mesmo assim' /tmp/dotnetscope-install.err || fail "aviso de raiz não confiável ausente"
  grep -q 'executado como root' /tmp/dotnetscope-install.json || fail "validação deveria ser somente por arquivos"
  "$BIN" install runtime "$VERSION" --scope global --yes --output json >/tmp/dotnetscope-again.json 2>/dev/null
  grep -q '"status": "installed"' /tmp/dotnetscope-again.json || fail "com raiz não confiável, a pasta existente não pode ser aceita: $(cat /tmp/dotnetscope-again.json)"
  # O runner é descartável: a raiz passa a ser do root para cobrir o ramo confiável.
  for dir in $untrusted; do chown root:wheel "$dir"; chmod go-w "$dir"; done
fi

step "Raiz confiável: idempotência validada pelo host, e inventário"
"$BIN" install runtime "$VERSION" --scope global --yes --output json >/tmp/dotnetscope-again.json
grep -q '"status": "alreadyInstalled"' /tmp/dotnetscope-again.json || fail "reinstalação não foi idempotente: $(cat /tmp/dotnetscope-again.json)"
if grep -q 'executado como root' /tmp/dotnetscope-again.json; then fail "com raiz confiável, a validação deveria executar o host"; fi
"$BIN" list --installed --output json | tr -d '\n ' | grep -q "\"component\":\"runtime\",\"version\":\"$VERSION\",\"origin\":\"dotnetscope\"" \
  || fail "list --installed sem o runtime do .pkg"

step "Remoção do .pkg não é automática (D-F7-4)"
expect_code 2 "$BIN" uninstall runtime "$VERSION" --scope global
grep -q 'dotnet-core-uninstall' /tmp/dotnetscope-out.log || fail "orientação de remoção ausente"
[ -e "$ROOT/shared/Microsoft.NETCore.App/$VERSION" ] || fail "o runtime foi removido"
expect_code 3 "$BIN" uninstall runtime 9.0.0 --scope global

printf '\nE2E macOS global aprovado.\n'
