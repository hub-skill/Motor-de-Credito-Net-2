using MotorCredito.Api.Configuration;
using Xunit;

namespace MotorCredito.Api.UnitTests;

public sealed class ArquivoDotEnvTests
{
    [Fact]
    public void Le_pares_ignora_comentarios_linhas_vazias_e_tira_aspas()
    {
        var pares = ArquivoDotEnv.Ler(
        [
            "# comentário",
            "",
            "Api__ApiKey=abc123",
            "  Bancos__Provedor = oracle  ",
            "Texto=\"com espaço\"",
            "Outro='aspas simples'",
            "SemValor=",
            "linha sem igual",
            "=semchave",
            "Senha=a=b=c",
        ]).ToList();

        Assert.Equal(
        [
            ("Api__ApiKey", "abc123"),
            ("Bancos__Provedor", "oracle"),
            ("Texto", "com espaço"),
            ("Outro", "aspas simples"),
            ("SemValor", ""),
            ("Senha", "a=b=c"),
        ], pares);
    }
}
