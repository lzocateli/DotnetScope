using System.Runtime.Versioning;
using DotnetScope.Core;
using DotnetScope.Core.UserEnvironment;
using DotnetScope.Infrastructure.Installation;
using DotnetScope.Infrastructure.UserEnvironment;
using Microsoft.Win32;

namespace DotnetScope.IntegrationTests;

/// <summary>
/// Registro e caminhos reais do Windows. O registro usa uma subchave isolada em <c>HKCU\Software</c>, nunca
/// <c>HKCU\Environment</c>, e o aviso <c>WM_SETTINGCHANGE</c> fica desligado.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsRegistryTests : IDisposable
{
    private readonly string _subKey = $@"Software\DotnetScope.Tests\{Guid.NewGuid():N}";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "dotnetscope-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (OperatingSystem.IsWindows())
        {
            Registry.CurrentUser.DeleteSubKeyTree(_subKey, throwOnMissingSubKey: false);
        }

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Store_PreservaValorBrutoETipo()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Somente Windows.");
        var store = new RegistryEnvironmentStore(_subKey, broadcast: false);

        store.SetUser("Path", @"%USERPROFILE%\bin;C:\x", expandable: true);
        store.SetUser("DOTNET_ROOT", @"C:\dotnet", expandable: false);

        Assert.Equal(new EnvironmentVariableValue(@"%USERPROFILE%\bin;C:\x", Expandable: true), store.GetUser("Path"));
        Assert.Equal(new EnvironmentVariableValue(@"C:\dotnet", Expandable: false), store.GetUser("DOTNET_ROOT"));
        store.DeleteUser("DOTNET_ROOT");
        Assert.Null(store.GetUser("DOTNET_ROOT"));
        Assert.False(string.IsNullOrEmpty(store.GetMachine("Path")));
    }

    [Fact]
    public void Store_TipoInesperado_EhConflito()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Somente Windows.");
        using (var key = Registry.CurrentUser.CreateSubKey(_subKey))
        {
            key.SetValue("Path", 1, RegistryValueKind.DWord);
        }

        var exception = Assert.Throws<DotnetScopeException>(() => new RegistryEnvironmentStore(_subKey, broadcast: false).GetUser("Path"));

        Assert.Equal(ExitCodes.Conflict, exception.ExitCode);
    }

    [Fact]
    public async Task Configurador_AplicaERemoveRestaurandoValorETipo()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Somente Windows.");
        var store = new RegistryEnvironmentStore(_subKey, broadcast: false);
        store.SetUser("Path", @"%SystemRoot%\x;;C:\y;", expandable: true);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var root = Path.Combine(_directory, "dotnet");
        var configurator = new WindowsEnvironmentConfigurator(store, Path.Combine(_directory, "state"), home);
        var target = new EnvironmentTarget(root, Path.Combine(home, ".dotnet", "tools"), TelemetryOptOut: false);
        var diagnostics = new NullDiagnostics();

        await configurator.ExecuteAsync(configurator.PlanApply(target, EnvConflictPolicy.Fail), diagnostics, TestContext.Current.CancellationToken);
        Assert.StartsWith(root + ";", store.GetUser("Path")!.Value, StringComparison.Ordinal);
        Assert.Equal(root, store.GetUser("DOTNET_ROOT")!.Value);
        Assert.False(configurator.PlanApply(target, EnvConflictPolicy.Fail).HasChanges);

        await configurator.ExecuteAsync(configurator.PlanRemove(), diagnostics, TestContext.Current.CancellationToken);

        Assert.Equal(new EnvironmentVariableValue(@"%SystemRoot%\x;;C:\y;", Expandable: true), store.GetUser("Path"));
        Assert.Null(store.GetUser("DOTNET_ROOT"));
    }

    [Fact]
    public void ValidateUserRoot_RecusaDiretoriosDoSistemaEOutrosPerfis()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Somente Windows.");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        Assert.Equal(Path.Combine(home, "dotnet"), InstallLocations.ValidateUserRoot(Path.Combine(home, "dotnet") + @"\", home));
        foreach (var denied in new[] { Path.Combine(windows, "dotnet"), Path.Combine(programFiles, "dotnet"), Path.GetPathRoot(home)!, home })
        {
            Assert.Equal(ExitCodes.Privilege, Assert.Throws<DotnetScopeException>(() => InstallLocations.ValidateUserRoot(denied, home)).ExitCode);
        }

        Assert.Equal(ExitCodes.Privilege, Assert.Throws<DotnetScopeException>(() => InstallLocations.ValidateUserRoot(@"\\servidor\share\dotnet", home)).ExitCode);
    }

    [Fact]
    public void ValidateUserRoot_LinkParaDiretorioDoSistema_EhRecusado()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Somente Windows.");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Directory.CreateDirectory(_directory);
        var link = Path.Combine(_directory, "link");
        try
        {
            Directory.CreateSymbolicLink(link, Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        }
        catch (IOException exception)
        {
            Assert.Skip($"Sem privilégio para criar links simbólicos: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            Assert.Skip($"Sem privilégio para criar links simbólicos: {exception.Message}");
        }

        var result = Assert.Throws<DotnetScopeException>(() => InstallLocations.ValidateUserRoot(Path.Combine(link, "dotnet"), home));

        Assert.Equal(ExitCodes.Privilege, result.ExitCode);
    }

    private sealed class NullDiagnostics : IDiagnosticSink
    {
        public void Warning(string message)
        {
        }

        public void Verbose(string message)
        {
        }
    }
}
