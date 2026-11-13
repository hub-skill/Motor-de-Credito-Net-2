using System.Globalization;
using System.Text;
using MotorCredito.Api.Configuration;

namespace MotorCredito.Carga;

public enum TipoColuna
{
    Texto,
    TextoLongo,
    Inteiro,
    Numerico,
    Data,
}

/// <summary>Uma coluna gravada: de qual coluna do CSV vem (nulo = valor fixo), tipo, tamanho máximo e conversão própria.</summary>
public sealed record ColunaLayout(
    string Nome,
    TipoColuna Tipo,
    string? ColunaCsv = null,
    int? TamanhoMaximo = null,
    Func<string, string?>? Preparar = null,
    bool OpcionalNoCsv = false);

public sealed record LayoutTabela(string Tabela, IReadOnlyList<ColunaLayout> Colunas);

/// <summary>Layout das tabelas sancoes (CEIS + CNEP) e peps. As colunas arquivo_origem e origem_carga são do programa.</summary>
public static class LayoutsCgu
{
    public static LayoutTabela Sancoes { get; } = new("sancoes",
    [
        new("cadastro", TipoColuna.Texto, "CADASTRO", 10),
        new("codigo_sancao", TipoColuna.Inteiro, "CÓDIGO DA SANÇÃO"),
        new("tipo_pessoa", TipoColuna.Texto, "TIPO DE PESSOA", 1),
        new("documento", TipoColuna.Texto, "CPF OU CNPJ DO SANCIONADO", 20, SoLetrasEDigitos),
        new("nome_sancionado", TipoColuna.Texto, "NOME DO SANCIONADO", 200),
        new("nome_informado_orgao", TipoColuna.Texto, "NOME INFORMADO PELO ÓRGÃO SANCIONADOR", 200),
        new("razao_social", TipoColuna.Texto, "RAZÃO SOCIAL - CADASTRO RECEITA", 200),
        new("nome_fantasia", TipoColuna.Texto, "NOME FANTASIA - CADASTRO RECEITA", 100),
        new("numero_processo", TipoColuna.Texto, "NÚMERO DO PROCESSO", 80),
        new("categoria", TipoColuna.Texto, "CATEGORIA DA SANÇÃO", 120),
        new("valor_multa", TipoColuna.Numerico, "VALOR DA MULTA", OpcionalNoCsv: true), // só existe no CNEP
        new("data_inicio", TipoColuna.Data, "DATA INÍCIO SANÇÃO"),
        new("data_fim", TipoColuna.Data, "DATA FINAL SANÇÃO"),
        new("data_publicacao", TipoColuna.Data, "DATA PUBLICAÇÃO"),
        new("publicacao", TipoColuna.Texto, "PUBLICAÇÃO", 150),
        new("detalhamento_publicacao", TipoColuna.TextoLongo, "DETALHAMENTO DO MEIO DE PUBLICAÇÃO"),
        new("data_transito_julgado", TipoColuna.Data, "DATA DO TRÂNSITO EM JULGADO"),
        new("abrangencia", TipoColuna.Texto, "ABRAGÊNCIA DA SANÇÃO", 100), // o erro de grafia é da CGU
        new("orgao_sancionador", TipoColuna.Texto, "ÓRGÃO SANCIONADOR", 300),
        new("uf_orgao", TipoColuna.Texto, "UF ÓRGÃO SANCIONADOR", 2),
        new("esfera_orgao", TipoColuna.Texto, "ESFERA ÓRGÃO SANCIONADOR", 20),
        new("fundamentacao_legal", TipoColuna.TextoLongo, "FUNDAMENTAÇÃO LEGAL"),
        new("data_origem", TipoColuna.Data, "DATA ORIGEM INFORMAÇÃO"),
        new("origem_informacao", TipoColuna.Texto, "ORIGEM INFORMAÇÕES", 150),
        new("observacoes", TipoColuna.TextoLongo, "OBSERVAÇÕES"),
    ]);

    public static LayoutTabela Peps { get; } = new("peps",
    [
        new("cpf_mascarado", TipoColuna.Texto, "CPF", 20),
        new("cpf_miolo", TipoColuna.Texto, "CPF", 20, SoDigitos),
        new("nome", TipoColuna.Texto, "Nome_PEP", 100),
        new("sigla_funcao", TipoColuna.Texto, "Sigla_Função", 10),
        new("descricao_funcao", TipoColuna.Texto, "Descrição_Função", 100),
        new("nivel_funcao", TipoColuna.Texto, "Nível_Função", 10),
        new("nome_orgao", TipoColuna.Texto, "Nome_Órgão", 100),
        new("data_inicio_exercicio", TipoColuna.Data, "Data_Início_Exercício"),
        new("data_fim_exercicio", TipoColuna.Data, "Data_Fim_Exercício"),
        new("data_fim_carencia", TipoColuna.Data, "Data_Fim_Carência"),
    ]);

    /// <summary>Marca de origem gravada em origem_carga.</summary>
    public const string Marca = Esquemas.MarcaDeOrigem;

    // CNPJ formatado vira só dígitos e letras (o CNPJ alfanumérico existe); maiúsculas.
    private static string? SoLetrasEDigitos(string bruto)
    {
        var sb = new StringBuilder(bruto.Length);
        foreach (var c in bruto)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                sb.Append(char.ToUpperInvariant(c));
            }
        }

        return sb.Length == 0 ? null : sb.ToString();
    }

    // "***.123.456-**" vira "123456": os dígitos que a CGU deixa visíveis.
    private static string? SoDigitos(string bruto)
    {
        var sb = new StringBuilder(6);
        foreach (var c in bruto)
        {
            if (char.IsAsciiDigit(c))
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private static readonly string[] FormatosDeData = ["dd/MM/yyyy", "d/M/yyyy"];

    /// <summary>Converte o texto do CSV no valor gravado. Lança FormatException quando o valor é inválido.</summary>
    public static object? Converter(ColunaLayout coluna, string bruto)
    {
        ArgumentNullException.ThrowIfNull(coluna);
        ArgumentNullException.ThrowIfNull(bruto);
        var limpo = bruto.Trim();
        if (coluna.Preparar is not null)
        {
            limpo = coluna.Preparar(limpo) ?? string.Empty;
        }

        if (limpo.Length == 0)
        {
            return null;
        }

        switch (coluna.Tipo)
        {
            case TipoColuna.Inteiro:
                return long.Parse(limpo, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            case TipoColuna.Numerico:
                // "813.870,96" ou "813870,96" viram 813870.96
                return decimal.Parse(
                    limpo.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.'),
                    NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture);
            case TipoColuna.Data:
                if (limpo.StartsWith("não informad", StringComparison.OrdinalIgnoreCase)
                    || limpo.StartsWith("sem informa", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                return DateOnly.ParseExact(limpo, FormatosDeData, CultureInfo.InvariantCulture, DateTimeStyles.None);
            default:
                if (coluna.TamanhoMaximo is int maximo && limpo.Length > maximo)
                {
                    throw new FormatException($"texto com {limpo.Length} caracteres (máximo {maximo})");
                }

                return limpo;
        }
    }
}
