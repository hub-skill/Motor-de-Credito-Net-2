using System.Globalization;
using MotorCredito.Api.Configuration;
using MotorCredito.Carga;

// Ferramenta do Motor de Crédito .NET 2: migrar, carregar, comparar e testar-api. Ver README (ou "ajuda").
var comando = args.Length > 0 ? args[0].ToLowerInvariant() : "ajuda";
var opcoes = args.Skip(1).Chunk(2).Where(p => p.Length == 2 && p[0].StartsWith("--", StringComparison.Ordinal))
    .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);
var banco = opcoes.GetValueOrDefault("--banco", "todos").ToLowerInvariant();
var bancosAlvo = banco == "todos" ? new[] { "postgres", "oracle" } : [banco];
if (bancosAlvo.Any(b => b is not ("postgres" or "oracle")))
{
    Console.Error.WriteLine("--banco deve ser postgres, oracle ou todos.");
    return 2;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
var ct = cts.Token;

try
{
    var (bancos, admin) = Ambiente.Carregar();
    switch (comando)
    {
        case "migrar":
            foreach (var b in bancosAlvo)
            {
                Console.WriteLine($"Migrando {b}...");
                await (b == "postgres" ? Migrador.PostgresAsync(bancos, admin, ct) : Migrador.OracleAsync(bancos, admin, ct));
            }

            return 0;

        case "carregar":
            await CarregarAsync(bancos, bancosAlvo, opcoes.GetValueOrDefault("--pasta"), ct);
            return 0;

        case "comparar":
            return await Comparador.CompararAsync(bancos, admin, ct) ? 0 : 1;

        case "testar-api":
            var url = opcoes.GetValueOrDefault("--url", "http://localhost:5080");
            var chave = opcoes.GetValueOrDefault("--chave") ?? Environment.GetEnvironmentVariable("Api__ApiKey") ?? string.Empty;
            return await TestarApi.ExecutarAsync(url, chave, bancosAlvo[0], bancos, admin, ct) ? 0 : 1;

        case "comparar-apis":
            // Duas APIs no ar com os mesmos casos: --url-a/--chave-a (referência) e --url-b/--chave-b (a nova).
            return await CompararApis.ExecutarAsync(
                opcoes.GetValueOrDefault("--url-a", "http://localhost:5090"), opcoes.GetValueOrDefault("--chave-a", string.Empty),
                opcoes.GetValueOrDefault("--url-b", "http://localhost:5080"),
                opcoes.GetValueOrDefault("--chave-b") ?? Environment.GetEnvironmentVariable("Api__ApiKey") ?? string.Empty,
                bancosAlvo[0], bancos, admin, ct);

        default:
            Console.WriteLine("""
                Uso: dotnet run --project src/MotorCredito.Carga -- <comando> [opções]
                  migrar      [--banco postgres|oracle|todos]   cria usuário, schema, tabelas e permissões
                  carregar    [--banco ...] [--pasta <csv>]     baixa CEIS, CNEP e PEP da CGU (ou usa os CSV da pasta) e grava
                  comparar                                      confere se PostgreSQL e Oracle têm os mesmos dados
                  testar-api  [--url ...] [--chave ...] [--banco postgres|oracle]   casos reais contra a API no ar
                  comparar-apis --url-a ... --chave-a ... --url-b ... [--chave-b ...]   mesmos casos em duas APIs (mostra só as diferenças)
                """);
            return comando == "ajuda" ? 0 : 2;
    }
}
catch (ArquivoCguInvalidoException e)
{
    Console.Error.WriteLine($"Arquivo inválido: {e.Message}");
    foreach (var problema in e.Problemas)
    {
        Console.Error.WriteLine($"  - {problema}");
    }

    return 1;
}
catch (Exception e) when (e is not OperationCanceledException)
{
    // Só o tipo e a mensagem: nunca a exceção inteira (a do driver pode trazer detalhes da conexão).
    Console.Error.WriteLine($"Falhou: {e.GetType().Name}: {e.Message}");
    return 1;
}

static async Task CarregarAsync(BancosSettings bancos, string[] bancosAlvo, string? pasta, CancellationToken ct)
{
    var baixar = pasta is null;
    pasta ??= Path.Combine(Directory.GetCurrentDirectory(), "dados-cgu");
    if (baixar)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MotorCredito-Carga/1.0");
        var baixador = new Baixador(http);
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        foreach (var tipo in new[] { "ceis", "cnep", "pep" })
        {
            Console.WriteLine($"Baixando {tipo.ToUpperInvariant()}...");
            Console.WriteLine($"  {Path.GetFileName(await baixador.BaixarAsync(tipo, pasta, hoje, ct))}");
        }
    }

    string Arquivo(string sufixo) =>
        Directory.GetFiles(pasta, $"*_{sufixo}.csv").OrderDescending().FirstOrDefault()
        ?? throw new FileNotFoundException($"Nenhum arquivo *_{sufixo}.csv em {pasta}.");

    // Lê e valida tudo ANTES de tocar em qualquer banco.
    var ceis = Arquivo("CEIS");
    var cnep = Arquivo("CNEP");
    var pep = Arquivo("PEP");
    Console.WriteLine("Lendo e validando os arquivos...");
    var tabelas = new List<CargaDaTabela>
    {
        new(LayoutsCgu.Sancoes,
        [
            (Path.GetFileName(ceis), ArquivoCgu.Ler(ceis, LayoutsCgu.Sancoes)),
            (Path.GetFileName(cnep), ArquivoCgu.Ler(cnep, LayoutsCgu.Sancoes)),
        ]),
        new(LayoutsCgu.Peps, [(Path.GetFileName(pep), ArquivoCgu.Ler(pep, LayoutsCgu.Peps))]),
    };
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  sanções: {tabelas[0].Total}, PEPs: {tabelas[1].Total}"));

    foreach (var b in bancosAlvo)
    {
        var relogio = System.Diagnostics.Stopwatch.StartNew();
        Console.WriteLine($"Gravando em {b}...");
        await (b == "postgres" ? Carregadores.PostgresAsync(bancos, tabelas, ct) : Carregadores.OracleAsync(bancos, tabelas, ct));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {b}: ok em {relogio.Elapsed.TotalSeconds:F1} s"));
    }
}
