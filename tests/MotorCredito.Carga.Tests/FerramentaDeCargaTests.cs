using System.Globalization;
using MotorCredito.Carga;
using Xunit;

namespace MotorCredito.Carga.Tests;

public sealed class LeitorCsvTests
{
    private static List<string[]> Ler(string texto) => [.. LeitorCsv.Ler(new StringReader(texto))];

    [Fact]
    public void Separa_por_ponto_e_virgula_e_aceita_ultima_linha_sem_quebra()
    {
        var linhas = Ler("a;b;c\r\n1;2;3");

        Assert.Equal(2, linhas.Count);
        Assert.Equal(["a", "b", "c"], linhas[0]);
        Assert.Equal(["1", "2", "3"], linhas[1]);
    }

    [Fact]
    public void Campo_entre_aspas_pode_ter_separador_aspas_duplas_e_quebra_de_linha()
    {
        var linhas = Ler("\"a;b\";\"disse \"\"oi\"\"\";\"linha1\nlinha2\"\n");

        var linha = Assert.Single(linhas);
        Assert.Equal(["a;b", "disse \"oi\"", "linha1\nlinha2"], linha);
    }

    [Fact]
    public void Campos_vazios_e_linhas_em_branco()
    {
        var linhas = Ler("a;;c\n\n;;\n");

        Assert.Equal(2, linhas.Count);
        Assert.Equal(["a", "", "c"], linhas[0]);
        Assert.Equal(["", "", ""], linhas[1]);
    }
}

public sealed class ConversoesDaCguTests
{
    private static object? Converter(string coluna, string bruto) =>
        LayoutsCgu.Converter(LayoutsCgu.Sancoes.Colunas.Concat(LayoutsCgu.Peps.Colunas).First(c => c.Nome == coluna), bruto);

    [Fact]
    public void Data_em_formato_brasileiro_e_valores_vazios_ou_nao_informados()
    {
        Assert.Equal(new DateOnly(2026, 3, 5), Converter("data_inicio", "05/03/2026"));
        Assert.Equal(new DateOnly(2026, 3, 5), Converter("data_inicio", "5/3/2026"));
        Assert.Null(Converter("data_inicio", ""));
        Assert.Null(Converter("data_fim", "Não informada"));
        Assert.Null(Converter("data_fim", "Sem informação"));
        Assert.Throws<FormatException>(() => Converter("data_inicio", "2026-03-05"));
    }

    [Fact]
    public void Valor_da_multa_aceita_milhar_com_ponto_e_decimal_com_virgula()
    {
        Assert.Equal(813870.96m, Converter("valor_multa", "813.870,96"));
        Assert.Equal(813870.96m, Converter("valor_multa", "813870,96"));
        Assert.Null(Converter("valor_multa", ""));
    }

    [Fact]
    public void Documento_fica_so_com_letras_e_digitos_em_maiusculas()
    {
        Assert.Equal("11222333000181", Converter("documento", "11.222.333/0001-81"));
        Assert.Equal("12ABC34501DE35", Converter("documento", "12.abc.345/01de-35"));
        Assert.Equal("52998224725", Converter("documento", " 529.982.247-25 "));
    }

    [Fact]
    public void Miolo_do_cpf_sao_os_seis_digitos_visiveis()
    {
        Assert.Equal("123456", Converter("cpf_miolo", "***.123.456-**"));
        Assert.Equal("***.123.456-**", Converter("cpf_mascarado", "***.123.456-**"));
    }

    [Fact]
    public void Texto_acima_do_tamanho_da_coluna_e_recusado_e_vazio_vira_nulo()
    {
        Assert.Throws<FormatException>(() => Converter("tipo_pessoa", "FJ"));
        Assert.Null(Converter("nome_sancionado", "   "));
        Assert.Equal("Nome", Converter("nome_sancionado", "  Nome "));
        Assert.Equal(12345L, Converter("codigo_sancao", "12345"));
        Assert.Throws<FormatException>(() => Converter("codigo_sancao", "12a"));
    }
}

public sealed class ArquivoCguTests
{
    private static readonly string CabecalhoCeis = string.Join(';', LayoutsCgu.Sancoes.Colunas
        .Where(c => c.ColunaCsv is not null && c.Nome != "valor_multa").Select(c => $"\"{c.ColunaCsv}\""));

    private static string LinhaCeis(string codigo, string dataInicio = "01/01/2025", string documento = "11.222.333/0001-81")
    {
        var valores = LayoutsCgu.Sancoes.Colunas.Where(c => c.Nome != "valor_multa").Select(c => c.Nome switch
        {
            "cadastro" => "CEIS",
            "codigo_sancao" => codigo,
            "documento" => documento,
            "data_inicio" => dataInicio,
            var n when n.StartsWith("data_", StringComparison.Ordinal) => string.Empty,
            _ => "x",
        });
        return string.Join(';', valores.Select(v => $"\"{v}\""));
    }

