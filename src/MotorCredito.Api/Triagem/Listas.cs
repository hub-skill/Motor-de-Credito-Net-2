namespace MotorCredito.Api.Triagem;

/// <summary>Uma sanção do CEIS ou do CNEP (uma linha da tabela sancoes).</summary>
public sealed record Sancao(
    string Cadastro,
    string? Categoria,
    string? OrgaoSancionador,
    string? UfOrgao,
    DateOnly? DataInicio,
    DateOnly? DataFim,
    string? NumeroProcesso);

/// <summary>Um registro de PEP (uma linha da tabela peps).</summary>
public sealed record RegistroPep(
    string Nome,
    string? DescricaoFuncao,
    DateOnly? DataFimExercicio,
    DateOnly? DataFimCarencia);

/// <summary>De onde vieram os dados: arquivos da CGU carregados e o momento da carga mais recente.</summary>
public sealed record FonteDasListas(string Banco, IReadOnlyList<string> Arquivos, DateTimeOffset? CarregadoEm);

/// <summary>Leitura das listas da CGU gravadas no banco. As implementações traduzem falhas do banco em 503/504.</summary>
public interface IRepositorioDeListas
{
    /// <summary>Sanções do documento (só dígitos e letras, como foi gravado), vigentes ou não.</summary>
    Task<IReadOnlyList<Sancao>> SancoesDoDocumentoAsync(string documento, CancellationToken cancellationToken);

    /// <summary>PEPs com os 6 dígitos visíveis do CPF (o casamento pelo nome é feito pela regra).</summary>
    Task<IReadOnlyList<RegistroPep>> PepsDoMioloAsync(string cpfMiolo, CancellationToken cancellationToken);

    Task<FonteDasListas> FonteAsync(CancellationToken cancellationToken);
}
