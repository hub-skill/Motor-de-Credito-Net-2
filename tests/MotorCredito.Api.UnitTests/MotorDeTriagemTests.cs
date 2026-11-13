using MotorCredito.Api.Documentos;
using MotorCredito.Api.Triagem;
using Xunit;

namespace MotorCredito.Api.UnitTests;

public sealed class MotorDeTriagemTests
{
    private static readonly DateOnly Hoje = new(2026, 11, 12);

    private static Sancao Sancao(string cadastro = "CEIS", DateOnly? inicio = null, DateOnly? fim = null) =>
        new(cadastro, "Impedimento", "Órgão X", "SP", inicio, fim, "123");

    private static RegistroPep Pep(string nome = "Maria da Silva", DateOnly? fimExercicio = null, DateOnly? fimCarencia = null, string? funcao = "Prefeito") =>
        new(nome, funcao, fimExercicio, fimCarencia);

    private static ResultadoDaTriagem Triar(
        TipoDocumento tipo = TipoDocumento.Cpf, string? nome = null, Sancao[]? sancoes = null, RegistroPep[]? peps = null) =>
        MotorDeTriagem.Triar(tipo, nome, sancoes ?? [], peps ?? [], Hoje);

    [Fact]
    public void Sem_restricao_libera_com_motivo_OK()
    {
        var r = Triar(nome: "Maria da Silva");

        Assert.Equal(Decisao.Liberar, r.Decisao);
        Assert.Equal(["OK"], r.Motivos.Select(m => m.Codigo));
    }

    [Fact]
    public void Sancao_vigente_bloqueia_com_S01_e_lista_os_cadastros()
    {
        var r = Triar(TipoDocumento.Cnpj, sancoes: [Sancao("CNEP"), Sancao("CEIS"), Sancao("CEIS")]);

        Assert.Equal(Decisao.Bloquear, r.Decisao);
        var motivo = Assert.Single(r.Motivos);
        Assert.Equal("S01", motivo.Codigo);
        Assert.Equal("Sanção vigente em CEIS, CNEP (3 registro(s)).", motivo.Texto);
    }

    [Fact]
    public void So_sancoes_encerradas_e_informativo_S02()
    {
        var r = Triar(TipoDocumento.Cnpj, sancoes: [Sancao(fim: Hoje.AddDays(-1))]);

        Assert.Equal(Decisao.Liberar, r.Decisao);
        Assert.Equal(["S02"], r.Motivos.Select(m => m.Codigo));
        Assert.Single(r.SancoesEncerradas);
    }

    [Fact]
    public void Vigencia_da_sancao_comeca_no_inicio_e_termina_no_fim_inclusive()
    {
        Assert.True(MotorDeTriagem.SancaoVigente(Sancao(inicio: null, fim: null), Hoje)); // sem prazo
        Assert.True(MotorDeTriagem.SancaoVigente(Sancao(inicio: Hoje, fim: Hoje), Hoje)); // começa e termina hoje
        Assert.False(MotorDeTriagem.SancaoVigente(Sancao(inicio: Hoje.AddDays(1)), Hoje)); // ainda não começou
        Assert.False(MotorDeTriagem.SancaoVigente(Sancao(fim: Hoje.AddDays(-1)), Hoje)); // já terminou
    }

    [Fact]
    public void Pep_em_exercicio_manda_revisar_P01_com_a_funcao()
    {
        var r = Triar(nome: "Maria da Silva", peps: [Pep(fimExercicio: null, funcao: "Vereador")]);

        Assert.Equal(Decisao.Revisar, r.Decisao);
        Assert.True(r.PepVerificado);
        Assert.True(r.PepAtivo);
        Assert.Equal("Pessoa exposta politicamente (Vereador).", r.Motivos.Single().Texto);
    }

    [Fact]
    public void Pep_sem_funcao_informada_usa_o_texto_padrao()
    {
        var r = Triar(nome: "Maria da Silva", peps: [Pep(funcao: null)]);

        Assert.Equal("Pessoa exposta politicamente (função não informada).", r.Motivos.Single().Texto);
    }

    [Theory]
    [InlineData(-1, null, false)] // fim do exercício ontem, sem carência: encerrado
    [InlineData(0, null, true)] // termina hoje: ainda ativo
    [InlineData(null, null, true)] // datas não informadas contam como ativas
    [InlineData(-400, 0, true)] // carência até hoje manda mais que o exercício encerrado
    [InlineData(null, -1, false)] // carência encerrada, mesmo sem fim de exercício
    public void Pep_ativo_considera_carencia_e_datas_nulas(int? fimExercicioEmDias, int? fimCarenciaEmDias, bool ativo)
    {
        var pep = Pep(
            fimExercicio: fimExercicioEmDias is int e ? Hoje.AddDays(e) : null,
            fimCarencia: fimCarenciaEmDias is int c ? Hoje.AddDays(c) : null);

        Assert.Equal(ativo, MotorDeTriagem.PepAtivo(pep, Hoje));
    }

    [Fact]
    public void Foi_pep_com_carencia_encerrada_e_informativo_P02()
    {
        var r = Triar(nome: "Maria da Silva", peps: [Pep(fimCarencia: Hoje.AddDays(-30))]);

        Assert.Equal(Decisao.Liberar, r.Decisao);
        Assert.True(r.PepVerificado);
        Assert.False(r.PepAtivo);
        Assert.Equal(["P02"], r.Motivos.Select(m => m.Codigo));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Cpf_sem_nome_nao_verifica_pep_P03(string? nome)
    {
        var r = Triar(nome: nome, peps: [Pep()]);

        Assert.False(r.PepVerificado);
        Assert.Equal(Decisao.Liberar, r.Decisao);
        Assert.Equal(["P03"], r.Motivos.Select(m => m.Codigo));
    }

    [Fact]
    public void Cnpj_ignora_o_nome_e_nao_verifica_pep()
    {
        var r = Triar(TipoDocumento.Cnpj, nome: "Maria da Silva", peps: [Pep()]);

        Assert.False(r.PepVerificado);
        Assert.Equal(["OK"], r.Motivos.Select(m => m.Codigo));
    }

    [Fact]
    public void Nome_diferente_do_candidato_nao_casa()
    {
        var r = Triar(nome: "Maria Souza", peps: [Pep("Maria da Silva")]);

        Assert.True(r.PepVerificado);
        Assert.Empty(r.Peps);
        Assert.Equal(["OK"], r.Motivos.Select(m => m.Codigo));
    }

    [Fact]
    public void A_decisao_mais_grave_prevalece_e_todos_os_motivos_aparecem()
    {
        var r = Triar(nome: "Maria da Silva", sancoes: [Sancao()], peps: [Pep()]);

        Assert.Equal(Decisao.Bloquear, r.Decisao); // bloquear > revisar
        Assert.Equal(["S01", "P01"], r.Motivos.Select(m => m.Codigo));
    }

    [Theory]
    [InlineData("José  da   Silva", "JOSE DA SILVA")]
    [InlineData("  maria JOÃO  ", "MARIA JOAO")]
    [InlineData("Ção", "CAO")]
    public void Normalizar_nome_tira_acentos_e_espacos_extras(string entrada, string esperado) =>
        Assert.Equal(esperado, MotorDeTriagem.NormalizarNome(entrada));

    [Fact]
    public void Nome_normalizado_casa_com_acentos_e_caixa_diferentes()
    {
        var r = Triar(nome: "jose da silva", peps: [Pep("JOSÉ DA SILVA")]);

        Assert.True(r.PepAtivo);
    }
}
