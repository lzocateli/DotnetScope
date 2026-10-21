using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using DotnetScope.Core;
using DotnetScope.Core.Installation;
using DotnetScope.Core.Resolution;
using DotnetScope.Infrastructure.Http;
using DotnetScope.Infrastructure.Installation;
using Microsoft.Win32.SafeHandles;

namespace DotnetScope.Infrastructure.SystemScope;

/// <summary>Elevação e diretórios protegidos do escopo global no Windows (modelo de ameaças, R03 e R17).</summary>
[SupportedOSPlatform("windows")]
public static class WindowsSecurity
{
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier LocalSystem = new(WellKnownSidType.LocalSystemSid, null);

    private const FileSystemRights WriteRights = FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.WriteExtendedAttributes
        | FileSystemRights.WriteAttributes | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles
        | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    /// <summary>Token elevado: sob UAC, um administrador sem elevação não está no papel de Administradores.</summary>
    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Cria (com ACL protegida, sem herança, só Administradores e SYSTEM) ou valida um diretório do estado global. Um
    /// diretório existente de outro dono ou que conceda escrita a outras identidades é recusado (código 7): em
    /// ProgramData, usuários comuns podem criar pastas e controlar a ACL delas.
    /// </summary>
    public static void EnsureProtectedDirectory(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var info = new DirectoryInfo(path);
        if (!info.Exists)
        {
            var parent = info.Parent ?? throw new DotnetScopeException(ExitCodes.Privilege, $"{path} não tem diretório pai.");
            if (!parent.Exists)
            {
                EnsureProtectedDirectory(parent.FullName);
            }

            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.SetOwner(Administrators);
            foreach (var sid in new[] { Administrators, LocalSystem })
            {
                security.AddAccessRule(new FileSystemAccessRule(
                    sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            }

            info.Create(security);
        }

        if (info.LinkTarget is not null)
        {
            throw new DotnetScopeException(ExitCodes.Privilege, $"{path} é uma junção ou link simbólico; o estado global não é usado por links.");
        }

        var acl = info.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
        if (acl.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || (owner != Administrators && owner != LocalSystem))
        {
            throw new DotnetScopeException(ExitCodes.Privilege, $"{path} não pertence a Administradores nem a SYSTEM; remova-o e execute novamente.");
        }

        foreach (FileSystemAccessRule rule in acl.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType == AccessControlType.Allow
                && (rule.FileSystemRights & WriteRights) != 0
                && rule.IdentityReference is SecurityIdentifier sid
                && sid != Administrators
                && sid != LocalSystem)
            {
                throw new DotnetScopeException(ExitCodes.Privilege, $"{path} concede escrita a {sid.Value}; ele não é seguro para o estado global.");
            }
        }
    }
}

/// <summary>Verificação Authenticode com <c>WinVerifyTrust</c> e exigência de assinante Microsoft (D-F7-1, R02).</summary>
[SupportedOSPlatform("windows")]
public static partial class Authenticode
{
    private const uint UiNone = 2;
    private const uint RevokeNone = 0;
    private const uint ChoiceFile = 1;
    private const uint StateActionVerify = 1;
    private const uint StateActionClose = 2;
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    /// <summary>Lança código 5 se a assinatura for inválida ou o assinante não for a Microsoft Corporation.</summary>
    public static void VerifyMicrosoftSignature(SafeFileHandle handle, string path)
    {
        ArgumentNullException.ThrowIfNull(handle);
        var status = Verify(handle, path);
        if (status != 0)
        {
            throw new DotnetScopeException(ExitCodes.Integrity, $"Assinatura Authenticode inválida em {Path.GetFileName(path)} (0x{status:X8}); nada foi executado.");
        }

#pragma warning disable SYSLIB0057 // Lê o certificado do assinante já validado por WinVerifyTrust acima.
        using var signer = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
        if (!SignerPolicy.IsMicrosoft(signer.SubjectName, signer.IssuerName))
        {
            throw new DotnetScopeException(ExitCodes.Integrity, $"{Path.GetFileName(path)} não é assinado pela Microsoft ({signer.Subject}); nada foi executado.");
        }
    }

    private static unsafe int Verify(SafeFileHandle handle, string path)
    {
        fixed (char* filePath = path)
        {
            var file = new WinTrustFileInfo
            {
                StructSize = (uint)sizeof(WinTrustFileInfo),
                FilePath = (nint)filePath,
                File = handle.DangerousGetHandle(),
            };
            var data = new WinTrustData
            {
                StructSize = (uint)sizeof(WinTrustData),
                UiChoice = UiNone,
                RevocationChecks = RevokeNone,
                UnionChoice = ChoiceFile,
                File = (nint)(&file),
                StateAction = StateActionVerify,
            };
            var action = GenericVerifyV2;
            var result = WinVerifyTrust(-1, &action, &data);
            data.StateAction = StateActionClose;
            _ = WinVerifyTrust(-1, &action, &data);
            return result;
        }
    }

