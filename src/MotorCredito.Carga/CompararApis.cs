using System.Text;
using System.Text.Json;
using MotorCredito.Api.Configuration;
using MotorCredito.Api.Documentos;

namespace MotorCredito.Carga;

/// <summary>
/// Roda os mesmos casos em duas APIs no ar (a antiga e a nova) e mostra só as diferenças de status, código de erro,
/// resultado, motivos, sanções, PEP e cabeçalhos. Não compara request_id, datas da consulta nem a origem dos dados.
/// </summary>
public static class CompararApis
{
    private sealed record Caso(string Nome, string Metodo, string Rota, byte[]? Corpo, bool ComChave, string? ChaveErrada = null);

    private static readonly string[] Cabecalhos =
        ["WWW-Authenticate", "Retry-After", "Cache-Control", "X-Content-Type-Options", "X-Frame-Options", "Strict-Transport-Security"];

    public static async Task<int> ExecutarAsync(
        string urlA, string chaveA, string urlB, string chaveB, string banco, BancosSettings bancos, Admins admin, CancellationToken ct)
    {
        var casos = await MontarCasosAsync(banco, bancos, admin, ct);
        using var a = new HttpClient { BaseAddress = new Uri(urlA), Timeout = TimeSpan.FromSeconds(30) };
        using var b = new HttpClient { BaseAddress = new Uri(urlB), Timeout = TimeSpan.FromSeconds(30) };
        var diferentes = 0;
        foreach (var caso in casos)
        {
            var ra = await Resumo(a, caso, chaveA, ct);
            var rb = await Resumo(b, caso, chaveB, ct);
            var igual = ra == rb;
            diferentes += igual ? 0 : 1;
            Console.WriteLine($"[{(igual ? "IGUAL" : "DIFERENTE")}] {caso.Nome}");
            if (!igual)
            {
                Console.WriteLine($"    antiga: {ra}");
                Console.WriteLine($"    nova:   {rb}");
            }
        }

        Console.WriteLine($"{casos.Count - diferentes} de {casos.Count} casos iguais.");
        return diferentes == 0 ? 0 : 1;
    }

