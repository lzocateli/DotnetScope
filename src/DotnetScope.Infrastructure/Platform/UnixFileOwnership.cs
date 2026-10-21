using System.Diagnostics;
using System.Globalization;

namespace DotnetScope.Infrastructure.Platform;

/// <summary>Dono (UID) e bits de permissão de um caminho, seguindo links simbólicos.</summary>
public sealed record UnixFileStatus(int Uid, int Mode);

/// <summary>
/// Lê dono e permissões com o <c>stat</c> do sistema (coreutils ou BusyBox no Linux, BSD no macOS). O .NET não expõe o UID do dono,
/// e um P/Invoke de <c>stat</c> exigiria layouts de struct distintos por libc e arquitetura.
/// </summary>
public static class UnixFileOwnership
{
    private static readonly string[] Candidates = ["/usr/bin/stat", "/bin/stat"];

    /// <summary>Retorna nulo se o caminho não existe; lança se não houver <c>stat</c> utilizável.</summary>
    public static UnixFileStatus? Stat(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!Path.Exists(path))
        {
            return null;
        }

        var tool = Candidates.FirstOrDefault(File.Exists)
            ?? throw new Core.DotnetScopeException(Core.ExitCodes.MissingDependency, "O utilitário stat não foi encontrado em /usr/bin nem em /bin.");
        var startInfo = new ProcessStartInfo(tool)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.Environment.Clear();
        startInfo.Environment["LC_ALL"] = "C";
        startInfo.ArgumentList.Add("-L");
        if (OperatingSystem.IsMacOS())
        {
            // stat do BSD: %Lp são os bits de permissão em octal.
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add("%u %Lp");
        }
        else
        {
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add("%u %a");
        }

        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(path);

        using var process = Process.Start(startInfo)
            ?? throw new Core.DotnetScopeException(Core.ExitCodes.UnexpectedError, $"Não foi possível executar {tool}.");
        var output = process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        if (!process.WaitForExit(TimeSpan.FromSeconds(10)) || process.ExitCode != 0)
        {
            return Path.Exists(path)
                ? throw new Core.DotnetScopeException(Core.ExitCodes.UnexpectedError, $"Não foi possível ler dono e permissões de {path}.")
                : null;
        }

        return Parse(output) ?? throw new Core.DotnetScopeException(Core.ExitCodes.UnexpectedError, $"Saída inesperada de stat para {path}.");
    }

    internal static UnixFileStatus? Parse(string output)
    {
        var parts = output.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var uid)
            && TryParseOctal(parts[1], out var mode)
                ? new UnixFileStatus(uid, mode)
                : null;
    }

    private static bool TryParseOctal(string value, out int mode)
    {
        mode = 0;
        if (value.Length is 0 or > 5)
        {
            return false;
        }

        foreach (var digit in value)
        {
            if (digit is < '0' or > '7')
            {
                return false;
            }

            mode = (mode * 8) + (digit - '0');
        }

        return true;
    }
}
