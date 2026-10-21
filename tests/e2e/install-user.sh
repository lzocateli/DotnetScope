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
  apt-get install -y -qq --no-install-recommends ca-certificates libstdc++6 libgcc-s1 "$icu" "$ssl" zsh fish >/dev/null
elif command -v dnf >/dev/null 2>&1; then
  dnf install -y -q ca-certificates libicu libstdc++ libgcc openssl-libs util-linux shadow-utils zsh fish >/dev/null
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

useradd -m -s /bin/bash e2e
cat >/tmp/user.sh <<USER
set -eu
fail() { printf 'FALHA: %s\n' "\$*" >&2; exit 1; }
cd "\$HOME"
# Perfis originais (do /etc/skel) e um ~/.zshrc do usuário, guardados para comparar byte a byte.
printf '# zshrc do usuário\n' >.zshrc
mkdir -p originals
for f in .profile .bashrc .bash_profile .zshrc; do [ -f "\$f" ] && cp -p "\$f" "originals/\$f"; done
same_as_originals() {
  for f in .profile .bashrc .bash_profile .zshrc; do
    if [ -f "originals/\$f" ]; then
      [ "\$(sha256sum <"\$f")" = "\$(sha256sum <"originals/\$f")" ] || return 1
    else
      [ ! -e "\$f" ] || return 1
    fi
  done
}
dotnetscope --version
dotnetscope list --available lts >/dev/null

printf '\n==> Instalação do SDK %s com ambiente (padrão)\n' "$SDK_SPEC"
dotnetscope install sdk "$SDK_SPEC" --yes --output json >install.json
grep -q '"status": "installed"' install.json || fail "status inesperado: \$(cat install.json)"
grep -q '"succeeded": true' install.json || fail "validação falhou"
grep -q '"status": "applied"' install.json || fail "ambiente não aplicado"
grep -q '"matches": true' install.json || fail "shell de login não resolveu dotnet para a raiz"
sdk=\$(sed -n 's/^  "version": "\(.*\)",\$/\1/p' install.json)
[ -n "\$sdk" ] || fail "versão não encontrada na saída JSON"
test -d "\$HOME/.dotnet/sdk/\$sdk" || fail "sdk/\$sdk ausente"
[ "\$(stat -c %a "\$HOME/.local/share/dotnetscope/manifest.json")" = 600 ] || fail "manifesto sem modo 600"
! ls -a "\$HOME/.dotnet" | grep -q dotnetscope-staging || fail "staging remanescente"

printf '\n==> Shells de login novos resolvem dotnet (bash, zsh, fish)\n'
expected="\$HOME/.dotnet/dotnet"
[ "\$(env -i HOME="\$HOME" PATH=/usr/bin:/bin bash -lc 'command -v dotnet')" = "\$expected" ] || fail "bash -l"
[ "\$(env -i HOME="\$HOME" PATH=/usr/bin:/bin zsh -ic 'command -v dotnet' 2>/dev/null)" = "\$expected" ] || fail "zsh -i"
[ "\$(env -i HOME="\$HOME" PATH=/usr/bin:/bin fish -lc 'command -v dotnet')" = "\$expected" ] || fail "fish -l"
[ "\$(env -i HOME="\$HOME" PATH=/usr/bin:/bin bash -lc 'dotnet --version')" = "\$sdk" ] || fail "dotnet --version no login"

printf '\n==> SDK funcional\n'
. "\$HOME/.config/dotnetscope/env.sh"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
[ "\$(dotnet --version)" = "\$sdk" ] || fail "dotnet --version diferente de \$sdk"
dotnet new console --output app --name e2e >/dev/null
(cd app && dotnet run) | grep -q 'Hello, World!' || fail "aplicação de console não executou"

printf '\n==> Reinstalação e env apply idempotentes\n'
snapshot=\$(cat .profile .bashrc .zshrc | sha256sum)
dotnetscope install sdk "\$sdk" | grep -q 'já está instalado' || fail "segunda instalação não foi idempotente"
dotnetscope env apply | grep -q 'já configurado' || fail "env apply não foi idempotente"
[ "\$(cat .profile .bashrc .zshrc | sha256sum)" = "\$snapshot" ] || fail "perfis mudaram na segunda aplicação"

