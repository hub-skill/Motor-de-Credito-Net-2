using Microsoft.Extensions.Configuration;
using MotorCredito.Api.Configuration;

namespace MotorCredito.Carga;

/// <summary>Configuração da ferramenta: .env e variáveis de ambiente (as variáveis têm prioridade).</summary>
public sealed record Admins(string PostgresUsuario, string PostgresSenha, string OracleUsuario, string OracleSenha);

public static class Ambiente
{
    public static (BancosSettings Bancos, Admins Admins) Carregar()
    {
        ArquivoDotEnv.Carregar();
        var configuracao = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var bancos = configuracao.GetSection(BancosSettings.SectionName).Get<BancosSettings>() ?? new BancosSettings();
        var admin = configuracao.GetSection("Admin");
        return (bancos, new Admins(
            admin["Postgres:Usuario"] ?? "postgres",
            admin["Postgres:Senha"] ?? string.Empty,
            admin["Oracle:Usuario"] ?? "SYSTEM",
            admin["Oracle:Senha"] ?? string.Empty));
    }
}
