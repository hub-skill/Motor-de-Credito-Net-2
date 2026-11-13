using System.Globalization;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using MotorCredito.Api.Bancos;
using MotorCredito.Api.Configuration;

namespace MotorCredito.Carga;

/// <summary>Leitura genérica das tabelas (como administrador) para a comparação e para escolher casos de teste.</summary>
public static class Consultas
{
    /// <summary>Devolve as linhas com cada valor normalizado em texto (nulo = null), para comparar bancos diferentes.</summary>
    public static async Task<IReadOnlyList<string?[]>> LerAsync(
        string banco, BancosSettings bancos, Admins admin, string tabela, IReadOnlyList<string> colunas, CancellationToken ct)
    {
        var lista = string.Join(", ", colunas);
        if (banco == "oracle")
        {
            var o = bancos.Oracle;
            using var con = new OracleConnection(ConexoesOracle.Montar(o.DataSource, admin.OracleUsuario, admin.OracleSenha));
            await con.OpenAsync(ct);
#pragma warning disable CA2100
            using var comando = new OracleCommand($"select {lista} from {Esquemas.OracleDono}.{tabela}", con) { InitialLOBFetchSize = -1, CommandTimeout = 120 };
#pragma warning restore CA2100
            using var leitor = await comando.ExecuteReaderAsync(ct);
            var linhas = new List<string?[]>();
            while (await leitor.ReadAsync(ct))
            {
                linhas.Add(Normalizar(leitor.FieldCount, i => leitor.IsDBNull(i) ? null : leitor.GetValue(i)));
            }

            return linhas;
        }

        var pg = bancos.Postgres;
        await using var conexao = new NpgsqlConnection(ConexoesPostgres.Montar(
            pg.Host, pg.Porta, pg.Banco, admin.PostgresUsuario, admin.PostgresSenha, somenteLeitura: false, 120,
            "motor-credito-net-2-consulta", Enum.Parse<SslMode>(pg.SslMode, true)));
        await conexao.OpenAsync(ct);
#pragma warning disable CA2100
        await using var cmd = new NpgsqlCommand($"select {lista} from {pg.Esquema}.{tabela}", conexao);
#pragma warning restore CA2100
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        var resultado = new List<string?[]>();
        while (await rd.ReadAsync(ct))
        {
            resultado.Add(Normalizar(rd.FieldCount, i => rd.IsDBNull(i) ? null : rd.GetValue(i)));
        }

        return resultado;
    }

    private static string?[] Normalizar(int quantidade, Func<int, object?> valor)
    {
        var linha = new string?[quantidade];
        for (var i = 0; i < quantidade; i++)
        {
            linha[i] = valor(i) switch
            {
                null => null,
                DateTime d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                decimal m => m.ToString("0.####", CultureInfo.InvariantCulture),
                long or int or short => Convert.ToDecimal(valor(i), CultureInfo.InvariantCulture).ToString("0.####", CultureInfo.InvariantCulture),
                string s => s.TrimEnd().Length == 0 ? null : s.TrimEnd(), // Oracle trata texto vazio como nulo; CHAR vem com preenchimento
                var outro => Convert.ToString(outro, CultureInfo.InvariantCulture),
            };
        }

        return linha;
    }
}

/// <summary>Compara o conteúdo dos dois bancos, linha a linha, pela chave de cada tabela.</summary>
public static class Comparador
{
    public static readonly string[] ColunasSancoes =
    [
        "cadastro", "codigo_sancao", "tipo_pessoa", "documento", "nome_sancionado", "nome_informado_orgao", "razao_social",
        "nome_fantasia", "numero_processo", "categoria", "valor_multa", "data_inicio", "data_fim", "data_publicacao",
        "publicacao", "detalhamento_publicacao", "data_transito_julgado", "abrangencia", "orgao_sancionador", "uf_orgao",
        "esfera_orgao", "fundamentacao_legal", "data_origem", "origem_informacao", "observacoes", "arquivo_origem", "origem_carga",
    ];

    public static readonly string[] ColunasPeps =
    [
        "cpf_mascarado", "cpf_miolo", "nome", "sigla_funcao", "descricao_funcao", "nivel_funcao", "nome_orgao",
        "data_inicio_exercicio", "data_fim_exercicio", "data_fim_carencia", "arquivo_origem", "origem_carga",
    ];

    public static async Task<bool> CompararAsync(BancosSettings bancos, Admins admin, CancellationToken ct)
    {
        var ok = true;
        ok &= await Tabela("sancoes", ColunasSancoes, bancos, admin, ct);
        ok &= await Tabela("peps", ColunasPeps, bancos, admin, ct);
        return ok;
    }

    // Compara as linhas inteiras como multiconjuntos: a tabela peps não tem chave única (homônimos com o mesmo
    // CPF mascarado existem), então uma chave parcial esconderia ou inventaria diferenças.
    private static async Task<bool> Tabela(string tabela, string[] colunas, BancosSettings bancos, Admins admin, CancellationToken ct)
    {
        var pg = Contar(await Consultas.LerAsync("postgres", bancos, admin, tabela, colunas, ct));
        var ora = Contar(await Consultas.LerAsync("oracle", bancos, admin, tabela, colunas, ct));
        var totalPg = pg.Values.Sum();
        var totalOra = ora.Values.Sum();
        var soPg = pg.Sum(p => Math.Max(0, p.Value - ora.GetValueOrDefault(p.Key)));
        var soOra = ora.Sum(p => Math.Max(0, p.Value - pg.GetValueOrDefault(p.Key)));
        var igual = soPg == 0 && soOra == 0;
        Console.WriteLine($"  {tabela}: PostgreSQL {totalPg}, Oracle {totalOra}, linhas só no PostgreSQL {soPg}, só no Oracle {soOra} -> {(igual ? "IGUAIS" : "DIFERENTES")}");
        return igual;
    }

    private static Dictionary<string, int> Contar(IReadOnlyList<string?[]> linhas)
    {
        var mapa = new Dictionary<string, int>(linhas.Count, StringComparer.Ordinal);
        foreach (var l in linhas)
        {
            var chave = string.Join('', l.Select(v => v ?? ""));
            mapa[chave] = mapa.GetValueOrDefault(chave) + 1;
        }

        return mapa;
    }
}
