namespace MotorCredito.Api.Configuration;

public sealed class PostgresSettings
{
    public string Host { get; init; } = "127.0.0.1";

    public int Porta { get; init; } = 5432;

    public string Banco { get; init; } = "motor_credito";

    public string Usuario { get; init; } = "motor_app_net_2";

    public string Senha { get; init; } = string.Empty;

    public string SslMode { get; init; } = "Prefer";

    /// <summary>Schema das tabelas (a sessão da aplicação também usa search_path = este schema).</summary>
    public string Esquema { get; init; } = Esquemas.PostgresPadrao;

    public int TimeoutSegundos { get; init; } = 5;
}

public sealed class OracleSettings
{
    public string DataSource { get; init; } = "localhost:1521/XEPDB1";

    public string Usuario { get; init; } = Esquemas.UsuarioAppOracle;

    public string Senha { get; init; } = string.Empty;

    public int TimeoutSegundos { get; init; } = 5;
}

/// <summary>Seção "Bancos": qual banco a API usa e como conectar. Senhas só em variável de ambiente/.env.</summary>
public sealed class BancosSettings
{
    public const string SectionName = "Bancos";

    /// <summary>postgres ou oracle: as tabelas existem nos dois.</summary>
    public string Provedor { get; init; } = "postgres";

    public PostgresSettings Postgres { get; init; } = new();

    public OracleSettings Oracle { get; init; } = new();

    public bool UsaOracle => string.Equals(Provedor, "oracle", StringComparison.OrdinalIgnoreCase);

    public string NomeDoBanco => UsaOracle ? "oracle" : "postgres";
}

/// <summary>Nomes dos objetos desta API (recriação de teste: sufixo _2, para não colidir com a API antiga).</summary>
public static class Esquemas
{
    public const string PostgresPadrao = "credito_net_2";
    public const string OracleDono = "MOTOR_CREDITO_NET_2";
    public const string UsuarioAppPostgres = "motor_app_net_2";
    public const string UsuarioAppOracle = "MOTOR_APP_NET_2";

    /// <summary>Marca gravada em origem_carga: quem carregou a linha.</summary>
    public const string MarcaDeOrigem = "DOTNET2";
}
