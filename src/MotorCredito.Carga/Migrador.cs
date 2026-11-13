using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using MotorCredito.Api.Bancos;
using MotorCredito.Api.Configuration;

namespace MotorCredito.Carga;

/// <summary>
/// Cria usuário, schema, tabelas, funções e permissões, como administrador. Pode ser executado de novo: o que já
/// existe é mantido. Os scripts SQL (pasta banco/) viajam dentro do programa; <c>&lt;schema&gt;</c>, <c>&lt;dono&gt;</c>
/// e <c>&lt;usuario_app&gt;</c> são trocados pelos nomes configurados (sempre identificadores simples).
/// </summary>
public static partial class Migrador
{
    // Oracle: usuário, tabela, índice, chaves e colunas que já existem não são erro ao reexecutar.
    private static readonly HashSet<int> JaExiste = [1920, 955, 1408, 2260, 2261, 2264, 2275, 1430];

    public static async Task PostgresAsync(BancosSettings bancos, Admins admin, CancellationToken ct)
    {
        var pg = bancos.Postgres;
        Identificador(pg.Esquema);
        Identificador(pg.Usuario);
        SemAspas(pg.Senha);

        await using var con = new NpgsqlConnection(ConexoesPostgres.Montar(
            pg.Host, pg.Porta, pg.Banco, admin.PostgresUsuario, admin.PostgresSenha, somenteLeitura: false,
            pg.TimeoutSegundos, "motor-credito-net-2-migracao", Enum.Parse<SslMode>(pg.SslMode, true)));
        await con.OpenAsync(ct);

        // CREATE/ALTER ROLE não aceita parâmetro: a senha vai como literal (validada: sem aspas).
        var existe = await Escalar(con, "select 1 from pg_roles where rolname = $1", pg.Usuario, ct) is not null;
        await Executar(con, existe
            ? $"alter role {pg.Usuario} with login password '{pg.Senha}'"
            : $"create role {pg.Usuario} login nosuperuser nocreatedb nocreaterole password '{pg.Senha}'", ct);
        await Executar(con, $"grant connect on database {pg.Banco} to {pg.Usuario}", ct);

        foreach (var script in Scripts("postgres"))
        {
            await Executar(con, script.Replace("<schema>", pg.Esquema, StringComparison.Ordinal)
                .Replace("<usuario_app>", pg.Usuario, StringComparison.Ordinal), ct);
            Console.WriteLine("  PostgreSQL: script aplicado.");
        }
    }

    public static async Task OracleAsync(BancosSettings bancos, Admins admin, CancellationToken ct)
    {
        var o = bancos.Oracle;
        var dono = Esquemas.OracleDono;
        Identificador(o.Usuario);
        SemAspas(o.Senha);

        using var con = new OracleConnection(ConexoesOracle.Montar(o.DataSource, admin.OracleUsuario, admin.OracleSenha));
        await con.OpenAsync(ct);

        // Dono das tabelas: sem login. Usuário da aplicação: só CREATE SESSION e o que os scripts concedem.
        await Oracle(con, $"CREATE USER {dono} NO AUTHENTICATION DEFAULT TABLESPACE users QUOTA 2G ON users", ct);
        await Oracle(con, $"CREATE USER {o.Usuario} IDENTIFIED BY \"{o.Senha}\" DEFAULT TABLESPACE users QUOTA 0 ON users", ct);
        await Oracle(con, $"ALTER USER {o.Usuario} IDENTIFIED BY \"{o.Senha}\"", ct);
        await Oracle(con, $"GRANT CREATE SESSION TO {o.Usuario}", ct);

        foreach (var script in Scripts("oracle"))
        {
            var texto = script.Replace("<dono>", dono, StringComparison.Ordinal)
                .Replace("<usuario_app>", o.Usuario, StringComparison.Ordinal);
            foreach (var comando in SepararComandos(texto))
            {
                try
                {
                    await Oracle(con, comando, ct);
                }
                catch (OracleException)
                {
                    // Mostra o início do comando do script (sem senhas: os comandos de usuário não passam por aqui).
                    Console.Error.WriteLine($"  comando com erro: {comando[..Math.Min(120, comando.Length)].ReplaceLineEndings(" ")}");
                    throw;
                }
            }

            Console.WriteLine("  Oracle: script aplicado.");
        }
    }

    /// <summary>Separa o script nas linhas que contêm só "/" (formato SQL*Plus) e descarta as linhas de comentário.</summary>
    public static IReadOnlyList<string> SepararComandos(string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        var comandos = new List<string>();
        var atual = new StringBuilder();
        foreach (var linha in script.Split('\n'))
        {
            var texto = linha.TrimEnd('\r');
            if (texto.Trim() == "/")
            {
                Fechar();
            }
            else if (!texto.TrimStart().StartsWith("--", StringComparison.Ordinal))
            {
                atual.AppendLine(texto);
            }
        }

        Fechar();
        return comandos;

        void Fechar()
        {
            var comando = atual.ToString().Trim();
            if (comando.Length > 0)
            {
                comandos.Add(comando);
            }

            atual.Clear();
        }
    }

    private static IEnumerable<string> Scripts(string banco)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var prefixo = $"banco/{banco}/";
        // O nome lógico do recurso pode vir com barra invertida (Windows): normaliza para "/".
        foreach (var nome in assembly.GetManifestResourceNames().Where(n => n.Replace('\\', '/').StartsWith(prefixo, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var leitor = new StreamReader(assembly.GetManifestResourceStream(nome)!, Encoding.UTF8);
            yield return leitor.ReadToEnd();
        }
    }

    private static async Task<object?> Escalar(NpgsqlConnection con, string sql, string valor, CancellationToken ct)
    {
        await using var comando = new NpgsqlCommand(sql, con);
        comando.Parameters.Add(new NpgsqlParameter { Value = valor });
        return await comando.ExecuteScalarAsync(ct);
    }

    private static async Task Executar(NpgsqlConnection con, string sql, CancellationToken ct)
    {
#pragma warning disable CA2100 // SQL dos scripts do próprio programa; os nomes são identificadores validados
        await using var comando = new NpgsqlCommand(sql, con);
#pragma warning restore CA2100
        await comando.ExecuteNonQueryAsync(ct);
    }

    private static async Task Oracle(OracleConnection con, string sql, CancellationToken ct)
    {
        try
        {
#pragma warning disable CA2100
            using var comando = new OracleCommand(sql, con);
#pragma warning restore CA2100
            await comando.ExecuteNonQueryAsync(ct);
        }
        catch (OracleException e) when (JaExiste.Contains(e.Number))
        {
            // já existe: reexecutar a migração é seguro
        }
    }

    private static void Identificador(string nome)
    {
        if (!IdentificadorSimples().IsMatch(nome))
        {
            throw new ArgumentException($"Identificador inválido: {nome}");
        }
    }

    private static void SemAspas(string senha)
    {
        if (senha.Length == 0 || senha.Contains('"', StringComparison.Ordinal) || senha.Contains('\'', StringComparison.Ordinal))
        {
            throw new ArgumentException("Senha da aplicação ausente ou com aspas (defina no .env).");
        }
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex IdentificadorSimples();
}
