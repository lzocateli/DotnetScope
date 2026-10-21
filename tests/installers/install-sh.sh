#!/bin/sh
# Cenarios do install.sh contra um servidor local de artefatos.
# Executado por tools/scripts/Test-Installers.ps1 em containers limpos ou no macOS.
# Variaveis: INSTALLER (caminho do install.sh), BASE_URL (artefatos validos),
# TAMPERED_URL (mesmos pacotes com SHA256SUMS adulterado).
set -eu

installer="${INSTALLER:?INSTALLER nao definido}"
base_url="${BASE_URL:?BASE_URL nao definido}"
tampered_url="${TAMPERED_URL:?TAMPERED_URL nao definido}"

work="$(mktemp -d "${TMPDIR:-/tmp}/dotnetscope-installer-test.XXXXXX")"
trap 'rm -rf "$work"' EXIT

fail() {
    printf 'FALHA: %s\n' "$1" >&2
    if [ -f "$work/out" ]; then
        sed 's/^/  | /' "$work/out" >&2
    fi
    exit 1
}

pass() {
    printf 'ok: %s\n' "$1"
}

expect_code() {
    expected="$1"
    description="$2"
    shift 2
    set +e
    "$@" > "$work/out" 2>&1
    code=$?
    set -e
    if [ "$code" -ne "$expected" ]; then
        fail "$description: codigo $code, esperado $expected"
    fi
}

expect_code 0 "--help" sh "$installer" --help
grep -q -- '--install-dir' "$work/out" || fail "--help sem a opcao --install-dir"
pass "--help"

expect_code 2 "argumento desconhecido" sh "$installer" --bogus
expect_code 2 "http fora do loopback" sh "$installer" --base-url http://example.com/x
expect_code 2 "credenciais na URL" sh "$installer" --base-url http://localhost:80@example.com/x
expect_code 2 "diretorio relativo" sh "$installer" --base-url "$base_url" --install-dir relativo
expect_code 2 "--global com --install-dir" sh "$installer" --global --install-dir "$work/x"
expect_code 2 "--base-url com --version" sh "$installer" --base-url "$base_url" --version 0.1.0
expect_code 2 "versao invalida" sh "$installer" --version '1.0.0;id'
pass "erros de uso retornam 2"

expect_code 1 "checksum adulterado" sh "$installer" --base-url "$tampered_url" --install-dir "$work/tampered"
grep -q 'nao confere' "$work/out" || fail "checksum adulterado sem mensagem de divergencia"
[ ! -e "$work/tampered" ] || fail "checksum adulterado criou o diretorio de destino"
pass "checksum adulterado rejeitado sem efeitos"

expect_code 1 "versao inexistente no servidor" sh "$installer" --base-url "$base_url/inexistente" --install-dir "$work/missing"
[ ! -e "$work/missing" ] || fail "download falho criou o diretorio de destino"
pass "download falho rejeitado sem efeitos"

expect_code 0 "instalacao em diretorio explicito" sh "$installer" --base-url "$base_url" --install-dir "$work/bin"
[ -x "$work/bin/dotnetscope" ] || fail "binario ausente ou sem permissao de execucao"
grep -q 'nao esta no PATH' "$work/out" || fail "aviso de PATH ausente"
"$work/bin/dotnetscope" --version > /dev/null || fail "binario instalado nao executa"
pass "instalacao em diretorio explicito"

printf 'versao anterior\n' > "$work/bin/dotnetscope"
expect_code 0 "reinstalacao" env "PATH=$work/bin:$PATH" sh "$installer" --base-url "$base_url" --install-dir "$work/bin"
if grep -q 'nao esta no PATH' "$work/out"; then
    fail "aviso de PATH com o diretorio ja no PATH"
fi
"$work/bin/dotnetscope" --version > /dev/null || fail "reinstalacao nao substituiu o binario"
[ "$(ls -A "$work/bin")" = "dotnetscope" ] || fail "arquivos temporarios restantes em $work/bin"
pass "reinstalacao substitui o binario sem residuos"

mkdir "$work/link"
ln -s "$work/bin/dotnetscope" "$work/link/dotnetscope"
expect_code 1 "destino link simbolico" sh "$installer" --base-url "$base_url" --install-dir "$work/link"
if [ "$(ls -A "$work/link")" != "dotnetscope" ] || [ ! -h "$work/link/dotnetscope" ]; then
    fail "link simbolico alterado"
fi
pass "destino link simbolico recusado"

if [ "$(id -u)" -eq 0 ]; then
    expect_code 0 "--global como root" sh "$installer" --base-url "$base_url" --global
    [ -x /usr/local/bin/dotnetscope ] || fail "--global nao instalou em /usr/local/bin"
    pass "--global como root"

    mkdir "$work/foreign" "$work/shared"
    chown 65534 "$work/foreign"
    chmod 0777 "$work/shared"
    expect_code 1 "root em diretorio de outro usuario" sh "$installer" --base-url "$base_url" --install-dir "$work/foreign"
    grep -q 'nao pertence ao root' "$work/out" || fail "diretorio de outro usuario sem mensagem de dono"
    expect_code 1 "root em diretorio gravavel por outros" sh "$installer" --base-url "$base_url" --install-dir "$work/shared"
    grep -q 'gravavel por grupo ou outros' "$work/out" || fail "diretorio gravavel sem mensagem de permissao"
    if [ -n "$(ls -A "$work/foreign")" ] || [ -n "$(ls -A "$work/shared")" ]; then
        fail "instalacao recusada deixou arquivos"
    fi
    pass "root recusa diretorio controlado por outro usuario"
else
    expect_code 1 "--global sem root" sh "$installer" --base-url "$base_url" --global
    grep -q 'exige root' "$work/out" || fail "--global sem root sem mensagem de privilegio"
    pass "--global sem root recusado"

    expect_code 0 "diretorio padrao" sh "$installer" --base-url "$base_url"
    [ -x "$HOME/.local/bin/dotnetscope" ] || fail "binario ausente em \$HOME/.local/bin"
    pass "diretorio padrao \$HOME/.local/bin"
fi

printf 'install.sh aprovado (uid %s)\n' "$(id -u)"