    [LibraryImport("wintrust.dll")]
    private static unsafe partial int WinVerifyTrust(nint window, Guid* action, WinTrustData* data);

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustFileInfo
    {
        public uint StructSize;
        public nint FilePath;
        public nint File;
        public nint KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint StructSize;
        public nint PolicyCallbackData;
        public nint SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public nint File;
        public uint StateAction;
        public nint StateData;
        public nint UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
        public nint SignatureSettings;
    }
}

/// <summary>
/// Assinante aceito para os instaladores oficiais: um único O exatamente "Microsoft Corporation" no certificado do
/// assinante e no do emissor. O CN varia (por exemplo, ".NET"); o emissor da Microsoft impede certificados de terceiros
/// com O semelhante, e os emitidos pela Microsoft a clientes (Trusted Signing) trazem o O do cliente no assinante.
/// </summary>
public static class SignerPolicy
{
    private const string OrganizationOid = "2.5.4.10";
    private const string Microsoft = "Microsoft Corporation";

    public static bool IsMicrosoft(X500DistinguishedName subject, X500DistinguishedName issuer)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(issuer);
        return HasOnlyMicrosoftOrganization(subject) && HasOnlyMicrosoftOrganization(issuer);
    }

    private static bool HasOnlyMicrosoftOrganization(X500DistinguishedName name)
    {
        var organizations = new List<string?>();
        foreach (var rdn in name.EnumerateRelativeDistinguishedNames())
        {
            if (rdn.HasMultipleElements)
            {
                return false;
            }

            if (rdn.GetSingleElementType().Value == OrganizationOid)
            {
                organizations.Add(rdn.GetSingleElementValue());
            }
        }

        return organizations is [Microsoft];
    }
}

/// <summary>Resultado do instalador oficial: códigos do Windows Installer/Burn mapeados para os da CLI (R16).</summary>
public static class SetupExitCodes
{
    public static (bool Succeeded, bool RebootRequired, string? Error) Interpret(int code) => code switch
    {
        0 => (true, false, null),
        3010 or 1641 => (true, true, null),
        1618 => (false, false, "outra instalação do Windows Installer está em andamento"),
        1602 or 1223 => (false, false, "a instalação foi cancelada"),
        _ => (false, false, $"o instalador terminou com código {code}"),
    };
}