    private static IReadOnlyList<object?[]> Ler(string csv) => ArquivoCgu.Ler(new StringReader(csv), LayoutsCgu.Sancoes, "teste.csv");

    [Fact]
    public void Le_o_layout_do_ceis_sem_a_coluna_valor_da_multa()
    {
        var linhas = Ler($"{CabecalhoCeis}\n{LinhaCeis("1")}\n{LinhaCeis("2")}\n");

        Assert.Equal(2, linhas.Count);
        var colunas = LayoutsCgu.Sancoes.Colunas.Select(c => c.Nome).ToList();
        Assert.Equal(1L, linhas[0][colunas.IndexOf("codigo_sancao")]);
        Assert.Equal("11222333000181", linhas[0][colunas.IndexOf("documento")]);
        Assert.Null(linhas[0][colunas.IndexOf("valor_multa")]);
        Assert.Equal(new DateOnly(2025, 1, 1), linhas[0][colunas.IndexOf("data_inicio")]);
    }

    [Fact]
    public void Coluna_ausente_no_cabecalho_rejeita_o_arquivo_inteiro()
    {
        var erro = Assert.Throws<ArquivoCguInvalidoException>(() => Ler($"\"CADASTRO\";\"OUTRA\"\n\"CEIS\";\"x\"\n"));

        Assert.Contains("colunas ausentes", erro.Problemas[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Linha_com_problema_rejeita_tudo_e_informa_a_linha_e_a_coluna()
    {
        var erro = Assert.Throws<ArquivoCguInvalidoException>(
            () => Ler($"{CabecalhoCeis}\n{LinhaCeis("1")}\n{LinhaCeis("2", dataInicio: "32/13/2025")}\n"));

        var problema = Assert.Single(erro.Problemas);
        Assert.StartsWith("linha 3, coluna data_inicio", problema, StringComparison.Ordinal);
    }

    [Fact]
    public void Arquivo_vazio_ou_so_com_cabecalho_e_rejeitado()
    {
        Assert.Throws<ArquivoCguInvalidoException>(() => Ler(string.Empty));
        Assert.Throws<ArquivoCguInvalidoException>(() => Ler($"{CabecalhoCeis}\n"));
    }

    [Fact]
    public void Le_o_layout_de_peps_com_o_cpf_duplicado_no_cabecalho()
    {
        var cabecalho = string.Join(';', LayoutsCgu.Peps.Colunas.Select(c => c.ColunaCsv).Distinct().Select(c => $"\"{c}\""));
        var valores = LayoutsCgu.Peps.Colunas.Select(c => c.ColunaCsv).Distinct().Select(c => c switch
        {
            "CPF" => "***.982.247-**",
            "Nome_PEP" => "MARIA DA SILVA",
            var n when n!.StartsWith("Data_", StringComparison.Ordinal) => "01/02/2024",
            _ => "x",
        });
        var linhas = ArquivoCgu.Ler(new StringReader($"{cabecalho}\n{string.Join(';', valores.Select(v => $"\"{v}\""))}\n"), LayoutsCgu.Peps, "pep.csv");

        var colunas = LayoutsCgu.Peps.Colunas.Select(c => c.Nome).ToList();
        Assert.Equal("***.982.247-**", linhas[0][colunas.IndexOf("cpf_mascarado")]);
        Assert.Equal("982247", linhas[0][colunas.IndexOf("cpf_miolo")]);
        Assert.Equal(new DateOnly(2024, 2, 1), linhas[0][colunas.IndexOf("data_inicio_exercicio")]);
    }
}

public sealed class MigradorTests
{
    [Fact]
    public void Separa_os_comandos_pelas_linhas_com_barra_e_descarta_comentarios()
    {
        const string Script = """
            -- comentário do script
            CREATE TABLE a (x NUMBER)
            /
            -- outro comentário
            CREATE OR REPLACE PROCEDURE p AS
            BEGIN
                -- comentário dentro do bloco
                NULL;
            END;
            /
            COMMENT ON TABLE a IS 'texto com / no meio'
            /
            """;

        var comandos = Migrador.SepararComandos(Script);

        Assert.Equal(3, comandos.Count);
        Assert.StartsWith("CREATE TABLE a", comandos[0], StringComparison.Ordinal);
        Assert.Contains("END;", comandos[1], StringComparison.Ordinal);
        Assert.DoesNotContain("--", comandos[1], StringComparison.Ordinal);
        Assert.Contains("'texto com / no meio'", comandos[2], StringComparison.Ordinal);
    }
}