    private static async Task<IReadOnlyList<Caso>> MontarCasosAsync(string banco, BancosSettings bancos, Admins admin, CancellationToken ct)
    {
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var sancoes = await Consultas.LerAsync(banco, bancos, admin, "sancoes", ["documento", "data_inicio", "data_fim"], ct);
        var peps = await Consultas.LerAsync(banco, bancos, admin, "peps", ["cpf_miolo", "nome", "data_fim_exercicio", "data_fim_carencia"], ct);
        static DateOnly? Data(string? t) => t is null ? null : DateOnly.ParseExact(t, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        static bool Ativo(DateOnly? fimEx, DateOnly? fimCar, DateOnly h) => fimCar is not null ? fimCar >= h : fimEx is null || fimEx >= h;

        var docs = sancoes.GroupBy(l => l[0]!).Where(g => ValidadorDeDocumento.TentarNormalizar(g.Key, out _)).ToList();
        bool Vigente(string?[] l) => (Data(l[1]) is null || Data(l[1]) <= hoje) && (Data(l[2]) is null || Data(l[2]) >= hoje);
        var vigentes = docs.Where(g => g.Any(Vigente)).Take(3).Select(g => g.Key).ToList();
        var encerradas = docs.Where(g => !g.Any(Vigente)).Take(3).Select(g => g.Key).ToList();
        var pepsAtivos = peps.Where(p => Ativo(Data(p[2]), Data(p[3]), hoje)).Take(3).ToList();
        var pepsEncerrados = peps.Where(p => !Ativo(Data(p[2]), Data(p[3]), hoje)).Take(3).ToList();

        var casos = new List<Caso>();
        void Json(string nome, string json, bool comChave = true, string? chaveErrada = null) =>
            casos.Add(new Caso(nome, "POST", "/api/v1/triagem", Encoding.UTF8.GetBytes(json), comChave, chaveErrada));
        static string J(object o) => JsonSerializer.Serialize(o);

        foreach (var d in vigentes)
        {
            Json("documento com sanção vigente", J(new { documento = d }));
        }

        foreach (var d in encerradas)
        {
            Json("documento só com sanções encerradas", J(new { documento = d, nome = "Qualquer Nome" }));
        }

        foreach (var p in pepsAtivos)
        {
            var cpf = CpfComMiolo(p[0]!);
            Json("PEP ativo com nome", J(new { documento = cpf, nome = p[1] }));
            Json("PEP ativo com nome minúsculo", J(new { documento = cpf, nome = p[1]!.ToLowerInvariant() }));
            Json("PEP ativo sem nome", J(new { documento = cpf }));
        }

        foreach (var p in pepsEncerrados)
        {
            Json("foi PEP, carência encerrada", J(new { documento = CpfComMiolo(p[0]!), nome = p[1] }));
        }

        Json("CPF limpo sem restrição", """{"documento":"52998224725","nome":"Fulano de Teste"}""");
        Json("CPF formatado", """{"documento":"529.982.247-25"}""");
        Json("CPF com espaços nas pontas", """{"documento":"  52998224725  ","nome":"  Fulano  "}""");
        Json("CNPJ formatado", """{"documento":"11.222.333/0001-81","nome":"ignorado"}""");
        Json("CNPJ alfanumérico", """{"documento":"12.ABC.345/01DE-35"}""");
        Json("CNPJ alfanumérico em minúsculas", """{"documento":"12.abc.345/01de-35"}""");
        Json("documento inválido", """{"documento":"123"}""");
        Json("dígito verificador errado", """{"documento":"52998224726"}""");
        Json("sequência repetida", """{"documento":"11111111111"}""");
        Json("documento com 21 caracteres", """{"documento":"123456789012345678901"}""");
        Json("documento com 20 caracteres inválido", """{"documento":"12345678901234567890"}""");
        Json("nome com 200 caracteres", J(new { documento = "52998224725", nome = new string('a', 200) }));
        Json("nome com 201 caracteres", J(new { documento = "52998224725", nome = new string('a', 201) }));
        Json("nome com caractere de controle", """{"documento":"52998224725","nome":"Maria\u0000Silva"}""");
        Json("nome com tab", """{"documento":"52998224725","nome":"Maria\tSilva"}""");
        Json("nome só com espaços", """{"documento":"52998224725","nome":"   "}""");
        Json("nome nulo", """{"documento":"52998224725","nome":null}""");
        Json("nome numérico", """{"documento":"52998224725","nome":12}""");
        Json("documento numérico", """{"documento":52998224725}""");
        Json("documento nulo", """{"documento":null}""");
        Json("sem documento", """{"nome":"Maria"}""");
        Json("objeto vazio", "{}");
        Json("null", "null");
        Json("lista", "[1]");
        Json("texto", "\"ola\"");
        Json("corpo vazio", string.Empty);
        Json("campo desconhecido", """{"documento":"52998224725","extra":{"a":[1,2]}}""");
        Json("JSON malformado", "{nao e json");
        Json("NaN", """{"documento":"52998224725","extra":NaN}""");
        Json("Infinity", """{"documento":"52998224725","extra":Infinity}""");
        Json("64 níveis", "{\"documento\":\"52998224725\",\"extra\":" + new string('[', 63) + new string(']', 63) + "}");
        Json("65 níveis", "{\"documento\":\"52998224725\",\"extra\":" + new string('[', 64) + new string(']', 64) + "}");
        Json("corpo acima de 4 KB", J(new { documento = "52998224725", nome = new string('a', 5000) }));
        casos.Add(new Caso("UTF-8 inválido", "POST", "/api/v1/triagem",
            [.. "{\"documento\":\"52998224725\",\"nome\":\"ol"u8, 0xFF, .. "\"}"u8], true));
        Json("sem chave de API", """{"documento":"52998224725"}""", comChave: false);
        Json("chave de API errada", """{"documento":"52998224725"}""", chaveErrada: "chave-errada-com-mais-de-trinta-e-dois-caracteres");
        casos.Add(new Caso("GET na rota de negócio (com chave)", "GET", "/api/v1/triagem", null, true));
        casos.Add(new Caso("GET na rota de negócio (sem chave)", "GET", "/api/v1/triagem", null, false));
        casos.Add(new Caso("rota inexistente (com chave)", "GET", "/nao-existe", null, true));
        casos.Add(new Caso("rota inexistente (sem chave)", "GET", "/nao-existe", null, false));
        casos.Add(new Caso("/health/live", "GET", "/health/live", null, false));
        casos.Add(new Caso("/health/ready", "GET", "/health/ready", null, false));
        casos.Add(new Caso("/openapi/v1.json desligado (com chave)", "GET", "/openapi/v1.json", null, true));
        casos.Add(new Caso("/openapi/v1.json desligado (sem chave)", "GET", "/openapi/v1.json", null, false));
        return casos;
    }

    private static async Task<string> Resumo(HttpClient http, Caso caso, string chave, CancellationToken ct)
    {
        using var pedido = new HttpRequestMessage(new HttpMethod(caso.Metodo), caso.Rota);
        if (caso.Corpo is not null)
        {
            pedido.Content = new ByteArrayContent(caso.Corpo);
            pedido.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        }

        if (caso.ComChave || caso.ChaveErrada is not null)
        {
            pedido.Headers.Add("X-API-Key", caso.ChaveErrada ?? chave);
        }

        using var resposta = await http.SendAsync(pedido, ct);
        var texto = await resposta.Content.ReadAsStringAsync(ct);
        var sb = new StringBuilder($"HTTP {(int)resposta.StatusCode}; tipo {resposta.Content.Headers.ContentType?.MediaType}");
        try
        {
            using var doc = JsonDocument.Parse(texto);
            var raiz = doc.RootElement;
            if (raiz.ValueKind == JsonValueKind.Object)
            {
                sb.Append("; campos ").Append(string.Join(',', raiz.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)));
                if (raiz.TryGetProperty("code", out var code))
                {
                    sb.Append("; code ").Append(code.GetString()).Append("; title ").Append(raiz.GetProperty("title").GetString());
                }

                if (raiz.TryGetProperty("resultado", out var resultado))
                {
                    sb.Append("; resultado ").Append(resultado.GetString());
                    sb.Append("; documento ").Append(raiz.GetProperty("documento").GetString());
                    sb.Append("; tipo ").Append(raiz.GetProperty("tipo").GetString());
                    sb.Append("; motivos ").Append(string.Join('|', raiz.GetProperty("motivos").EnumerateArray().Select(m => $"{m.GetProperty("codigo").GetString()}:{m.GetProperty("texto").GetString()}")));
                    sb.Append("; sancoes ").Append(string.Join('|', raiz.GetProperty("sancoes").EnumerateArray().Select(s => s.GetRawText().Length + (s.GetProperty("vigente").GetBoolean() ? "v" : "e"))));
                    sb.Append("; pep ").Append(raiz.GetProperty("pep").GetRawText());
                    sb.Append("; fonte.campos ").Append(string.Join(',', raiz.GetProperty("fonte").EnumerateObject().Select(p => p.Name)));
                }

                if (raiz.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String)
                {
                    sb.Append("; status ").Append(status.GetString());
                    if (raiz.TryGetProperty("banco", out var b))
                    {
                        sb.Append("; banco ").Append(b.GetString());
                    }
                }
            }
        }
        catch (JsonException)
        {
            sb.Append("; corpo não é JSON");
        }

        foreach (var cabecalho in Cabecalhos)
        {
            if (resposta.Headers.TryGetValues(cabecalho, out var valores))
            {
                sb.Append("; ").Append(cabecalho).Append('=').Append(string.Join(',', valores));
            }
        }

        sb.Append("; X-Request-ID ").Append(resposta.Headers.Contains("X-Request-ID") ? "sim" : "não");
        return sb.ToString();
    }

    private static string CpfComMiolo(string miolo)
    {
        for (var inicio = 0; inicio < 1000; inicio++)
        {
            for (var dv = 0; dv < 100; dv++)
            {
                var cpf = $"{inicio:D3}{miolo}{dv:D2}";
                if (ValidadorDeDocumento.CpfValido(cpf))
                {
                    return cpf;
                }
            }
        }

        throw new InvalidOperationException("Não achei CPF válido com esse miolo.");
    }
}
