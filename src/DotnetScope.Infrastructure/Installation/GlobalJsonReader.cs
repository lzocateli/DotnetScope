using System.Text.Json;
using DotnetScope.Core;
using DotnetScope.Core.Resolution;
using DotnetScope.Core.Versions;

namespace DotnetScope.Infrastructure.Installation;

/// <summary>Localiza e interpreta a seção <c>sdk</c> de um global.json (comentários e vírgulas finais aceitos).</summary>
public static class GlobalJsonReader
{
    /// <summary>
    /// Usa o caminho informado (arquivo ou diretório) ou procura global.json a partir do diretório atual em direção à raiz,
    /// como o host do .NET.
    /// </summary>
    public static GlobalJsonSdk Read(string? path, string currentDirectory)
    {
        var file = Locate(path, currentDirectory);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(
                File.ReadAllBytes(file),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException exception)
        {
            throw new DotnetScopeException(ExitCodes.Usage, $"{file} não é um JSON válido: {exception.Message}", exception);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("sdk", out var sdk)
                || sdk.ValueKind != JsonValueKind.Object)
            {
                throw new DotnetScopeException(ExitCodes.Usage, $"{file} não tem a seção \"sdk\".");
            }

            DotnetVersion? version = null;
            if (sdk.TryGetProperty("version", out var versionElement))
            {
                if (versionElement.ValueKind != JsonValueKind.String || !DotnetVersion.TryParse(versionElement.GetString(), out version))
                {
                    throw new DotnetScopeException(ExitCodes.Usage, $"sdk.version inválido em {file}: {versionElement}.");
                }
            }

            // Sem sdk.version, o host usa latestMajor; com versão, latestPatch.
            var policy = version is null ? RollForwardPolicy.LatestMajor : RollForwardPolicy.LatestPatch;
            if (sdk.TryGetProperty("rollForward", out var rollForward)
                && !RollForwardPolicies.TryParse(rollForward.ValueKind == JsonValueKind.String ? rollForward.GetString() : null, out policy))
            {
                throw new DotnetScopeException(ExitCodes.Usage, $"sdk.rollForward inválido em {file}: {rollForward}.");
            }

            var allowPrerelease = sdk.TryGetProperty("allowPrerelease", out var prerelease) && prerelease.ValueKind == JsonValueKind.True;
            return new GlobalJsonSdk(file, version, policy, allowPrerelease);
        }
    }

    private static string Locate(string? path, string currentDirectory)
    {
        if (path is not null)
        {
            var full = Path.GetFullPath(path, currentDirectory);
            if (Directory.Exists(full))
            {
                full = Path.Combine(full, "global.json");
            }

            return File.Exists(full)
                ? full
                : throw new DotnetScopeException(ExitCodes.NotFound, $"global.json não encontrado: {full}.");
        }

        for (var directory = new DirectoryInfo(currentDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "global.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DotnetScopeException(ExitCodes.NotFound, $"Nenhum global.json encontrado a partir de {currentDirectory}.");
    }
}
