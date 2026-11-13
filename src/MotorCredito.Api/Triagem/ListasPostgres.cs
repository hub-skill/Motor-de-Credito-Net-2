using Npgsql;

using MotorCredito.Api.Bancos;
using MotorCredito.Api.Movimentacao;

namespace MotorCredito.Api.Triagem;

/// <summary>Leitura das listas no PostgreSQL. SQL fixo e parametrizado; falhas do banco viram 503/504.</summary>
public sealed class ListasPostgres : IRepositorioDeListas
{
    private readonly NpgsqlDataSource _fonte;
    private readonly string _sqlSancoes;
    private readonly string _sqlPeps;
    private readonly string _sqlArquivos;

    public ListasPostgres(NpgsqlDataSource fonte, string esquema)
    {
        ArgumentNullException.ThrowIfNull(fonte);
        MovimentacaoPostgres.ValidarEsquema(esquema); // o schema vem da configuração e entra no SQL
        _fonte = fonte;
        _sqlSancoes = $"select cadastro, categoria, orgao_sancionador, uf_orgao, data_inicio, data_fim, numero_processo from {esquema}.sancoes where documento = $1 order by cadastro, codigo_sancao";
        _sqlPeps = $"select nome, descricao_funcao, data_fim_exercicio, data_fim_carencia from {esquema}.peps where cpf_miolo = $1";
        _sqlArquivos = $"select arquivo_origem, max(carregado_em) from (select arquivo_origem, carregado_em from {esquema}.sancoes union all select arquivo_origem, carregado_em from {esquema}.peps) t group by arquivo_origem order by arquivo_origem";
    }

    public Task<IReadOnlyList<Sancao>> SancoesDoDocumentoAsync(string documento, CancellationToken cancellationToken) =>
        ErrosPostgres.TraduzirAsync<IReadOnlyList<Sancao>>(async () =>
        {
            await using var comando = _fonte.CreateCommand(_sqlSancoes);
            comando.Parameters.Add(new NpgsqlParameter { Value = documento });
            var lista = new List<Sancao>();
            await using var leitor = await comando.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await leitor.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                lista.Add(new Sancao(
                    leitor.GetString(0),
                    Texto(leitor, 1),
                    Texto(leitor, 2),
                    Texto(leitor, 3),
                    Data(leitor, 4),
                    Data(leitor, 5),
                    Texto(leitor, 6)));
            }

            return lista;
        });

    public Task<IReadOnlyList<RegistroPep>> PepsDoMioloAsync(string cpfMiolo, CancellationToken cancellationToken) =>
        ErrosPostgres.TraduzirAsync<IReadOnlyList<RegistroPep>>(async () =>
        {
            await using var comando = _fonte.CreateCommand(_sqlPeps);
            comando.Parameters.Add(new NpgsqlParameter { Value = cpfMiolo });
            var lista = new List<RegistroPep>();
            await using var leitor = await comando.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await leitor.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                lista.Add(new RegistroPep(leitor.GetString(0), Texto(leitor, 1), Data(leitor, 2), Data(leitor, 3)));
            }

            return lista;
        });

    public Task<FonteDasListas> FonteAsync(CancellationToken cancellationToken) =>
        ErrosPostgres.TraduzirAsync(async () =>
        {
            await using var comando = _fonte.CreateCommand(_sqlArquivos);
            var arquivos = new List<string>();
            DateTimeOffset? maisRecente = null;
            await using var leitor = await comando.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await leitor.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                arquivos.Add(leitor.GetString(0));
                var momento = leitor.GetFieldValue<DateTimeOffset>(1);
                maisRecente = maisRecente is null || momento > maisRecente ? momento : maisRecente;
            }

            return new FonteDasListas("postgres", arquivos, maisRecente);
        });

    private static string? Texto(NpgsqlDataReader leitor, int coluna) =>
        leitor.IsDBNull(coluna) ? null : leitor.GetString(coluna);

    private static DateOnly? Data(NpgsqlDataReader leitor, int coluna) =>
        leitor.IsDBNull(coluna) ? null : leitor.GetFieldValue<DateOnly>(coluna);
}
