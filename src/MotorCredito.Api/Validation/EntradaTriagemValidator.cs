using System.Text.RegularExpressions;
using FluentValidation;
using MotorCredito.Api.Contracts;
using MotorCredito.Api.Documentos;

namespace MotorCredito.Api.Validation;

public sealed partial class EntradaTriagemValidator : AbstractValidator<EntradaTriagem>
{
    public const int TamanhoMaximoDoNome = 200;

    public EntradaTriagemValidator()
    {
        // A entrada já chega sem espaços nas pontas (ver TriagemEndpoints).
        RuleFor(x => x.Documento)
            .NotEmpty()
            .Length(11, 20) // cabe a máscara (CNPJ formatado tem 18)
            .Must(d => ValidadorDeDocumento.TentarNormalizar(d, out _))
            .WithMessage("documento não é um CPF ou CNPJ válido");

        RuleFor(x => x.Nome)
            .MaximumLength(TamanhoMaximoDoNome)
            .Must(n => n is null || !CaracteresDeControle().IsMatch(n))
            .WithMessage("nome contém caracteres de controle");
    }

    // Qualquer caractere de controle (inclusive tab, LF e CR) e o byte nulo.
    [GeneratedRegex(@"[\x00-\x1F\x7F]")]
    private static partial Regex CaracteresDeControle();
}