printf '\n==> env remove restaura os perfis originais\n'
dotnetscope env remove
same_as_originals || fail "perfis diferem dos originais após env remove"
test ! -e "\$HOME/.config/dotnetscope/env.sh" || fail "env.sh remanescente"
test ! -e "\$HOME/.config/fish/conf.d/dotnetscope.fish" || fail "script do fish remanescente"

printf '\n==> Runtime %s lado a lado com --configure-env false\n' "$RUNTIME_SPEC"
before=\$(sha256sum "\$HOME/.dotnet/sdk/\$sdk/dotnet.dll")
dotnetscope install runtime "$RUNTIME_SPEC" --yes --configure-env false
same_as_originals || fail "--configure-env false alterou perfis"
dotnet --list-runtimes | grep -q '^Microsoft.NETCore.App $RUNTIME_SPEC' || fail "runtime $RUNTIME_SPEC ausente"
[ "\$(dotnet --version)" = "\$sdk" ] || fail "SDK deixou de funcionar após o runtime"
[ "\$(sha256sum "\$HOME/.dotnet/sdk/\$sdk/dotnet.dll")" = "\$before" ] || fail "arquivo do SDK alterado"
grep -c '"component"' "\$HOME/.local/share/dotnetscope/manifest.json" | grep -q '^2\$' || fail "manifesto sem as duas instalações"

printf '\n==> list --installed, doctor e global.json\n'
dotnetscope list --installed | grep -q "^sdk .*\$sdk .*dotnetscope" || fail "list --installed não mostra o SDK gerenciado"
dotnetscope doctor || fail "doctor encontrou problemas em instalação saudável"
printf '{ "sdk": { "version": "%s", "rollForward": "disable" } }\n' "\$sdk" >global.json
dotnetscope install --from-global-json --dry-run --output json | grep -q '"status": "alreadyInstalled"' || fail "--from-global-json não resolveu \$sdk"
rm global.json

printf '\n==> uninstall devolve a raiz e os perfis ao estado original\n'
rt=\$(dotnet --list-runtimes | sed -n 's/^Microsoft.NETCore.App \($RUNTIME_SPEC\.[0-9]*\) .*/\1/p' | head -1)
[ -n "\$rt" ] || fail "versão exata do runtime $RUNTIME_SPEC não encontrada"
dotnetscope uninstall runtime "\$rt"
! dotnet --list-runtimes | grep -q "^Microsoft.NETCore.App \$rt " || fail "runtime \$rt ainda presente"
[ "\$(dotnet --version)" = "\$sdk" ] || fail "SDK quebrou após remover o runtime"
dotnetscope env apply >/dev/null
dotnetscope uninstall sdk "\$sdk" | grep -q 'DOTNET_ROOT/PATH foram removidos' || fail "ambiente não removido com a última instalação"
# ~/.dotnet também é o DOTNET_CLI_HOME: arquivos do usuário criados pelo SDK (sentinela, telemetria) são preservados.
for leftover in dotnet dnx LICENSE.txt ThirdPartyNotices.txt host shared sdk packs templates sdk-manifests; do
  [ ! -e "\$HOME/.dotnet/\$leftover" ] || fail "\$leftover permaneceu na raiz após remover tudo"
done
same_as_originals || fail "perfis diferem dos originais após o último uninstall"
! grep -q '"component"' "\$HOME/.local/share/dotnetscope/manifest.json" || fail "manifesto não ficou vazio"
dotnetscope doctor || fail "doctor encontrou problemas após remover tudo"
printf '\nE2E OK: sdk %s e runtime %s instalados, verificados e removidos\n' "\$sdk" "\$rt"
USER

step "Cenário como usuário comum"
runuser -u e2e -- sh /tmp/user.sh
