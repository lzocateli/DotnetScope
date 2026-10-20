namespace DotnetScope.Cli.Output;

/// <summary>Tabela de texto com colunas alinhadas, sem cores.</summary>
internal sealed class TextTable(params string[] headers)
{
    private readonly List<string[]> _rows = [];

    public void Add(params string[] cells)
    {
        if (cells.Length != headers.Length)
        {
            throw new ArgumentException("Quantidade de colunas diferente do cabeçalho.", nameof(cells));
        }

        _rows.Add(cells);
    }

    public async Task WriteAsync(TextWriter writer)
    {
        var widths = headers
            .Select((header, index) => Math.Max(header.Length, _rows.Count == 0 ? 0 : _rows.Max(row => row[index].Length)))
            .ToArray();
        await WriteRowAsync(writer, headers, widths).ConfigureAwait(false);
        foreach (var row in _rows)
        {
            await WriteRowAsync(writer, row, widths).ConfigureAwait(false);
        }
    }

    private static Task WriteRowAsync(TextWriter writer, string[] cells, int[] widths) =>
        writer.WriteLineAsync(string.Join("  ", cells.Select((cell, index) => cell.PadRight(widths[index]))).TrimEnd());
}
