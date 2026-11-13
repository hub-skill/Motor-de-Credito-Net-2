using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using MotorCredito.Api.Configuration;
using MotorCredito.Api.Contracts;
using MotorCredito.Api.Documentos;
using MotorCredito.Api.Errors;
using MotorCredito.Api.Services;

namespace MotorCredito.Api.Endpoints.V1;

public static class TriagemEndpoints
{
    public static IEndpointRouteBuilder MapTriagemV1(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/v1")
            .RequireAuthorization()
            .WithTags("triagem");

        grupo.MapPost("/triagem", TriarAsync)
            .WithName("Triagem")
            .Produces<SaidaTriagem>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        return app;
    }

    private static async Task<Ok<SaidaTriagem>> TriarAsync(
        EntradaTriagem? entrada,
        IValidator<EntradaTriagem> validador,
        ITriagemService servico,
        ApiSettings settings,
        CancellationToken cancellationToken)
    {
        // Corpo vazio ou "null": 422 (sem o "?" o ASP.NET responderia 400 antes de chegar aqui).
        if (entrada is null)
        {
            throw new EntradaInvalidaException("Dados de entrada inválidos.");
        }

        // Espaços nas pontas saem antes de validar; nome vazio ou só com espaços conta como não informado.
        var nome = entrada.Nome?.Trim();
        var normalizada = new EntradaTriagem(entrada.Documento?.Trim(), string.IsNullOrEmpty(nome) ? null : nome);

        var validacao = await validador.ValidateAsync(normalizada, cancellationToken);
        if (!validacao.IsValid || !ValidadorDeDocumento.TentarNormalizar(normalizada.Documento, out var documento))
        {
            // Mensagem genérica: não ecoa o documento nem o nome na resposta.
            throw new EntradaInvalidaException("Dados de entrada inválidos.");
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));
        try
        {
            return TypedResults.Ok(await servico.TriarAsync(documento, normalizada.Nome, cts.Token));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ProcessamentoExpirouException(); // expirou o nosso prazo (não foi o cliente que cancelou)
        }
    }
}
