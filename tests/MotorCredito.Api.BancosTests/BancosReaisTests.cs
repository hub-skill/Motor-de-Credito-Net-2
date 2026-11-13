using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using MotorCredito.Api.Movimentacao;
using Xunit;

namespace MotorCredito.Api.BancosTests;

[Collection("Bancos")]
[Trait("Categoria", "Bancos")]
public sealed class BancosReaisTests(AmbienteDosBancos ambiente)
{
    private static readonly string[] Cadastros = ["CEIS", "CNEP"];
    private static readonly int[] ErrosDeAcessoOracle = [942, 1031];

    public static TheoryData<string> Bancos => new(AmbienteDosBancos.Provedores);

    [Theory]
    [MemberData(nameof(Bancos))]
    public async Task Listas_leem_sancoes_e_peps_reais_e_documento_inexistente_vem_vazio(string banco)
    {
        var (documento, miolo, nome) = await ambiente.AmostrasAsync(banco);
        var listas = ambiente.Listas(banco);

        var sancoes = await listas.SancoesDoDocumentoAsync(documento, CancellationToken.None);
        var peps = await listas.PepsDoMioloAsync(miolo, CancellationToken.None);

        Assert.NotEmpty(sancoes);
        Assert.All(sancoes, s => Assert.Contains(s.Cadastro, Cadastros));
        Assert.Contains(peps, p => p.Nome == nome);
        Assert.Empty(await listas.SancoesDoDocumentoAsync("00000000000000", CancellationToken.None));
        Assert.Empty(await listas.PepsDoMioloAsync("ABCDEF", CancellationToken.None));
    }

    [Theory]
    [MemberData(nameof(Bancos))]
    public async Task Fonte_traz_o_banco_os_arquivos_carregados_e_a_data_da_carga(string banco)
    {
        var fonte = await ambiente.Listas(banco).FonteAsync(CancellationToken.None);

        Assert.Equal(banco, fonte.Banco);
        Assert.Contains(fonte.Arquivos, a => a.EndsWith("_CEIS.csv", StringComparison.Ordinal));
        Assert.Contains(fonte.Arquivos, a => a.EndsWith("_CNEP.csv", StringComparison.Ordinal));
        Assert.Contains(fonte.Arquivos, a => a.EndsWith("_PEP.csv", StringComparison.Ordinal));
        Assert.NotNull(fonte.CarregadoEm);
    }

    [Fact]
    public async Task Postgres_aplicacao_so_insere_na_movimentacao_e_nas_listas_le_insere_e_apaga_sem_alterar()
    {
        await using var fonte = ambiente.FonteApp(somenteLeitura: false); // sessão de escrita: o que barra é a permissão
        var esquema = ambiente.Bancos.Postgres.Esquema;

        foreach (var sql in new[]
        {
            $"select count(*) from {esquema}.movimentacao",
            $"select count(*) from {esquema}.log_erros",
            $"update {esquema}.movimentacao set status_http = 200",
            $"delete from {esquema}.movimentacao",
            $"update {esquema}.sancoes set categoria = 'x'", // nas listas a aplicação lê, insere e apaga, mas não altera linha
            $"update {esquema}.peps set nome = 'x'",
        })
        {
            await using var comando = fonte.CreateCommand(sql);
            var erro = await Assert.ThrowsAsync<PostgresException>(() => comando.ExecuteNonQueryAsync());
            Assert.Equal("42501", erro.SqlState); // permissão negada
        }

        // A carga usa o usuário da aplicação: apagar o conteúdo das listas é permitido (aqui desfeito com rollback).
        await using (var con = await fonte.OpenConnectionAsync())
        await using (var tx = await con.BeginTransactionAsync())
        {
            foreach (var tabela in new[] { "sancoes", "peps" })
            {
                await using var apagar = new NpgsqlCommand($"delete from {esquema}.{tabela}", con, tx);
                Assert.True(await apagar.ExecuteNonQueryAsync() > 0);
                await using var contar = new NpgsqlCommand($"select count(*) from {esquema}.{tabela}", con, tx);
                Assert.Equal(0L, await contar.ExecuteScalarAsync());
            }

            await tx.RollbackAsync();
        }

        // INSERT em log_erros é permitido: sem movimentação correspondente, quem recusa é a chave estrangeira (23503).
        await using var insercao = fonte.CreateCommand(
            $"insert into {esquema}.log_erros (requisicao_id, request_id, metodo, rota, status, codigo, titulo, ambiente) values ('SEM-PAI', 'x', 'GET', '/', 400, 'x', 'x', 'x')");
        var chave = await Assert.ThrowsAsync<PostgresException>(() => insercao.ExecuteNonQueryAsync());
        Assert.Equal("23503", chave.SqlState);
    }

