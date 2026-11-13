using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MotorCredito.Api.Contracts;
using MotorCredito.Api.Documentos;
using MotorCredito.Api.Movimentacao;
using MotorCredito.Api.Services;
using MotorCredito.Api.Triagem;

namespace MotorCredito.Api.IntegrationTests;

/// <summary>Desliga o .env antes de qualquer teste: os testes nunca dependem da máquina nem tocam em banco real.</summary>
internal static class SemDotEnv
{
#pragma warning disable CA2255 // inicializador de módulo: precisa rodar antes da primeira fábrica
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Desligar() => Environment.SetEnvironmentVariable("MOTORCREDITO_SEM_DOTENV", "1");
#pragma warning restore CA2255
}

/// <summary>Configuração comum das fábricas: Production, chave de teste e repositórios falsos (nenhum banco é tocado).</summary>
internal static class ConfiguracaoDeTeste
{
    public static void Aplicar(IWebHostBuilder builder, ITriagemService? servico = null, IRepositorioDeListas? listas = null)
    {
        // Production: o WebApplicationFactory usa Development por padrão, e alguns comportamentos mudam com o
        // ambiente (ex.: ThrowOnBadRequest). Testar como em produção pega essas diferenças.
        builder.UseEnvironment("Production");
        builder.UseSetting("Api:ApiKey", ApiFactory.ChaveDeTeste);
        builder.ConfigureTestServices(servicos =>
        {
            servicos.RemoveAll<IRepositorioDeListas>();
            servicos.AddSingleton(listas ?? new ListasFalsas());
            servicos.RemoveAll<IRepositorioDeMovimentacao>();
            servicos.AddSingleton<IRepositorioDeMovimentacao>(new MovimentacaoFalsa());
            if (servico is not null)
            {
                servicos.RemoveAll<ITriagemService>();
                servicos.AddSingleton(servico);
            }
        });
    }
}

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string ChaveDeTeste = "chave-de-teste-com-pelo-menos-32-caracteres";

    protected override void ConfigureWebHost(IWebHostBuilder builder) => ConfiguracaoDeTeste.Aplicar(builder);
}

/// <summary>
/// Sobe a API com configurações próprias (rate limit baixo, docs ligadas, timeout curto) e, se informado, troca o
/// serviço ou as listas por falsos (para provocar timeout, erro inesperado ou falha de banco).
/// </summary>
public sealed class ApiFactoryConfiguravel(
    IReadOnlyDictionary<string, string> configuracao, ITriagemService? servico = null, IRepositorioDeListas? listas = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ConfiguracaoDeTeste.Aplicar(builder, servico, listas);
        foreach (var (chave, valor) in configuracao)
        {
            builder.UseSetting(chave, valor);
        }
    }
}

/// <summary>Serviço falso: atrasa (para o timeout) ou lança uma exceção (para o erro 500).</summary>
public sealed class ServicoFalso(TimeSpan atraso = default, Exception? lancar = null) : ITriagemService
{
    public async Task<SaidaTriagem> TriarAsync(DocumentoNormalizado documento, string? nome, CancellationToken cancellationToken)
    {
        await Task.Delay(atraso, cancellationToken);
        if (lancar is not null)
        {
            throw lancar;
        }

        return new SaidaTriagem(
            ValidadorDeDocumento.Mascarar(documento), "CPF", "liberar", [new MotivoSaida("OK", "ok")], [],
            new PepSaida(false, false, []), new DateOnly(2026, 11, 12), new FonteSaida("postgres", [], null));
    }
}

/// <summary>Listas falsas, na memória: o que cada teste precisa de sanções e PEPs, sem banco.</summary>
public sealed class ListasFalsas : IRepositorioDeListas
{
    private readonly Dictionary<string, List<Sancao>> _sancoes = [];
    private readonly Dictionary<string, List<RegistroPep>> _peps = [];

    /// <summary>Exceção a lançar em toda leitura (simula o banco fora do ar ou lento).</summary>
    public Exception? Falha { get; set; }

    public int ConsultasDeFonte { get; private set; }

    public ListasFalsas ComSancao(string documento, Sancao sancao)
    {
        _sancoes.TryAdd(documento, []);
        _sancoes[documento].Add(sancao);
        return this;
    }

    public ListasFalsas ComPep(string miolo, RegistroPep pep)
    {
        _peps.TryAdd(miolo, []);
        _peps[miolo].Add(pep);
        return this;
    }

    public Task<IReadOnlyList<Sancao>> SancoesDoDocumentoAsync(string documento, CancellationToken cancellationToken) =>
        Falha is not null
            ? Task.FromException<IReadOnlyList<Sancao>>(Falha)
            : Task.FromResult<IReadOnlyList<Sancao>>(_sancoes.GetValueOrDefault(documento) ?? []);

    public Task<IReadOnlyList<RegistroPep>> PepsDoMioloAsync(string cpfMiolo, CancellationToken cancellationToken) =>
        Falha is not null
            ? Task.FromException<IReadOnlyList<RegistroPep>>(Falha)
            : Task.FromResult<IReadOnlyList<RegistroPep>>(_peps.GetValueOrDefault(cpfMiolo) ?? []);

    public Task<FonteDasListas> FonteAsync(CancellationToken cancellationToken)
    {
        ConsultasDeFonte++;
        return Falha is not null
            ? Task.FromException<FonteDasListas>(Falha)
            : Task.FromResult(new FonteDasListas("postgres", ["20261112_CEIS.csv", "202609_PEP.csv"], new DateTimeOffset(2026, 11, 12, 7, 0, 0, TimeSpan.FromHours(-3))));
    }
}
