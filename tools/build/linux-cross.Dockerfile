# Toolchain de publicação Native AOT para RIDs Linux.
#
# Combina a imagem oficial de cross-build do .NET (clang/lld e sysroot com glibc 2.27
# ou musl 1.2.3) com o SDK oficial, para que o binário rode em distribuições antigas.
# Use somente via tools/scripts/Publish-Aot.ps1, que fixa as imagens por digest.
#
# CROSS_IMAGE: mcr.microsoft.com/dotnet-buildtools/prereqs:azurelinux-3.0-net10.0-cross-<arch>[-musl]
# SDK_IMAGE:   mcr.microsoft.com/dotnet/sdk:<versão>
ARG CROSS_IMAGE
ARG SDK_IMAGE

FROM ${SDK_IMAGE} AS sdk

FROM ${CROSS_IMAGE}
COPY --from=sdk /usr/share/dotnet /usr/share/dotnet
RUN ln -s /usr/share/dotnet/dotnet /usr/local/bin/dotnet \
    && dotnet --version
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
