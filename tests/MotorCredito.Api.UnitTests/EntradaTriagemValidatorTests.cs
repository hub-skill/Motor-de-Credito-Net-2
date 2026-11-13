using MotorCredito.Api.Contracts;
using MotorCredito.Api.Validation;
using Xunit;

namespace MotorCredito.Api.UnitTests;

public sealed class EntradaTriagemValidatorTests
{
    private static readonly EntradaTriagemValidator Validador = new();

    [Theory]
    [InlineData("52998224725")]
    [InlineData("529.982.247-25")]
    [InlineData("11.222.333/0001-81")]
    [InlineData("12.ABC.345/01DE-35")] // CNPJ alfanumérico da Receita
    public void Documento_valido_passa(string documento) =>
        Assert.True(Validador.Validate(new EntradaTriagem(documento)).IsValid);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("52998224726")] // dígito verificador errado
    [InlineData("11111111111")] // sequência repetida
    [InlineData("529.982.247-25 e mais texto")]
    [InlineData("123456789012345678901")] // acima de 20 caracteres
    public void Documento_invalido_nao_passa(string? documento) =>
        Assert.False(Validador.Validate(new EntradaTriagem(documento)).IsValid);

    [Fact]
    public void Nome_de_200_caracteres_passa_e_de_201_nao()
    {
        Assert.True(Validador.Validate(new EntradaTriagem("52998224725", new string('a', 200))).IsValid);
        Assert.False(Validador.Validate(new EntradaTriagem("52998224725", new string('a', 201))).IsValid);
    }

    [Theory]
    [InlineData("Maria\u0000Silva")]
    [InlineData("Maria\nSilva")]
    [InlineData("Maria\tSilva")]
    [InlineData("Maria\u007FSilva")]
    public void Nome_com_caractere_de_controle_nao_passa(string nome) =>
        Assert.False(Validador.Validate(new EntradaTriagem("52998224725", nome)).IsValid);

    [Fact]
    public void Nome_ausente_ou_com_acentos_passa()
    {
        Assert.True(Validador.Validate(new EntradaTriagem("52998224725", null)).IsValid);
        Assert.True(Validador.Validate(new EntradaTriagem("52998224725", "José da Conceição")).IsValid);
    }
}
