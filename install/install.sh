#!/bin/sh
# Instalador do dotnetscope para Linux e macOS.
# Uso: curl -fsSL https://github.com/lzocateli/DotnetScope/releases/latest/download/install.sh | sh
#      sh install.sh --help
set -eu

repository_url="https://github.com/lzocateli/DotnetScope/releases"
version="latest"
install_dir=""
global=0
base_url=""
work_dir=""
staged_binary=""

usage() {
    cat <<'EOF'
Instala o dotnetscope (binario Native AOT, sem dependencia de .NET) a partir das
releases do GitHub, conferindo o SHA-256 do pacote com o SHA256SUMS da release.

Uso:
  install.sh [--version <X.Y.Z|latest>] [--install-dir <dir> | --global]
  install.sh --help

Opcoes:
  --version <v>       Versao a instalar (X.Y.Z ou X.Y.Z-sufixo). Padrao: latest.
  --install-dir <dir> Diretorio absoluto de destino. Padrao: $HOME/.local/bin.
  --global            Instala em /usr/local/bin. Exige root (use sudo); o
                      instalador nunca se autoeleva.
  --base-url <url>    Diretorio com os pacotes e o SHA256SUMS (espelho ou
                      testes). Exige https://, exceto http://127.0.0.1 e
                      http://localhost. Nao combina com --version.
  --help              Mostra esta ajuda.

Requisitos: curl ou wget, tar com gzip e sha256sum ou shasum.
Plataformas: linux-x64, linux-arm64, linux-musl-x64, osx-arm64, osx-x64.

O instalador nao altera perfis de shell nem o PATH: se o diretorio de destino
nao estiver no PATH, ele mostra a linha a adicionar. Para configurar o .NET,
use depois 'dotnetscope install' (Linux), que ajusta DOTNET_ROOT e PATH
(--configure-env, padrao true).

Exemplos:
  sh install.sh
  sh install.sh --version 0.1.0 --install-dir "$HOME/bin"
  curl -fsSL https://github.com/lzocateli/DotnetScope/releases/latest/download/install.sh | sudo sh -s -- --global

Codigos de saida: 0 sucesso; 1 falha; 2 erro de uso.
EOF
}

die() {
    printf 'dotnetscope-install: %s\n' "$1" >&2
    exit 1
}

usage_error() {
    printf 'dotnetscope-install: %s. Use --help.\n' "$1" >&2
    exit 2
}

cleanup() {
    if [ -n "$staged_binary" ] && [ -e "$staged_binary" ]; then
        rm -f "$staged_binary"
    fi
    if [ -n "$work_dir" ] && [ -d "$work_dir" ]; then
        rm -rf "$work_dir"
    fi
}

require_value() {
    if [ $# -lt 2 ] || [ -z "$2" ]; then
        usage_error "a opcao $1 exige um valor"
    fi
}

version_set=0
while [ $# -gt 0 ]; do
    case "$1" in
        --help|-h)
            usage
            exit 0
            ;;
        --version)
            require_value "$@"
            version="$2"
            version_set=1
            shift 2
            ;;
        --version=*)
            version="${1#*=}"
            version_set=1
            shift
            ;;
        --install-dir)
            require_value "$@"
            install_dir="$2"
            shift 2
            ;;
        --install-dir=*)
            install_dir="${1#*=}"
            shift
            ;;
        --global)
            global=1
            shift
            ;;
        --base-url)
            require_value "$@"
            base_url="$2"
            shift 2
            ;;
        --base-url=*)
            base_url="${1#*=}"
            shift
            ;;
        *)
            usage_error "argumento desconhecido: $1"
            ;;
    esac
done

version="${version#v}"
if [ "$version" != "latest" ]; then
    case "$version" in
        [0-9]*.[0-9]*.[0-9]*) ;;
        *) usage_error "versao invalida: $version" ;;
    esac
    case "$version" in
        *[!0-9A-Za-z.-]*) usage_error "versao invalida: $version" ;;
    esac
fi

if [ "$global" -eq 1 ] && [ -n "$install_dir" ]; then
    usage_error "--global e --install-dir sao mutuamente exclusivos"
fi

secure=1
if [ -n "$base_url" ]; then
    if [ "$version_set" -eq 1 ]; then
        usage_error "--base-url e --version sao mutuamente exclusivos"
    fi
    base_url="${base_url%/}"
    case "$base_url" in
        https://?*) ;;
        http://?*)
            host_port="${base_url#http://}"
            host_port="${host_port%%/*}"
            case "$host_port" in
                *@*) usage_error "--base-url nao aceita credenciais" ;;
            esac
            case "${host_port%:*}" in
                127.0.0.1|localhost) secure=0 ;;
                *) usage_error "--base-url http:// so e aceito para 127.0.0.1 ou localhost" ;;
            esac
            ;;
        *) usage_error "--base-url deve usar https://" ;;
    esac
    asset_url="$base_url"
elif [ "$version" = "latest" ]; then
    asset_url="$repository_url/latest/download"
else
    asset_url="$repository_url/download/v$version"
fi

if [ "$global" -eq 1 ]; then
    if [ "$(id -u)" -ne 0 ]; then
        die "--global exige root; execute com sudo (o instalador nao se autoeleva)"
    fi
    install_dir="/usr/local/bin"
elif [ -z "$install_dir" ]; then
    if [ -z "${HOME:-}" ]; then
        die "HOME nao definido; informe --install-dir"
    fi
    install_dir="$HOME/.local/bin"
fi