    [Fact]
    public async Task Oracle_aplicacao_so_insere_na_movimentacao_e_nas_listas_le_insere_e_apaga_sem_alterar()
    {
        using var con = new OracleConnection(ambiente.ConexaoAppOracle());
        await con.OpenAsync();

        foreach (var sql in new[]
        {
            "select count(*) from movimentacao",
            "select count(*) from log_erros",
            "update movimentacao set status_http = 200",
            "delete from movimentacao",
            "update sancoes set categoria = 'x'", // nas listas a aplicação lê, insere e apaga, mas não altera linha
            "update peps set nome = 'x'",
        })
        {
            using var comando = new OracleCommand(sql, con);
            var erro = await Assert.ThrowsAsync<OracleException>(() => comando.ExecuteNonQueryAsync());
            Assert.Contains(erro.Number, ErrosDeAcessoOracle); // privilégio insuficiente (o SELECT/UPDATE/DELETE não foi concedido)
        }

        // A carga usa o usuário da aplicação: apagar o conteúdo das listas é permitido (aqui desfeito com rollback).
        using (var tx = con.BeginTransaction())
        {
            foreach (var tabela in new[] { "sancoes", "peps" })
            {
                using var apagar = new OracleCommand($"delete from {tabela}", con) { Transaction = tx };
                Assert.True(await apagar.ExecuteNonQueryAsync() > 0);
                using var contar = new OracleCommand($"select count(*) from {tabela}", con) { Transaction = tx };
                Assert.Equal(0L, Convert.ToInt64(await contar.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture));
            }

            tx.Rollback();
        }

        // INSERT em log_erros é permitido: sem movimentação correspondente, quem recusa é a chave estrangeira (ORA-02291).
        using var insercao = new OracleCommand(
            "insert into log_erros (requisicao_id, request_id, metodo, rota, status, codigo, titulo, ambiente) values ('SEM-PAI', 'x', 'GET', '/', 400, 'x', 'x', 'x')", con);
        var chave = await Assert.ThrowsAsync<OracleException>(() => insercao.ExecuteNonQueryAsync());
        Assert.Equal(2291, chave.Number);
    }

    [Theory]
    [MemberData(nameof(Bancos))]
    public async Task Movimentacao_grava_requisicao_resposta_e_erro_na_mesma_transacao(string banco)
    {
        try
        {
            var repositorio = ambiente.Movimentacao(banco);
            var agora = DateTimeOffset.UtcNow;
            var ok = Ulid.Novo(agora);
            var comErro = Ulid.Novo(agora);
            var grande = Ulid.Novo(agora);
            var erro = new RegistroDeErro("rid", "POST", AmbienteDosBancos.RotaDeTeste + "/erro", 422, "entrada_invalida", "Dados de entrada inválidos.", null, 5, "Teste");

            await repositorio.GravarAsync(
            [
                new RegistroDeMovimentacao(ok, "POST", AmbienteDosBancos.RotaDeTeste + "/ok", "{\"x\":1}", agora, 200, "{\"y\":2}", agora.AddMilliseconds(3), "OK", "OK", null),
                new RegistroDeMovimentacao(comErro, "POST", AmbienteDosBancos.RotaDeTeste + "/erro", null, agora, 422, "{}", agora.AddMilliseconds(2), "ER", "ER", erro),
                new RegistroDeMovimentacao(grande, "POST", AmbienteDosBancos.RotaDeTeste + "/grande", new string('x', 32 * 1024), agora, 200, new string('y', 32 * 1024), agora, "OK", "OK", null),
            ], CancellationToken.None);

            var conteudo = banco == "oracle" ? "DBMS_LOB.GETLENGTH(requisicao_conteudo)" : "length(requisicao_conteudo)";
            Assert.Equal(1, await ambiente.AdminEscalarAsync(banco, $"select count(*) from {{s}}.movimentacao where requisicao_id = '{ok}' and status_http = 200 and situacao_resposta = 'OK'"));
            Assert.Equal(1, await ambiente.AdminEscalarAsync(banco, $"select count(*) from {{s}}.movimentacao where requisicao_id = '{grande}' and {conteudo} = 32768"));
            Assert.Equal(1, await ambiente.AdminEscalarAsync(banco, $"select count(*) from {{s}}.log_erros where requisicao_id = '{comErro}' and status = 422 and codigo = 'entrada_invalida'"));
            Assert.Equal(0, await ambiente.AdminEscalarAsync(banco, $"select count(*) from {{s}}.log_erros where requisicao_id = '{ok}'"));

            // O instante é gravado com fuso: o que volta é o mesmo instante, em qualquer fuso do servidor.
            var epoca = banco == "oracle"
                ? $"select round((cast(requisicao_em at time zone 'UTC' as date) - date '1970-01-01') * 86400) from {{s}}.movimentacao where requisicao_id = '{ok}'"
                : $"select extract(epoch from requisicao_em)::bigint from {{s}}.movimentacao where requisicao_id = '{ok}'";
            Assert.InRange(await ambiente.AdminEscalarAsync(banco, epoca) - agora.ToUnixTimeSeconds(), -1, 1);

            // As frações de segundo também chegam ao banco (microssegundos), não só os segundos.
            var microssegundos = banco == "oracle"
                ? $"select round(extract(second from sys_extract_utc(requisicao_em)) * 1000000) from {{s}}.movimentacao where requisicao_id = '{ok}'"
                : $"select round(extract(second from requisicao_em at time zone 'UTC') * 1000000)::bigint from {{s}}.movimentacao where requisicao_id = '{ok}'";
            var esperados = (agora.Second * 1_000_000L) + (agora.Ticks % TimeSpan.TicksPerSecond / 10);
            Assert.InRange(await ambiente.AdminEscalarAsync(banco, microssegundos) - esperados, -1, 1);

            // O mesmo ULID não entra duas vezes (a chave primária recusa) e o lote inteiro é desfeito.
            var repetido = new RegistroDeMovimentacao(ok, "GET", AmbienteDosBancos.RotaDeTeste + "/dup", null, agora, 200, null, agora, "OK", "OK", null);
            var novo = new RegistroDeMovimentacao(Ulid.Novo(agora), "GET", AmbienteDosBancos.RotaDeTeste + "/novo", null, agora, 200, null, agora, "OK", "OK", null);
            await Assert.ThrowsAnyAsync<Exception>(() => repositorio.GravarAsync([novo, repetido], CancellationToken.None));
            Assert.Equal(0, await ambiente.AdminEscalarAsync(banco, $"select count(*) from {{s}}.movimentacao where requisicao_id = '{novo.RequisicaoId}'"));
        }
        finally
        {
            await ambiente.LimparAsync(banco);
        }
    }

