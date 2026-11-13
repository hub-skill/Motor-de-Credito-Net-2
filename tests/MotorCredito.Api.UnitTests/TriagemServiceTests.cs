using Microsoft.Extensions.Time.Testing;
using MotorCredito.Api.Documentos;
using MotorCredito.Api.Services;
using MotorCredito.Api.Triagem;
using Xunit;

namespace MotorCredito.Api.UnitTests;

public sealed class TriagemServiceTests
{
    private static readonly DocumentoNormalizado Cpf = Normalizar("529.982.247-25");
    private static readonly DocumentoNormalizado Cnpj = Normalizar("11.222.333/0001-81");

    private static DocumentoNormalizado Normalizar(string texto)
    {
        Assert.True(ValidadorDeDocumento.TentarNormalizar(texto, out var documento));
        return documento;
    }

    private sealed class Listas : IRepositorioDeListas
    {
        public int ConsultasDeFonte { get; private set; }

        public int ConsultasDePep { get; private set; }

        public List<Sancao> Sancoes { get; } = [];

        public List<RegistroPep> Peps { get; } = [];

        public Task<IReadOnlyList<Sancao>> SancoesDoDocumentoAsync(string documento, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Sancao>>(Sancoes);

        public Task<IReadOnlyList<RegistroPep>> PepsDoMioloAsync(string cpfMiolo, CancellationToken cancellationToken)
        {
            ConsultasDePep++;
            Assert.Equal("982247", cpfMiolo); // só os 6 dígitos do meio vão ao banco
            return Task.FromResult<IReadOnlyList<RegistroPep>>(Peps);
        }

        public Task<FonteDasListas> FonteAsync(CancellationToken cancellationToken)
        {
            ConsultasDeFonte++;
            return Task.FromResult(new FonteDasListas("postgres", ["a.csv"], null));
        }
    }

    [Fact]
    public async Task Resposta_mascara_o_cpf_e_traz_a_fonte()
    {
        var servico = new TriagemService(new Listas(), new FakeTimeProvider(new DateTimeOffset(2026, 11, 12, 15, 0, 0, TimeSpan.Zero)));

        var saida = await servico.TriarAsync(Cpf, "Maria", CancellationToken.None);

        Assert.Equal("***.982.247-**", saida.Documento);
        Assert.Equal("CPF", saida.Tipo);
        Assert.Equal("liberar", saida.Resultado);
        Assert.Equal(new DateOnly(2026, 11, 12), saida.ConsultadoEm);
        Assert.Equal(["a.csv"], saida.Fonte.Arquivos);
    }

    [Fact]
    public async Task Cnpj_sai_formatado_e_nao_consulta_pep()
    {
        var listas = new Listas();
        var servico = new TriagemService(listas, TimeProvider.System);

        var saida = await servico.TriarAsync(Cnpj, "Nome ignorado", CancellationToken.None);

        Assert.Equal("11.222.333/0001-81", saida.Documento);
        Assert.Equal("CNPJ", saida.Tipo);
        Assert.Equal(0, listas.ConsultasDePep);
    }

    [Fact]
    public async Task Cpf_sem_nome_nao_consulta_pep()
    {
        var listas = new Listas();
        var servico = new TriagemService(listas, TimeProvider.System);

        var saida = await servico.TriarAsync(Cpf, null, CancellationToken.None);

        Assert.Equal(0, listas.ConsultasDePep);
        Assert.Equal("P03", saida.Motivos.Single().Codigo);
    }

    [Fact]
    public async Task Data_da_consulta_e_a_de_brasilia_nao_a_do_utc()
    {
        // 01:00 UTC de 13/11 ainda é 22:00 de 12/11 em Brasília.
        var relogio = new FakeTimeProvider(new DateTimeOffset(2026, 11, 13, 1, 0, 0, TimeSpan.Zero));
        var servico = new TriagemService(new Listas(), relogio);

        var saida = await servico.TriarAsync(Cnpj, null, CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 11, 12), saida.ConsultadoEm);
    }

    [Fact]
    public async Task Fonte_fica_em_cache_por_5_minutos()
    {
        var listas = new Listas();
        var relogio = new FakeTimeProvider(new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero));
        var servico = new TriagemService(listas, relogio);

        await servico.TriarAsync(Cnpj, null, CancellationToken.None);
        await servico.TriarAsync(Cnpj, null, CancellationToken.None);
        Assert.Equal(1, listas.ConsultasDeFonte);

        relogio.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        await servico.TriarAsync(Cnpj, null, CancellationToken.None);
        Assert.Equal(2, listas.ConsultasDeFonte);
    }

    [Fact]
    public async Task Sancoes_vigentes_vem_antes_das_encerradas_com_a_marca_de_vigencia()
    {
        var listas = new Listas();
        listas.Sancoes.Add(new Sancao("CEIS", "Antiga", "Órgão", "SP", new DateOnly(2020, 1, 1), new DateOnly(2021, 1, 1), "1"));
        listas.Sancoes.Add(new Sancao("CNEP", "Atual", "Órgão", "RJ", new DateOnly(2025, 1, 1), null, "2"));
        var servico = new TriagemService(listas, new FakeTimeProvider(new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero)));

        var saida = await servico.TriarAsync(Cnpj, null, CancellationToken.None);

        Assert.Equal("bloquear", saida.Resultado);
        Assert.Equal([true, false], saida.Sancoes.Select(s => s.Vigente));
        Assert.Equal(["Atual", "Antiga"], saida.Sancoes.Select(s => s.Categoria));
    }
}
