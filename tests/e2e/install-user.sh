#!/bin/sh
# Cenário E2E da instalação no escopo user, executado como root dentro de um container limpo e descartável.
# O binário dotnetscope deve estar montado em /usr/local/bin/dotnetscope. Variáveis:
#   E2E_SDK      especificação do SDK (padrão: lts)
#   E2E_RUNTIME  especificação do runtime instalado lado a lado (padrão: 9.0)
set -eu

SDK_SPEC="${E2E_SDK:-lts}"
RUNTIME_SPEC="${E2E_RUNTIME:-9.0}"

step() { printf '\n==> %s\n' "$*"; }
fail() { printf 'FALHA: %s\n' "$*" >&2; exit 1; }

step "Dependências nativas do .NET (como root, pelo gerenciador da distribuição)"
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

step "Escopo user recusado como root"
set +e
dotnetscope install sdk "$SDK_SPEC" --yes >/tmp/root.log 2>&1
code=$?
set -e
[ "$code" -eq 7 ] || fail "esperado código 7 como root, obtido $code"
dotnetscope install sdk "$SDK_SPEC" --yes --allow-root --dry-run | grep -q '/root/.dotnet' \
  || fail "--allow-root não aceitou a raiz padrão /root/.dotnet"

useradd -m e2e
cat >/tmp/user.sh <<USER
set -eu
fail() { printf 'FALHA: %s\n' "\$*" >&2; exit 1; }
cd "\$HOME"
dotnetscope --version
dotnetscope list --available lts

printf '\n==> Instalação do SDK %s\n' "$SDK_SPEC"
dotnetscope install sdk "$SDK_SPEC" --yes --output json >install.json
grep -q '"status": "installed"' install.json || fail "status inesperado: \$(cat install.json)"
grep -q '"succeeded": true' install.json || fail "validação falhou"
sdk=\$(sed -n 's/^  "version": "\(.*\)",\$/\1/p' install.json)
[ -n "\$sdk" ] || fail "versão não encontrada na saída JSON"
test -d "\$HOME/.dotnet/sdk/\$sdk" || fail "sdk/\$sdk ausente"
test -f "\$HOME/.local/share/dotnetscope/manifest.json" || fail "manifesto ausente"
[ "\$(stat -c %a "\$HOME/.local/share/dotnetscope/manifest.json")" = 600 ] || fail "manifesto sem modo 600"
! ls -a "\$HOME/.dotnet" | grep -q dotnetscope-staging || fail "staging remanescente"

printf '\n==> SDK funcional\n'
export DOTNET_ROOT="\$HOME/.dotnet" PATH="\$HOME/.dotnet:\$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
[ "\$(dotnet --version)" = "\$sdk" ] || fail "dotnet --version diferente de \$sdk"
dotnet new console --output app --name e2e >/dev/null
cd app
dotnet run | grep -q 'Hello, World!' || fail "aplicação de console não executou"
cd "\$HOME"

printf '\n==> Reinstalação idempotente\n'
dotnetscope install sdk "\$sdk" | grep -q 'já está instalado' || fail "segunda instalação não foi idempotente"

printf '\n==> Runtime %s lado a lado\n' "$RUNTIME_SPEC"
before=\$(sha256sum "\$HOME/.dotnet/sdk/\$sdk/dotnet.dll")
dotnetscope install runtime "$RUNTIME_SPEC" --yes
dotnet --list-runtimes | grep -q '^Microsoft.NETCore.App $RUNTIME_SPEC' || fail "runtime $RUNTIME_SPEC ausente"
[ "\$(dotnet --version)" = "\$sdk" ] || fail "SDK deixou de funcionar após o runtime"
[ "\$(sha256sum "\$HOME/.dotnet/sdk/\$sdk/dotnet.dll")" = "\$before" ] || fail "arquivo do SDK alterado"
grep -c '"component"' "\$HOME/.local/share/dotnetscope/manifest.json" | grep -q '^2\$' || fail "manifesto sem as duas instalações"
printf '\nE2E OK: sdk %s e runtime %s\n' "\$sdk" "$RUNTIME_SPEC"
USER

step "Cenário como usuário comum"
runuser -u e2e -- sh /tmp/user.sh