/// <summary>
/// Executa o instalador oficial <c>.exe</c> no escopo global (R01 a R04, R16): download na allowlist com SHA-512,
/// staging exclusivo protegido, nova conferência do hash e da assinatura Authenticode pelo handle aberto sem
/// compartilhar escrita nem exclusão, e execução com <c>/quiet /norestart</c> e <c>TEMP</c> no staging.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSetupInstaller : IArtifactInstaller
{
    private static readonly TimeSpan SetupTimeout = TimeSpan.FromMinutes(30);

    private readonly TrustedHttpFetcher fetcher;
    private readonly InstallerGlobalLayout layout;
    private readonly InstallationManifestStore manifest;
    private readonly IDiagnosticSink diagnostics;
    private readonly IProgress<DownloadProgress>? progress;

    internal WindowsSetupInstaller(
        TrustedHttpFetcher fetcher,
        InstallerGlobalLayout layout,
        InstallationManifestStore manifest,
        IDiagnosticSink diagnostics,
        IProgress<DownloadProgress>? progress)
    {
        this.fetcher = fetcher;
        this.layout = layout;
        this.manifest = manifest;
        this.diagnostics = diagnostics;
        this.progress = progress;
    }

    public static WindowsSetupInstaller CreateOfficial(InstallerGlobalLayout layout, IDiagnosticSink diagnostics, IProgress<DownloadProgress>? progress)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return new(TrustedHttpFetcher.CreateOfficial(diagnostics), layout, new InstallationManifestStore(layout.ManifestFile), diagnostics, progress);
    }

    public async Task<InstallResult> InstallAsync(ResolvedArtifact artifact, string root, InstallScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        if (InstallLocations.IsInstalled(root, artifact.Component, artifact.Version))
        {
            var existing = await DotnetHostValidator.ValidateAsync(root, artifact, cancellationToken).ConfigureAwait(false);
            return new InstallResult(InstallStatus.AlreadyInstalled, artifact, root, [], [], false, existing);
        }

        await RunSetupAsync(artifact.File.Url, artifact.Sha512, artifact.File.Name, "/install", cancellationToken).ConfigureAwait(false);
        await manifest.AddAsync(
            new InstallationRecord(
                artifact.Component,
                artifact.Version.ToString(),
                artifact.Rid,
                scope,
                root,
                artifact.File.Name,
                artifact.File.Url,
                artifact.Sha512,
                DateTimeOffset.UtcNow,
                [],
                [],
                false),
            diagnostics,
            CancellationToken.None).ConfigureAwait(false);
        var validation = await DotnetHostValidator.ValidateAsync(root, artifact, CancellationToken.None).ConfigureAwait(false);
        return new InstallResult(InstallStatus.Installed, artifact, root, [], [], false, validation);
    }

    /// <summary>Remove a instalação registrada executando o mesmo instalador verificado com <c>/uninstall</c> (D-F7-4).</summary>
    public async Task UninstallAsync(InstallationRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        await RunSetupAsync(record.Url, record.Sha512, record.FileName, "/uninstall", cancellationToken).ConfigureAwait(false);
        await manifest.UpdateAsync(
            records => records.Where(r => !(r.Component == record.Component && r.Version == record.Version && InstallLocations.SameRoot(r.Root, record.Root))).ToList(),
            diagnostics,
            CancellationToken.None).ConfigureAwait(false);
    }

    public void Dispose() => fetcher.Dispose();

    private async Task RunSetupAsync(Uri url, string sha512, string fileName, string action, CancellationToken cancellationToken)
    {
        WindowsSecurity.EnsureProtectedDirectory(layout.StateDirectory);
        WindowsSecurity.EnsureProtectedDirectory(layout.StagingDirectory);
        foreach (var stale in Directory.EnumerateDirectories(layout.StagingDirectory))
        {
            TryDeleteStaging(stale, attempts: 1);
        }

        var staging = Path.Combine(layout.StagingDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var installer = Path.Combine(staging, Path.GetFileName(fileName));
            diagnostics.Verbose($"baixando {url} para {installer}");
            await fetcher.DownloadToFileAsync(url, installer, sha512, progress, cancellationToken).ConfigureAwait(false);

            // O arquivo fica aberto sem compartilhar escrita nem exclusão até o fim da execução (R04).
            using var handle = File.OpenHandle(installer, FileMode.Open, FileAccess.Read, FileShare.Read);
            VerifySha512(handle, sha512, fileName);
            Authenticode.VerifyMicrosoftSignature(handle, installer);
            diagnostics.Verbose($"SHA-512 e Authenticode verificados: {fileName}");

            var startInfo = new System.Diagnostics.ProcessStartInfo(installer)
            {
                UseShellExecute = false,
                WorkingDirectory = staging,
            };
            startInfo.ArgumentList.Add(action);
            startInfo.ArgumentList.Add("/quiet");
            startInfo.ArgumentList.Add("/norestart");
            startInfo.Environment["TEMP"] = staging;
            startInfo.Environment["TMP"] = staging;
            using var process = System.Diagnostics.Process.Start(startInfo)
                ?? throw new DotnetScopeException(ExitCodes.UnexpectedError, $"Não foi possível executar {fileName}.");

            // Interromper um instalador elevado no meio deixaria a máquina fora do controle do dotnetscope: o Ctrl+C
            // só é informado, e a CLI espera o resultado real; apenas o prazo encerra a árvore de processos.
            using var notice = cancellationToken.Register(
                () => diagnostics.Warning($"aguardando {fileName} terminar; interrompê-lo deixaria a instalação inconsistente."));
            using var limit = new CancellationTokenSource(SetupTimeout);
            try
            {
                await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                throw new DotnetScopeException(
                    ExitCodes.UnexpectedError, $"{fileName} excedeu {SetupTimeout.TotalMinutes:0} min e foi encerrado; verifique Aplicativos instalados do Windows.");
            }

            var (succeeded, reboot, error) = SetupExitCodes.Interpret(process.ExitCode);
            if (!succeeded)
            {
                throw new DotnetScopeException(process.ExitCode == 1618 ? ExitCodes.Conflict : ExitCodes.UnexpectedError, $"{fileName}: {error}.");
            }

            if (reboot)
            {
                diagnostics.Warning($"{fileName} pediu reinicialização para concluir.");
            }
        }
        finally
        {
            // Processos filhos do instalador (Burn) podem manter arquivos do TEMP abertos por alguns segundos.
            if (!TryDeleteStaging(staging, attempts: 20))
            {
                diagnostics.Warning($"não foi possível remover o staging {staging}; ele será removido na próxima operação.");
            }
        }
    }

    private static bool TryDeleteStaging(string path, int attempts)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }

                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (attempt >= attempts)
                {
                    return false;
                }

                Thread.Sleep(500);
            }
        }
    }

    private static void VerifySha512(SafeFileHandle handle, string expected, string fileName)
    {
        // RandomAccess lê pelo handle sem tomar posse dele: o handle continua aberto para o Authenticode e a execução.
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
        var buffer = new byte[1024 * 1024];
        long offset = 0;
        int read;
        while ((read = RandomAccess.Read(handle, buffer, offset)) > 0)
        {
            hash.AppendData(buffer, 0, read);
            offset += read;
        }

        var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new DotnetScopeException(ExitCodes.Integrity, $"SHA-512 de {fileName} mudou após o download; nada foi executado.");
        }
    }
}
