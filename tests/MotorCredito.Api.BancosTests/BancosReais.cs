using System.Globalization;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using MotorCredito.Api.Bancos;
using MotorCredito.Api.Configuration;
using MotorCredito.Api.Movimentacao;
using MotorCredito.Api.Triagem;
using MotorCredito.Carga;
using Xunit;

namespace MotorCredito.Api.BancosTests;

/// <summary>
/// Testes contra os bancos REAIS (PostgreSQL e Oracle locais, schemas _2 já migrados e carregados: ver README).
/// Filtro: <c>dotnet test --filter Categoria=Bancos</c>. Cada teste apaga o que gravou; o contador da movimentação
/// é zerado no fim se a movimentação ficou vazia (os testes consomem números).
/// </summary>
[CollectionDefinition("Bancos", DisableParallelization = true)]
public sealed class ColecaoDosBancos : ICollectionFixture<AmbienteDosBancos>;

public sealed class AmbienteDosBancos : IAsyncLifetime
{
    public const string RotaDeTeste = "/teste-bancos";

    public AmbienteDosBancos()
    {
        (Bancos, Admin) = Ambiente.Carregar();
    }

    public BancosSettings Bancos { get; }

    public Admins Admin { get; }

    public static string[] Provedores => ["postgres", "oracle"];

    public IRepositorioDeListas Listas(string banco) => banco == "oracle"
        ? new ListasOracle(ConexaoAppOracle())
        : new ListasPostgres(FonteApp(somenteLeitura: true), Bancos.Postgres.Esquema);

    public IRepositorioDeMovimentacao Movimentacao(string banco) => banco == "oracle"
        ? new MovimentacaoOracle(ConexaoAppOracle())
        : new MovimentacaoPostgres(FonteApp(somenteLeitura: true), Bancos.Postgres.Esquema);

    public string ConexaoAppOracle() => ConexoesOracle.Montar(Bancos.Oracle.DataSource, Bancos.Oracle.Usuario, Bancos.Oracle.Senha);

    public NpgsqlDataSource FonteApp(bool somenteLeitura) => NpgsqlDataSource.Create(ConexoesPostgres.Montar(
        Bancos.Postgres.Host, Bancos.Postgres.Porta, Bancos.Postgres.Banco, Bancos.Postgres.Usuario, Bancos.Postgres.Senha,
        somenteLeitura));

    private NpgsqlDataSource FonteAdmin() => NpgsqlDataSource.Create(ConexoesPostgres.Montar(
        Bancos.Postgres.Host, Bancos.Postgres.Porta, Bancos.Postgres.Banco, Admin.PostgresUsuario, Admin.PostgresSenha, somenteLeitura: false));

    private string ConexaoAdminOracle() => ConexoesOracle.Montar(Bancos.Oracle.DataSource, Admin.OracleUsuario, Admin.OracleSenha);

    /// <summary>Executa SQL como administrador (para conferir e limpar). Os nomes de schema vêm da configuração.</summary>
    public async Task<long> AdminEscalarAsync(string banco, string sqlPg, string sqlOra)
    {
        if (banco == "oracle")
        {
            using var con = new OracleConnection(ConexaoAdminOracle());
            await con.OpenAsync();
            using var cmd = new OracleCommand(sqlOra.Replace("{s}", Esquemas.OracleDono, StringComparison.Ordinal), con);
            return Convert.ToInt64(await cmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        }

        await using var fonte = FonteAdmin();
        await using var comando = fonte.CreateCommand(sqlPg.Replace("{s}", Bancos.Postgres.Esquema, StringComparison.Ordinal));
        return Convert.ToInt64(await comando.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    public async Task AdminExecutarAsync(string banco, string sql)
    {
        if (banco == "oracle")
        {
            using var con = new OracleConnection(ConexaoAdminOracle());
            await con.OpenAsync();
            using var cmd = new OracleCommand(sql.Replace("{s}", Esquemas.OracleDono, StringComparison.Ordinal), con);
            await cmd.ExecuteNonQueryAsync();
            return;
        }

        await using var fonte = FonteAdmin();
        await using var comando = fonte.CreateCommand(sql.Replace("{s}", Bancos.Postgres.Esquema, StringComparison.Ordinal));
        await comando.ExecuteNonQueryAsync();
    }

    /// <summary>Atalho: o mesmo SQL nos dois bancos.</summary>
    public Task<long> AdminEscalarAsync(string banco, string sql) => AdminEscalarAsync(banco, sql, sql);

    /// <summary>Apaga o que os testes gravaram (erros antes da movimentação, por causa da chave estrangeira).</summary>
    public async Task LimparAsync(string banco)
    {
        var testes = $"rota like '{RotaDeTeste}%' or rota = '/api/v1/triagem'";
        await AdminExecutarAsync(banco, $"delete from {{s}}.log_erros where requisicao_id in (select requisicao_id from {{s}}.movimentacao where {testes})");
        await AdminExecutarAsync(banco, $"delete from {{s}}.movimentacao where {testes}");
    }

    public async Task InitializeAsync()
    {
        foreach (var banco in Provedores)
        {
            await LimparAsync(banco);
        }
    }

    public async Task DisposeAsync()
    {
        foreach (var banco in Provedores)
        {
            await LimparAsync(banco);
        }
    }

    /// <summary>Um documento sancionado e um miolo de PEP que existem de verdade nos dados carregados.</summary>
    public async Task<(string Documento, string Miolo, string NomeDoPep)> AmostrasAsync(string banco)
    {
        var sancoes = await Consultas.LerAsync(banco, Bancos, Admin, "sancoes", ["documento"], CancellationToken.None);
        var peps = await Consultas.LerAsync(banco, Bancos, Admin, "peps", ["cpf_miolo", "nome"], CancellationToken.None);
        return (sancoes[0][0]!, peps[0][0]!, peps[0][1]!);
    }
}
