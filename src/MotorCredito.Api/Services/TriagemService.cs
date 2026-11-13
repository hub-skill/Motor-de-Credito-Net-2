using System.Globalization;
using MotorCredito.Api.Configuration;
using MotorCredito.Api.Contracts;
using MotorCredito.Api.Documentos;
using MotorCredito.Api.Triagem;

namespace MotorCredito.Api.Services;

public interface ITriagemService
{
    Task<SaidaTriagem> TriarAsync(DocumentoNormalizado documento, string? nome, CancellationToken cancellationToken);
}

/// <summary>Consulta as listas, aplica as regras e monta a resposta (documento mascarado, nunca o CPF completo).</summary>
public sealed class TriagemService(IRepositorioDeListas listas, TimeProvider relogio) : ITriagemService, IDisposable
{
    // A origem dos dados (arquivos e data da carga) muda no máximo uma vez por dia: 5 minutos de cache.
    private static readonly TimeSpan ValidadeDaFonte = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _trava = new(1, 1);
    private FonteDasListas? _fonte;
    private DateTimeOffset _fonteValidaAte;

    public async Task<SaidaTriagem> TriarAsync(
        DocumentoNormalizado documento, string? nome, CancellationToken cancellationToken)
    {
        var sancoes = await listas.SancoesDoDocumentoAsync(documento.Valor, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<RegistroPep> candidatos = [];
        if (documento.Tipo == TipoDocumento.Cpf && !string.IsNullOrWhiteSpace(nome))
        {
            candidatos = await listas.PepsDoMioloAsync(ValidadorDeDocumento.MioloCpf(documento.Valor), cancellationToken)
                .ConfigureAwait(false);
        }

        var hoje = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(relogio.GetUtcNow(), FusoDeBrasilia).DateTime);
        var resultado = MotorDeTriagem.Triar(documento.Tipo, nome, sancoes, candidatos, hoje);
        var fonte = await FonteAsync(cancellationToken).ConfigureAwait(false);

        return new SaidaTriagem(
            ValidadorDeDocumento.Mascarar(documento),
            documento.Tipo == TipoDocumento.Cpf ? "CPF" : "CNPJ",
            resultado.Decisao.ToString().ToLowerInvariant(),
            [.. resultado.Motivos.Select(m => new MotivoSaida(m.Codigo, m.Texto))],
            [.. resultado.SancoesVigentes.Select(s => Saida(s, true)), .. resultado.SancoesEncerradas.Select(s => Saida(s, false))],
            new PepSaida(
                resultado.PepVerificado,
                resultado.PepAtivo,
                [.. resultado.Peps.Select(p => p.DescricaoFuncao ?? "função não informada")]),
            hoje,
            new FonteSaida(fonte.Banco, fonte.Arquivos, fonte.CarregadoEm));
    }

    public void Dispose() => _trava.Dispose();

    private static SancaoSaida Saida(Sancao s, bool vigente) =>
        new(s.Cadastro, s.Categoria, s.OrgaoSancionador, s.UfOrgao, s.DataInicio, s.DataFim, s.NumeroProcesso, vigente);

    private static TimeZoneInfo FusoDeBrasilia { get; } = ObterFuso();

    private static TimeZoneInfo ObterFuso()
    {
        foreach (var id in new[] { "America/Sao_Paulo", "E. South America Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // tenta o próximo nome (Linux x Windows)
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone("UTC-03", TimeSpan.FromHours(-3), "UTC-03", "UTC-03");
    }

    private async Task<FonteDasListas> FonteAsync(CancellationToken cancellationToken)
    {
        if (_fonte is { } atual && relogio.GetUtcNow() < _fonteValidaAte)
        {
            return atual;
        }

        await _trava.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_fonte is { } outra && relogio.GetUtcNow() < _fonteValidaAte)
            {
                return outra;
            }

            _fonte = await listas.FonteAsync(cancellationToken).ConfigureAwait(false);
            _fonteValidaAte = relogio.GetUtcNow().Add(ValidadeDaFonte);
            return _fonte;
        }
        finally
        {
            _trava.Release();
        }
    }
}
