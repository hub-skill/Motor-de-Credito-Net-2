using Oracle.ManagedDataAccess.Client;

using MotorCredito.Api.Bancos;

namespace MotorCredito.Api.Triagem;

/// <summary>
/// Leitura das listas no Oracle, pelos sinônimos que o script de migração cria no usuário da aplicação (nenhum nome de
/// schema no SQL). SQL fixo, com variáveis de ligação; falhas do banco viram 503/504.
/// </summary>
public sealed class ListasOracle(string conexaoOracle, int timeoutSegundos = 5) : IRepositorioDeListas
{
    private const string SqlSancoes =
        "select cadastro, categoria, orgao_sancionador, uf_orgao, data_inicio, data_fim, numero_processo from sancoes where documento = :documento order by cadastro, codigo_sancao";

    private const string SqlPeps =
        "select nome, descricao_funcao, data_fim_exercicio, data_fim_carencia from peps where cpf_miolo = :miolo";

    private const string SqlArquivos =
        "select arquivo_origem, max(carregado_em) from (select arquivo_origem, carregado_em from sancoes union all select arquivo_origem, carregado_em from peps) group by arquivo_origem order by arquivo_origem";

    public Task<IReadOnlyList<Sancao>> SancoesDoDocumentoAsync(string documento, CancellationToken cancellationToken) =>
        ErrosOracle.TraduzirAsync<IReadOnlyList<Sancao>>(async () =>
        {
            using var conexao = new OracleConnection(conexaoOracle);
            await conexao.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var comando = Comando(conexao, SqlSancoes);
            comando.Parameters.Add("documento", OracleDbType.Varchar2).Value = documento;
            var lista = new List<Sancao>();
            using var leitor = await comando.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await leitor.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                lista.Add(new Sancao(
                    leitor.GetString(0),
                    Texto(leitor, 1),
                    Texto(leitor, 2),
                    Texto(leitor, 3)?.Trim(), // uf_orgao é CHAR(2)
                    Data(leitor, 4),
                    Data(leitor, 5),
                    Texto(leitor, 6)));
            }

            return lista;
        });

    public Task<IReadOnlyList<RegistroPep>> PepsDoMioloAsync(string cpfMiolo, CancellationToken cancellationToken) =>
        ErrosOracle.TraduzirAsync<IReadOnlyList<RegistroPep>>(async () =>
        {
            using var conexao = new OracleConnection(conexaoOracle);
            await conexao.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var comando = Comando(conexao, SqlPeps);
            comando.Parameters.Add("miolo", OracleDbType.Varchar2).Value = cpfMiolo;
            var lista = new List<RegistroPep>();
            using var leitor = await comando.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await leitor.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                lista.Add(new RegistroPep(leitor.GetString(0), Texto(leitor, 1), Data(leitor, 2), Data(leitor, 3)));
            }

            return lista;
        });

    public Task<FonteDasListas> FonteAsync(CancellationToken cancellationToken) =>
        ErrosOracle.TraduzirAsync(async () =>
        {
            using var conexao = new OracleConnection(conexaoOracle);
            await conexao.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var comando = Comando(conexao, SqlArquivos);
            var arquivos = new List<string>();
            DateTimeOffset? maisRecente = null;
            using var leitor = await comando.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await leitor.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                arquivos.Add(leitor.GetString(0));
                var momento = leitor.GetFieldValue<DateTimeOffset>(1); // TIMESTAMP WITH TIME ZONE: sem depender do fuso de quem lê
                maisRecente = maisRecente is null || momento > maisRecente ? momento : maisRecente;
            }

            return new FonteDasListas("oracle", arquivos, maisRecente);
        });

    private OracleCommand Comando(OracleConnection conexao, string sql)
    {
        var comando = conexao.CreateCommand();
        comando.BindByName = true;
        comando.CommandTimeout = timeoutSegundos;
        comando.CommandText = sql;
        return comando;
    }

    private static string? Texto(OracleDataReader leitor, int coluna) =>
        leitor.IsDBNull(coluna) ? null : leitor.GetString(coluna);

    private static DateOnly? Data(OracleDataReader leitor, int coluna) =>
        leitor.IsDBNull(coluna) ? null : DateOnly.FromDateTime(leitor.GetDateTime(coluna));
}
