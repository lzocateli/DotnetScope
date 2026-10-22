---
name: "Código .NET do DotnetScope"
description: "Use ao criar ou alterar C#, .NET 10, projetos, CLI, domínio, estratégias de instalação, metadata, download, extração ou adaptadores de plataforma do DotnetScope."
applyTo: "src/**, Directory.Build.props, Directory.Packages.props, global.json, *.slnx"
---

# Código .NET

- Use .NET 10 LTS, `Nullable`, `ImplicitUsings`, `TreatWarningsAsErrors` e analyzers ativos; versões de pacotes ficam centralizadas em `Directory.Packages.props`.
- Todo projeto executável é compatível com Native AOT: `IsAotCompatible`, `PublishAot`, `InvariantGlobalization` e avisos de trimming/AOT como erro.
- Sem reflexão própria, `dynamic`, `Assembly.Load` ou serializadores baseados em reflexão; JSON usa `JsonSerializerContext` (source generation) e P/Invoke usa `LibraryImport`.
- Respeite as camadas: `DotnetScope.Cli` compõe `DotnetScope.Core` (domínio e políticas, sem I/O) e `DotnetScope.Infrastructure` (HTTP, filesystem, processos, registro, perfis). `Infrastructure` referencia `Core`, nunca o inverso.
- Dependências de plataforma passam por interfaces (`IPlatformDetector`, `IReleaseMetadataClient`, `IInstallStrategy`, `IEnvironmentConfigurator`, `IProcessRunner`, `IFileSystem`) e são escolhidas por SO e escopo na composição.
- Use tipos imutáveis (`record`) para `InstallRequest`, `VersionSpec`, `ResolvedArtifact`, `InstallPlan` e resultados.
- Propague `CancellationToken`; não use `.Result`, `.Wait()` ou `async void`. Ctrl+C cancela, limpa staging e mantém o estado consistente.
- `HttpClient` é criado uma vez, com timeout, retry exponencial apenas para falhas transitórias, respeito a `HTTPS_PROXY`/`NO_PROXY` e às CAs do sistema (`SSL_CERT_DIR`/`SSL_CERT_FILE`).
- Hash SHA-512 é calculado em streaming e comparado em tempo constante antes de qualquer extração ou execução.
- Processos externos (`apt-get`, `installer`, instaladores Windows) usam `ProcessStartInfo.ArgumentList`, caminho absoluto resolvido, ambiente controlado e timeout; nunca `/bin/sh -c` com interpolação.
- APIs específicas de SO ficam atrás de `OperatingSystem.IsWindows()`/`IsLinux()`/`IsMacOS()` e `[SupportedOSPlatform]`, sem quebrar o analisador de plataforma.
- Escrita de arquivos usa arquivo temporário no mesmo diretório e `File.Move(..., overwrite: true)` somente após validação; preserve permissões e modo de execução.
- Saída humana vai para stdout, diagnósticos para stderr; `--output json` é contrato estável e não mistura texto livre. Respeite `NO_COLOR` e ausência de TTY.
- Códigos de saída são documentados e estáveis; erro de uso, falha de rede, falha de integridade, conflito e falta de privilégio têm códigos distintos.
- Mensagens ao usuário em pt-BR; identificadores, opções e chaves JSON em inglês.
- Não registre conteúdo de perfis, variáveis de ambiente completas, tokens ou URLs com credenciais.