    [Theory]
    [MemberData(nameof(Bancos))]
    public async Task Api_de_ponta_a_ponta_com_banco_real_grava_movimentacao_e_erro(string banco)
    {
        var (documento, _, _) = await ambiente.AmostrasAsync(banco);
        const string Chave = "chave-de-teste-com-pelo-menos-32-caracteres";
        using var fabrica = new WebApplicationFactory<MotorCredito.Api.Services.TriagemService>().WithWebHostBuilder(b => // Program também existe na ferramenta
        {
            b.UseEnvironment("Production");
            b.UseSetting("Api:ApiKey", Chave);
            b.UseSetting("Bancos:Provedor", banco);
        });
        var cliente = fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Add("X-API-Key", Chave);
        try
        {
            var ok = await cliente.PostAsJsonAsync("/api/v1/triagem", new { documento, nome = "Maria da Silva" });
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            var idOk = ok.Headers.GetValues("X-Requisicao").Single();
            Assert.True(Ulid.Valido(idOk));

            var erro = await cliente.PostAsJsonAsync("/api/v1/triagem", new { documento = "123" });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, erro.StatusCode);
            var idErro = erro.Headers.GetValues("X-Requisicao").Single();

            using var semChave = fabrica.CreateClient();
            var recusada = await semChave.PostAsJsonAsync("/api/v1/triagem", new { documento });
            Assert.Equal(HttpStatusCode.Unauthorized, recusada.StatusCode);
            Assert.False(recusada.Headers.Contains("X-Requisicao")); // sem chave válida nada é gravado

            // a gravação é em segundo plano: espera até 5 s
            var gravado = 0L;
            for (var i = 0; i < 50 && gravado < 2; i++)
            {
                await Task.Delay(100);
                gravado = await ambiente.AdminEscalarAsync(banco, $"select count(*) from {{s}}.movimentacao where requisicao_id in ('{idOk}', '{idErro}')");
            }

            Assert.Equal(2, gravado);
            Assert.Equal(1, await ambiente.AdminEscalarAsync(banco, $"select count(*) from {{s}}.movimentacao where requisicao_id = '{idOk}' and status_http = 200 and situacao_requisicao = 'OK' and situacao_resposta = 'OK'"));
            Assert.Equal(1, await ambiente.AdminEscalarAsync(banco, $"select count(*) from {{s}}.log_erros where requisicao_id = '{idErro}' and status = 422 and codigo = 'entrada_invalida'"));
            Assert.Equal(0, await ambiente.AdminEscalarAsync(banco, $"select count(*) from {{s}}.log_erros where requisicao_id = '{idOk}'"));
            // o nome vai como veio (decisão do projeto) e o CPF ficaria mascarado; o documento do teste é um CNPJ/CPF público da lista
            var nome = banco == "oracle" ? "DBMS_LOB.INSTR(requisicao_conteudo, 'Maria da Silva') > 0" : "requisicao_conteudo like '%Maria da Silva%'";
            Assert.Equal(1, await ambiente.AdminEscalarAsync(banco, $"select count(*) from {{s}}.movimentacao where requisicao_id = '{idOk}' and {nome}"));
        }
        finally
        {
            await ambiente.LimparAsync(banco);
        }
    }
}