case "$install_dir" in
    /*) ;;
    *) usage_error "--install-dir deve ser um caminho absoluto" ;;
esac

os_name="$(uname -s)"
machine="$(uname -m)"
case "$machine" in
    x86_64|amd64) arch="x64" ;;
    aarch64|arm64) arch="arm64" ;;
    *) die "arquitetura nao suportada: $machine" ;;
esac

case "$os_name" in
    Linux)
        libc=""
        for loader in /lib/ld-musl-*.so.1; do
            if [ -e "$loader" ]; then
                libc="musl-"
            fi
        done
        rid="linux-$libc$arch"
        if [ "$rid" = "linux-musl-arm64" ]; then
            die "linux-musl-arm64 ainda nao e publicado"
        fi
        ;;
    Darwin)
        # Shell x64 sob Rosetta reporta x86_64 em Mac Apple Silicon.
        if [ "$arch" = "x64" ] && [ "$(sysctl -n sysctl.proc_translated 2>/dev/null || echo 0)" = "1" ]; then
            arch="arm64"
        fi
        rid="osx-$arch"
        ;;
    *)
        die "sistema operacional nao suportado: $os_name (no Windows, use install.ps1)"
        ;;
esac

asset="dotnetscope-$rid.tar.gz"

download() {
    if command -v curl >/dev/null 2>&1; then
        if [ "$secure" -eq 1 ]; then
            curl --proto '=https' --proto-redir '=https' -fsSL --retry 3 -o "$2" "$1"
        else
            curl -fsSL -o "$2" "$1"
        fi
    elif command -v wget >/dev/null 2>&1; then
        wget -q -O "$2" "$1"
    else
        die "curl ou wget e necessario"
    fi
}

sha256_of() {
    if command -v sha256sum >/dev/null 2>&1; then
        sha256sum "$1" | cut -d ' ' -f 1
    elif command -v shasum >/dev/null 2>&1; then
        shasum -a 256 "$1" | cut -d ' ' -f 1
    else
        die "sha256sum ou shasum e necessario"
    fi
}

trap cleanup EXIT
trap 'exit 130' HUP INT TERM

work_dir="$(mktemp -d "${TMPDIR:-/tmp}/dotnetscope-install.XXXXXX")" || die "falha ao criar diretorio temporario"

printf 'Baixando %s de %s\n' "$asset" "$asset_url"
download "$asset_url/SHA256SUMS" "$work_dir/SHA256SUMS" || die "falha ao baixar SHA256SUMS de $asset_url"
download "$asset_url/$asset" "$work_dir/$asset" || die "falha ao baixar $asset (versao ou plataforma inexistente?)"

expected="$(awk -v name="$asset" '$2 == name { count++; hash = $1 } END { if (count == 1) print hash }' "$work_dir/SHA256SUMS")"
case "$expected" in
    ''|*[!0-9a-f]*) die "SHA256SUMS nao contem uma entrada valida para $asset" ;;
esac
if [ "${#expected}" -ne 64 ]; then
    die "SHA256SUMS nao contem uma entrada valida para $asset"
fi

actual="$(sha256_of "$work_dir/$asset")"
if [ "$actual" != "$expected" ]; then
    die "SHA-256 de $asset nao confere (esperado $expected, obtido $actual); nada foi instalado"
fi
printf 'SHA-256 conferido: %s\n' "$actual"

mkdir "$work_dir/extract"
tar -xzf "$work_dir/$asset" -C "$work_dir/extract" || die "falha ao extrair $asset"
if [ ! -f "$work_dir/extract/dotnetscope" ] || [ -h "$work_dir/extract/dotnetscope" ]; then
    die "$asset nao contem o binario dotnetscope"
fi

target="$install_dir/dotnetscope"
if [ -h "$target" ] || [ -d "$target" ]; then
    die "$target e um link simbolico ou diretorio; remova-o antes de instalar"
fi

mkdir -p "$install_dir" || die "falha ao criar $install_dir"

# Como root, um diretorio controlado por outro usuario permitiria trocar os arquivos
# temporarios por links simbolicos e sobrescrever arquivos do sistema.
if [ "$(id -u)" -eq 0 ]; then
    # shellcheck disable=SC2012 # ls -ldn e a forma POSIX de obter dono e modo.
    dir_info="$(ls -ldn "$install_dir")"
    dir_mode="$(printf '%s\n' "$dir_info" | awk '{ print $1 }')"
    dir_owner="$(printf '%s\n' "$dir_info" | awk '{ print $3 }')"
    case "$dir_mode" in
        ?????w????*|????????w?*) die "$install_dir e gravavel por grupo ou outros; instalacao como root recusada" ;;
    esac
    if [ "$dir_owner" != "0" ]; then
        die "$install_dir nao pertence ao root; instale sem sudo com --install-dir \"$install_dir\""
    fi
fi

staged_binary="$(mktemp "$install_dir/.dotnetscope.install.XXXXXX")" || die "falha ao criar arquivo temporario em $install_dir"
cp "$work_dir/extract/dotnetscope" "$staged_binary" || die "falha ao copiar para $install_dir"
chmod 0755 "$staged_binary"

# Executa no destino (nao em /tmp, que pode ser noexec) antes de substituir a versao atual.
installed_version="$("$staged_binary" --version)" || die "o binario baixado nao executa nesta plataforma ($rid); nada foi substituido"
mv -f "$staged_binary" "$target" || die "falha ao instalar em $target"
staged_binary=""

printf 'dotnetscope %s instalado em %s\n' "$installed_version" "$target"

case ":${PATH:-}:" in
    *":$install_dir:"*) ;;
    *)
        printf '\nAviso: %s nao esta no PATH. Adicione ao perfil do seu shell:\n' "$install_dir"
        printf "  export PATH=\"%s:\$PATH\"\n" "$install_dir"
        ;;
esac

printf '\nProximo passo: dotnetscope list --available\n'
