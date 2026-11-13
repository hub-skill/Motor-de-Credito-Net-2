using System.Text;

namespace MotorCredito.Carga;

public sealed class ArquivoCguInvalidoException(string arquivo, IReadOnlyList<string> problemas)
    : Exception($"{arquivo}: {problemas.Count} problema(s); nada será gravado.")
{
    public string Arquivo { get; } = arquivo;

    public IReadOnlyList<string> Problemas { get; } = problemas;
}

/// <summary>Linhas já convertidas de um CSV da CGU: cada linha tem os valores na ordem das colunas do layout.</summary>
public static class ArquivoCgu
{
    private const int MaximoDeProblemasRelatados = 20;

    /// <summary>Os CSV da CGU vêm em ISO-8859-1 (latin-1), não em UTF-8.</summary>
    public static IReadOnlyList<object?[]> Ler(string caminho, LayoutTabela layout)
    {
        using var leitor = new StreamReader(caminho, Encoding.Latin1);
        return Ler(leitor, layout, Path.GetFileName(caminho));
    }

    public static IReadOnlyList<object?[]> Ler(TextReader leitor, LayoutTabela layout, string nomeDoArquivo)
    {
        ArgumentNullException.ThrowIfNull(layout);
        using var registros = LeitorCsv.Ler(leitor).GetEnumerator();
        if (!registros.MoveNext())
        {
            throw new ArquivoCguInvalidoException(nomeDoArquivo, ["arquivo vazio"]);
        }

        var cabecalho = registros.Current.Select(c => c.Trim().TrimStart('﻿')).ToArray();
        var indices = new int[layout.Colunas.Count];
        var ausentes = new List<string>();
        for (var i = 0; i < indices.Length; i++)
        {
            var coluna = layout.Colunas[i];
            indices[i] = Array.LastIndexOf(cabecalho, coluna.ColunaCsv);
            if (indices[i] < 0 && !coluna.OpcionalNoCsv)
            {
                ausentes.Add(coluna.ColunaCsv!);
            }
        }

        if (ausentes.Count > 0)
        {
            throw new ArquivoCguInvalidoException(
                nomeDoArquivo, [$"colunas ausentes no CSV (o layout mudou?): {string.Join(", ", ausentes)}"]);
        }

        var linhas = new List<object?[]>();
        var problemas = new List<string>();
        var numero = 1;
        while (registros.MoveNext())
        {
            numero++;
            var campos = registros.Current;
            var valores = new object?[indices.Length];
            for (var i = 0; i < indices.Length; i++)
            {
                var coluna = layout.Colunas[i];
                try
                {
                    var bruto = indices[i] >= 0 && indices[i] < campos.Length ? campos[indices[i]] : string.Empty;
                    valores[i] = LayoutsCgu.Converter(coluna, bruto);
                }
                catch (Exception e) when (e is FormatException or OverflowException)
                {
                    if (problemas.Count < MaximoDeProblemasRelatados)
                    {
                        problemas.Add($"linha {numero}, coluna {coluna.Nome}: {e.Message}");
                    }
                }
            }

            linhas.Add(valores);
        }

        if (problemas.Count > 0)
        {
            throw new ArquivoCguInvalidoException(nomeDoArquivo, problemas);
        }

        if (linhas.Count == 0)
        {
            throw new ArquivoCguInvalidoException(nomeDoArquivo, ["arquivo sem nenhuma linha de dados"]);
        }

        return linhas;
    }
}
