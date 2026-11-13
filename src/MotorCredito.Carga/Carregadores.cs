using Npgsql;
using NpgsqlTypes;
using Oracle.ManagedDataAccess.Client;
using MotorCredito.Api.Bancos;
using MotorCredito.Api.Configuration;

namespace MotorCredito.Carga;

/// <summary>Uma tabela pronta para gravar: as linhas já convertidas e o nome do arquivo de cada grupo.</summary>
public sealed record CargaDaTabela(LayoutTabela Layout, IReadOnlyList<(string Arquivo, IReadOnlyList<object?[]> Linhas)> Arquivos)
{
    public int Total => Arquivos.Sum(a => a.Linhas.Count);
}

/// <summary>
/// Substitui o conteúdo das tabelas dentro de UMA transação por banco: se algo falhar, nada é gravado e os dados
/// anteriores continuam valendo (a API não precisa reiniciar). Conecta com o usuário da APLICAÇÃO (select, insert e
/// delete nas listas), não com o administrador: o administrador só cria objetos (<c>migrar</c>).
/// </summary>
public static class Carregadores
{
    private const int LoteOracle = 2000;

    public static async Task PostgresAsync(BancosSettings bancos, IReadOnlyList<CargaDaTabela> tabelas, CancellationToken ct)
    {
        var pg = bancos.Postgres;
        await using var con = new NpgsqlConnection(ConexoesPostgres.Montar(
            pg.Host, pg.Porta, pg.Banco, pg.Usuario, pg.Senha, somenteLeitura: false,
            60, "motor-credito-net-2-carga", Enum.Parse<SslMode>(pg.SslMode, true)));
        await con.OpenAsync(ct);
        await using var tx = await con.BeginTransactionAsync(ct);

        foreach (var tabela in tabelas)
        {
#pragma warning disable CA2100 // nomes de tabela e colunas vêm do layout fixo; o schema é validado no migrador
            await using (var apagar = new NpgsqlCommand($"delete from {pg.Esquema}.{tabela.Layout.Tabela}", con, tx))
#pragma warning restore CA2100
            {
                await apagar.ExecuteNonQueryAsync(ct);
            }

            var colunas = string.Join(", ", tabela.Layout.Colunas.Select(c => c.Nome).Append("arquivo_origem").Append("origem_carga"));
#pragma warning disable CA2100
            await using var importador = await con.BeginBinaryImportAsync(
                $"copy {pg.Esquema}.{tabela.Layout.Tabela} ({colunas}) from stdin (format binary)", ct);
#pragma warning restore CA2100
            foreach (var (arquivo, linhas) in tabela.Arquivos)
            {
                foreach (var linha in linhas)
                {
                    await importador.StartRowAsync(ct);
                    for (var i = 0; i < linha.Length; i++)
                    {
                        await Escrever(importador, linha[i], tabela.Layout.Colunas[i].Tipo, ct);
                    }

                    await importador.WriteAsync(arquivo, NpgsqlDbType.Varchar, ct);
                    await importador.WriteAsync(LayoutsCgu.Marca, NpgsqlDbType.Varchar, ct);
                }
            }

            await importador.CompleteAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    private static async Task Escrever(NpgsqlBinaryImporter importador, object? valor, TipoColuna tipo, CancellationToken ct)
    {
        if (valor is null)
        {
            await importador.WriteNullAsync(ct);
            return;
        }

        switch (tipo)
        {
            case TipoColuna.Inteiro:
                await importador.WriteAsync((long)valor, NpgsqlDbType.Bigint, ct);
                break;
            case TipoColuna.Numerico:
                await importador.WriteAsync((decimal)valor, NpgsqlDbType.Numeric, ct);
                break;
            case TipoColuna.Data:
                await importador.WriteAsync((DateOnly)valor, NpgsqlDbType.Date, ct);
                break;
            case TipoColuna.TextoLongo:
                await importador.WriteAsync((string)valor, NpgsqlDbType.Text, ct);
                break;
            default:
                await importador.WriteAsync((string)valor, NpgsqlDbType.Varchar, ct);
                break;
        }
    }

    public static async Task OracleAsync(BancosSettings bancos, IReadOnlyList<CargaDaTabela> tabelas, CancellationToken ct)
    {
        var o = bancos.Oracle;
        using var con = new OracleConnection(ConexoesOracle.Montar(o.DataSource, o.Usuario, o.Senha));
        await con.OpenAsync(ct);
        using var tx = con.BeginTransaction();

        foreach (var tabela in tabelas)
        {
            var nome = $"{Esquemas.OracleDono}.{tabela.Layout.Tabela}";
#pragma warning disable CA2100
            using (var apagar = new OracleCommand($"delete from {nome}", con) { Transaction = tx })
#pragma warning restore CA2100
            {
                await apagar.ExecuteNonQueryAsync(ct);
            }

            var nomes = tabela.Layout.Colunas.Select(c => c.Nome).Append("arquivo_origem").Append("origem_carga").ToList();
            var sql = $"insert into {nome} ({string.Join(", ", nomes)}) values ({string.Join(", ", nomes.Select(n => ":" + n))})";
            foreach (var (arquivo, linhas) in tabela.Arquivos)
            {
                for (var inicio = 0; inicio < linhas.Count; inicio += LoteOracle)
                {
                    var lote = linhas.Skip(inicio).Take(LoteOracle).ToList();
#pragma warning disable CA2100
                    using var comando = new OracleCommand(sql, con) { Transaction = tx, BindByName = true, ArrayBindCount = lote.Count };
#pragma warning restore CA2100
                    for (var i = 0; i < tabela.Layout.Colunas.Count; i++)
                    {
                        var coluna = tabela.Layout.Colunas[i];
                        comando.Parameters.Add(coluna.Nome, TipoOracle(coluna.Tipo)).Value =
                            lote.Select(l => ParaOracle(l[i])).ToArray();
                    }

                    comando.Parameters.Add("arquivo_origem", OracleDbType.Varchar2).Value = lote.Select(_ => (object)arquivo).ToArray();
                    comando.Parameters.Add("origem_carga", OracleDbType.Varchar2).Value = lote.Select(_ => (object)LayoutsCgu.Marca).ToArray();
                    await comando.ExecuteNonQueryAsync(ct);
                }
            }
        }

        await tx.CommitAsync(ct);
    }

    private static OracleDbType TipoOracle(TipoColuna tipo) => tipo switch
    {
        TipoColuna.Inteiro => OracleDbType.Int64,
        TipoColuna.Numerico => OracleDbType.Decimal,
        TipoColuna.Data => OracleDbType.Date,
        TipoColuna.TextoLongo => OracleDbType.Clob,
        _ => OracleDbType.Varchar2,
    };

    private static object ParaOracle(object? valor) => valor switch
    {
        null => DBNull.Value,
        DateOnly d => d.ToDateTime(TimeOnly.MinValue),
        _ => valor,
    };
}
