namespace DotnetScope.Core;

/// <summary>Destino de avisos (sempre exibidos) e diagnósticos detalhados (somente com --verbose).</summary>
public interface IDiagnosticSink
{
    void Warning(string message);

    void Verbose(string message);
}

public sealed class NullDiagnosticSink : IDiagnosticSink
{
    public static NullDiagnosticSink Instance { get; } = new();

    public void Warning(string message)
    {
    }

    public void Verbose(string message)
    {
    }
}
