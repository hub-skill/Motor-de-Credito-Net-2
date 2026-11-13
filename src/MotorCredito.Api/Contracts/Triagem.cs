namespace MotorCredito.Api.Contracts;

/// <summary>
/// Entrada: CPF ou CNPJ (com ou sem pontuação) e, para verificar PEP em CPF, o nome da pessoa. Os dois podem
/// ser nulos no JSON; a validação responde 422 (sem isso, o ASP.NET responderia 400 antes de chegar nela).
/// </summary>
public sealed record EntradaTriagem(string? Documento = null, string? Nome = null);

public sealed record MotivoSaida(string Codigo, string Texto);

public sealed record SancaoSaida(
    string Cadastro,
    string? Categoria,
    string? OrgaoSancionador,
    string? UfOrgao,
    DateOnly? DataInicio,
    DateOnly? DataFim,
    string? NumeroProcesso,
    bool Vigente);

public sealed record PepSaida(bool Verificado, bool Ativo, IReadOnlyList<string> Funcoes);

public sealed record FonteSaida(string Banco, IReadOnlyList<string> Arquivos, DateTimeOffset? CarregadoEm);

/// <summary>Saída em snake_case. O documento sai mascarado (CPF) ou formatado (CNPJ): nunca o CPF completo.</summary>
public sealed record SaidaTriagem(
    string Documento,
    string Tipo,
    string Resultado,
    IReadOnlyList<MotivoSaida> Motivos,
    IReadOnlyList<SancaoSaida> Sancoes,
    PepSaida Pep,
    DateOnly ConsultadoEm,
    FonteSaida Fonte);
