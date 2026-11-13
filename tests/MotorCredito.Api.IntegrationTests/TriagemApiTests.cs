using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MotorCredito.Api.Bancos;
using MotorCredito.Api.Triagem;
using Xunit;
using static MotorCredito.Api.IntegrationTests.ContratoDeErro;

namespace MotorCredito.Api.IntegrationTests;

/// <summary>A rota de negócio: POST /api/v1/triagem com listas falsas (sem banco).</summary>
public sealed class TriagemApiTests
{
    private const string Url = "/api/v1/triagem";
    private const string CpfLimpo = "52998224725";

    private static readonly DateOnly Hoje = DateOnly.FromDateTime(DateTime.UtcNow);

    private static HttpClient Cliente(ApiFactoryConfiguravel fabrica)
    {
        var cliente = fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Add("X-API-Key", ApiFactory.ChaveDeTeste);
        return cliente;
    }

    private static ApiFactoryConfiguravel Fabrica(ListasFalsas listas) => new(new Dictionary<string, string>(), listas: listas);

    private static async Task<JsonElement> Corpo(HttpResponseMessage resposta)
    {
        using var documento = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        return documento.RootElement.Clone();
    }

    private static async Task<HttpResponseMessage> EnviarAsync(HttpClient cliente, string json)
    {
        using var conteudo = new StringContent(json, Encoding.UTF8, "application/json");
        return await cliente.PostAsync(new Uri(Url, UriKind.Relative), conteudo);
    }

