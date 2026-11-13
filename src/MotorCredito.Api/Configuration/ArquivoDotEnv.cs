namespace MotorCredito.Api.Configuration;

/// <summary>
/// Lê o arquivo <c>.env</c> (procurado a partir da pasta atual e subindo) e define as variáveis que ainda não existem
/// no ambiente: variável de ambiente já definida tem prioridade. Só para desenvolvimento local; o arquivo nunca é
/// versionado nem entra na imagem. Chaves no padrão do .NET ("__" separa seções). Desligue com
/// <c>MOTORCREDITO_SEM_DOTENV=1</c> (os testes automáticos fazem isso).
/// </summary>
public static class ArquivoDotEnv
{
    public static void Carregar(string? pastaInicial = null)
    {
        if (Environment.GetEnvironmentVariable("MOTORCREDITO_SEM_DOTENV") == "1")
        {
            return;
        }

        var arquivo = Localizar(pastaInicial ?? Directory.GetCurrentDirectory());
        if (arquivo is null)
        {
            return;
        }

        foreach (var (chave, valor) in Ler(File.ReadAllLines(arquivo)))
        {
            if (Environment.GetEnvironmentVariable(chave) is null)
            {
                Environment.SetEnvironmentVariable(chave, valor);
            }
        }
    }

    public static IEnumerable<(string Chave, string Valor)> Ler(IEnumerable<string> linhas)
    {
        ArgumentNullException.ThrowIfNull(linhas);
        foreach (var bruta in linhas)
        {
            var linha = bruta.Trim();
            if (linha.Length == 0 || linha[0] == '#')
            {
                continue;
            }

            var igual = linha.IndexOf('=', StringComparison.Ordinal);
            if (igual <= 0)
            {
                continue;
            }

            var valor = linha[(igual + 1)..].Trim();
            if (valor.Length >= 2 && valor[0] == valor[^1] && valor[0] is '"' or '\'')
            {
                valor = valor[1..^1];
            }

            yield return (linha[..igual].Trim(), valor);
        }
    }

    private static string? Localizar(string pasta)
    {
        for (var diretorio = new DirectoryInfo(pasta); diretorio is not null; diretorio = diretorio.Parent)
        {
            var candidato = Path.Combine(diretorio.FullName, ".env");
            if (File.Exists(candidato))
            {
                return candidato;
            }
        }

        return null;
    }
}
