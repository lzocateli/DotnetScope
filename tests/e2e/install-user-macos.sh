#!/bin/sh
# Cenário E2E do escopo user no macOS, com HOME isolado em diretório temporário (nada do usuário real é tocado).
# Uso: sh install-user-macos.sh <caminho do dotnetscope> [sdk] [runtime]
set -eu

BIN="${1:?informe o caminho do dotnetscope}"
SDK_SPEC="${2:-lts}"
RUNTIME_SPEC="${3:-9.0}"

step() { printf '\n==> %s\n' "$*"; }
fail() { printf 'FALHA: %s\n' "$*" >&2; exit 1; }

[ "$(id -u)" -ne 0 ] || fail "execute como usuário comum"
WORK="$(mktemp -d /tmp/dotnetscope-e2e.XXXXXX)"
WORK="$(cd "$WORK" && pwd -P)"
trap 'rm -rf "$WORK"' EXIT
export HOME="$WORK/home"
mkdir -p "$HOME"
unset XDG_CONFIG_HOME XDG_DATA_HOME XDG_STATE_HOME XDG_CACHE_HOME DOTNET_ROOT
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
cd "$HOME"

# ~/.zshrc do usuário e ausência de ~/.zprofile: o macOS cria ~/.zprofile e edita só o que já existe.
printf '# zshrc do usuário\n' >.zshrc
zshrc_before=$(shasum -a 256 <.zshrc)

step "Instalação do SDK $SDK_SPEC com ambiente (padrão)"
"$BIN" install sdk "$SDK_SPEC" --yes --output json >install.json
grep -q '"status": "installed"' install.json || fail "status: $(cat install.json)"
grep -q '"fileName": "dotnet-sdk-osx-.*\.tar\.gz"' install.json || fail "artefato não é .tar.gz do macOS"
grep -q '"succeeded": true' install.json || fail "validação falhou"
grep -q '"status": "applied"' install.json || fail "ambiente não aplicado"
grep -q '"matches": true' install.json || fail "shell de login não resolveu dotnet para a raiz: $(cat install.json)"
sdk=$(sed -n 's/^  "version": "\(.*\)",$/\1/p' install.json)
[ -n "$sdk" ] || fail "versão não encontrada"
[ -d "$HOME/.dotnet/sdk/$sdk" ] || fail "sdk/$sdk ausente"
[ -f "$HOME/.zprofile" ] || fail "HOME/.zprofile não foi criado"
grep -q '# >>> dotnetscope >>>' "$HOME/.zprofile" || fail "bloco ausente em ~/.zprofile"
[ ! -e "$HOME/.profile" ] || fail "HOME/.profile não deveria ser criado no macOS"
[ "$(stat -f '%Lp' "$HOME/.local/share/dotnetscope/manifest.json")" = 600 ] || fail "manifesto sem modo 600"

step "Shells de login novos resolvem dotnet (zsh e bash)"
expected="$HOME/.dotnet/dotnet"
[ "$(env -i HOME="$HOME" PATH=/usr/bin:/bin /bin/zsh -lc 'command -v dotnet')" = "$expected" ] || fail "zsh -l"
printf '# bash_profile do usuário\n' >.bash_profile
"$BIN" env apply >/dev/null
[ "$(env -i HOME="$HOME" PATH=/usr/bin:/bin /bin/bash -lc 'command -v dotnet')" = "$expected" ] || fail "bash -l"
[ "$(env -i HOME="$HOME" PATH=/usr/bin:/bin /bin/zsh -lc 'dotnet --version')" = "$sdk" ] || fail "dotnet --version no login"

step "SDK funcional"
# shellcheck disable=SC1091 # gerado pelo dotnetscope durante o teste.
. "$HOME/.config/dotnetscope/env.sh"
[ "$(dotnet --version)" = "$sdk" ] || fail "dotnet --version diferente de $sdk"
dotnet new console --output app --name e2e >/dev/null
(cd app && dotnet run) | grep -q 'Hello, World!' || fail "aplicação de console não executou"

step "Idempotência"
snapshot=$(cat .zprofile .zshrc .bash_profile | shasum -a 256)
"$BIN" install sdk "$sdk" | grep -q 'já está instalado' || fail "segunda instalação não foi idempotente"
"$BIN" env apply | grep -q 'já configurado' || fail "env apply não foi idempotente"
[ "$(cat .zprofile .zshrc .bash_profile | shasum -a 256)" = "$snapshot" ] || fail "perfis mudaram na segunda aplicação"

step "Runtime $RUNTIME_SPEC lado a lado, inventário e doctor"
"$BIN" install runtime "$RUNTIME_SPEC" --yes --configure-env false >/dev/null
dotnet --list-runtimes | grep -q "^Microsoft.NETCore.App $RUNTIME_SPEC" || fail "runtime $RUNTIME_SPEC ausente"
"$BIN" list --installed | grep -q "^sdk .*$sdk .*dotnetscope" || fail "list --installed não mostra o SDK gerenciado"
"$BIN" doctor || fail "doctor encontrou problemas em instalação saudável"

step "uninstall devolve a raiz e os perfis ao estado original"
rt=$(dotnet --list-runtimes | sed -n "s/^Microsoft.NETCore.App \($RUNTIME_SPEC\.[0-9]*\) .*/\1/p" | head -1)
[ -n "$rt" ] || fail "versão exata do runtime não encontrada"
"$BIN" uninstall runtime "$rt" >/dev/null
"$BIN" uninstall sdk "$sdk" | grep -q 'DOTNET_ROOT/PATH foram removidos' || fail "ambiente não removido com a última instalação"
for leftover in dotnet LICENSE.txt ThirdPartyNotices.txt host shared sdk packs templates sdk-manifests; do
  [ ! -e "$HOME/.dotnet/$leftover" ] || fail "$leftover permaneceu na raiz"
done
[ ! -e "$HOME/.zprofile" ] || fail "HOME/.zprofile criado pelo dotnetscope não foi removido"
[ "$(shasum -a 256 <.zshrc)" = "$zshrc_before" ] || fail "HOME/.zshrc mudou"
[ "$(cat .bash_profile)" = "# bash_profile do usuário" ] || fail "HOME/.bash_profile não foi restaurado"
"$BIN" doctor || fail "doctor encontrou problemas após remover tudo"

printf '\nE2E macOS user aprovado: sdk %s e runtime %s instalados e removidos.\n' "$sdk" "$rt"