    [Fact]
    public async Task Sem_restricao_libera_e_a_resposta_tem_todos_os_campos_em_snake_case()
    {
        using var fabrica = Fabrica(new ListasFalsas());

        var resposta = await Cliente(fabrica).PostAsJsonAsync(Url, new { documento = CpfLimpo, nome = "Maria da Silva" });

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await Corpo(resposta);
        Assert.Equal("***.982.247-**", corpo.GetProperty("documento").GetString());
        Assert.Equal("CPF", corpo.GetProperty("tipo").GetString());
        Assert.Equal("liberar", corpo.GetProperty("resultado").GetString());
        Assert.Equal("OK", corpo.GetProperty("motivos")[0].GetProperty("codigo").GetString());
        Assert.Empty(corpo.GetProperty("sancoes").EnumerateArray());
        Assert.True(corpo.GetProperty("pep").GetProperty("verificado").GetBoolean());
        Assert.False(corpo.GetProperty("pep").GetProperty("ativo").GetBoolean());
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", corpo.GetProperty("consultado_em").GetString());
        Assert.Equal("postgres", corpo.GetProperty("fonte").GetProperty("banco").GetString());
        Assert.Equal(2, corpo.GetProperty("fonte").GetProperty("arquivos").GetArrayLength());
        Assert.NotEqual(JsonValueKind.Null, corpo.GetProperty("fonte").GetProperty("carregado_em").ValueKind);
        // nunca o CPF completo
        Assert.DoesNotContain(CpfLimpo, corpo.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sancao_vigente_bloqueia_e_aparece_na_lista()
    {
        var listas = new ListasFalsas().ComSancao("11222333000181", new Sancao("CEIS", "Impedimento", "Órgão X", "SP", Hoje.AddYears(-1), null, "99"));
        using var fabrica = Fabrica(listas);

        var resposta = await Cliente(fabrica).PostAsJsonAsync(Url, new { documento = "11.222.333/0001-81" });

        var corpo = await Corpo(resposta);
        Assert.Equal("bloquear", corpo.GetProperty("resultado").GetString());
        Assert.Equal("S01", corpo.GetProperty("motivos")[0].GetProperty("codigo").GetString());
        Assert.Equal("11.222.333/0001-81", corpo.GetProperty("documento").GetString());
        var sancao = corpo.GetProperty("sancoes")[0];
        Assert.Equal("CEIS", sancao.GetProperty("cadastro").GetString());
        Assert.Equal("Órgão X", sancao.GetProperty("orgao_sancionador").GetString());
        Assert.Equal(JsonValueKind.Null, sancao.GetProperty("data_fim").ValueKind);
        Assert.True(sancao.GetProperty("vigente").GetBoolean());
    }

    [Fact]
    public async Task Pep_ativo_manda_revisar_e_o_nome_casa_sem_acentos()
    {
        var listas = new ListasFalsas().ComPep("982247", new RegistroPep("MARIA DA SILVA", "Vereador", null, null));
        using var fabrica = Fabrica(listas);

        var resposta = await Cliente(fabrica).PostAsJsonAsync(Url, new { documento = CpfLimpo, nome = "  maria da silva " });

        var corpo = await Corpo(resposta);
        Assert.Equal("revisar", corpo.GetProperty("resultado").GetString());
        Assert.True(corpo.GetProperty("pep").GetProperty("ativo").GetBoolean());
        Assert.Equal("Vereador", corpo.GetProperty("pep").GetProperty("funcoes")[0].GetString());
    }

    [Fact]
    public async Task Cpf_sem_nome_nao_verifica_pep_e_nome_vazio_conta_como_ausente()
    {
        using var fabrica = Fabrica(new ListasFalsas());
        var cliente = Cliente(fabrica);

        foreach (var nome in new string?[] { null, "   " })
        {
            var corpo = await Corpo(await cliente.PostAsJsonAsync(Url, new { documento = CpfLimpo, nome }));
            Assert.False(corpo.GetProperty("pep").GetProperty("verificado").GetBoolean());
            Assert.Equal("P03", corpo.GetProperty("motivos")[0].GetProperty("codigo").GetString());
        }
    }

    [Fact]
    public async Task Campo_desconhecido_e_aceito_e_ignorado()
    {
        // Decisão do projeto (REGRAS-E-DECISOES.md): o campo extra não é erro.
        using var fabrica = Fabrica(new ListasFalsas());

        var resposta = await EnviarAsync(Cliente(fabrica), $$"""{"documento": "{{CpfLimpo}}", "extra": {"a": [1, 2]}, "outro": true}""");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }

    [Theory]
    [InlineData("""{"documento": "123"}""")]
    [InlineData("""{"documento": "52998224726"}""")]
    [InlineData("""{"documento": 52998224725}""")]
    [InlineData("""{"documento": "52998224725", "nome": 12}""")]
    [InlineData("""{"documento": "52998224725", "nome": "Maria\u0000"}""")]
    [InlineData("""{"nome": "Maria"}""")]
    [InlineData("""{"documento": null}""")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[1]")]
    [InlineData("")]
    public async Task Entrada_invalida_responde_422_e_nao_ecoa_nada(string json)
    {
        using var fabrica = Fabrica(new ListasFalsas());

        var resposta = await EnviarAsync(Cliente(fabrica), json);
        var texto = await resposta.Content.ReadAsStringAsync();

        await AfirmarProblema(resposta, HttpStatusCode.UnprocessableEntity, "entrada_invalida");
        Assert.DoesNotContain("52998224726", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("Maria", texto, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nome_de_201_caracteres_responde_422()
    {
        using var fabrica = Fabrica(new ListasFalsas());

        var resposta = await Cliente(fabrica).PostAsJsonAsync(Url, new { documento = CpfLimpo, nome = new string('a', 201) });

        await AfirmarProblema(resposta, HttpStatusCode.UnprocessableEntity, "entrada_invalida");
    }

    [Fact]
    public async Task Cnpj_alfanumerico_e_aceito()
    {
        using var fabrica = Fabrica(new ListasFalsas());

        var resposta = await Cliente(fabrica).PostAsJsonAsync(Url, new { documento = "12.ABC.345/01DE-35" });

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("CNPJ", (await Corpo(resposta)).GetProperty("tipo").GetString());
    }

    [Fact]
    public async Task Banco_indisponivel_responde_503_sem_vazar_detalhes()
    {
        var listas = new ListasFalsas { Falha = new BancoIndisponivelException(new InvalidOperationException("senha=abc host=interno")) };
        using var fabrica = Fabrica(listas);

        var resposta = await Cliente(fabrica).PostAsJsonAsync(Url, new { documento = CpfLimpo });
        var texto = await resposta.Content.ReadAsStringAsync();

        await AfirmarProblema(resposta, HttpStatusCode.ServiceUnavailable, "banco_indisponivel");
        Assert.DoesNotContain("senha", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("interno", texto, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Banco_lento_responde_504_banco_timeout()
    {
        using var fabrica = Fabrica(new ListasFalsas { Falha = new BancoExpirouException() });

        var resposta = await Cliente(fabrica).PostAsJsonAsync(Url, new { documento = CpfLimpo });

        await AfirmarProblema(resposta, HttpStatusCode.GatewayTimeout, "banco_timeout");
    }

    [Fact]
    public async Task Health_ready_com_o_banco_fora_responde_503_banco_indisponivel()
    {
        using var fabrica = Fabrica(new ListasFalsas { Falha = new BancoIndisponivelException() });
        var cliente = fabrica.CreateClient();

        var pronto = await cliente.GetAsync(new Uri("/health/ready", UriKind.Relative));
        var vivo = await cliente.GetAsync(new Uri("/health/live", UriKind.Relative));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, pronto.StatusCode);
        Assert.Equal("""{"status":"banco_indisponivel"}""", (await Corpo(pronto)).GetRawText());
        Assert.Equal(HttpStatusCode.OK, vivo.StatusCode); // liveness não consulta o banco
    }

    [Fact]
    public async Task Resposta_de_erro_do_banco_leva_request_id_e_cabecalhos()
    {
        using var fabrica = Fabrica(new ListasFalsas { Falha = new BancoIndisponivelException() });

        var resposta = await Cliente(fabrica).PostAsJsonAsync(Url, new { documento = CpfLimpo });

        Assert.Equal("nosniff", resposta.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal((await Corpo(resposta)).GetProperty("request_id").GetString(), resposta.Headers.GetValues("X-Request-ID").Single());
    }
}
