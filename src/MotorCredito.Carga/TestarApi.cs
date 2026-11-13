using System.Net.Http.Json;
using System.Text.Json;
using MotorCredito.Api.Configuration;
using MotorCredito.Api.Documentos;

namespace MotorCredito.Carga;

/// <summary>
/// Teste de ponta a ponta contra a API no ar: escolhe, nos dados carregados, casos reais (sanção vigente, só encerradas,
/// PEP ativo, sem restrição...) e confere o resultado de cada um. Não imprime CPF: só o formato mascarado devolvido.
/// </summary>
public static class TestarApi
{
    public static async Task<bool> ExecutarAsync(string url, string chave, string banco, BancosSettings bancos, Admins admin, CancellationToken ct)
    {
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var sancoes = await Consultas.LerAsync(banco, bancos, admin, "sancoes", ["documento", "data_inicio", "data_fim"], ct);
        var peps = await Consultas.LerAsync(banco, bancos, admin, "peps", ["cpf_miolo", "nome", "data_fim_exercicio", "data_fim_carencia"], ct);

        var porDocumento = sancoes.GroupBy(l => l[0]!).ToDictionary(
            g => g.Key,
            g => g.Select(l => (Inicio: Data(l[1]), Fim: Data(l[2]))).ToList(),
            StringComparer.Ordinal);
        bool Vigente((DateOnly? Inicio, DateOnly? Fim) s) => (s.Inicio is null || s.Inicio <= hoje) && (s.Fim is null || s.Fim >= hoje);

        var vigente = porDocumento.FirstOrDefault(d => d.Value.Any(Vigente) && Valido(d.Key)).Key;
        var encerrada = porDocumento.FirstOrDefault(d => !d.Value.Any(Vigente) && Valido(d.Key)).Key;
        var pepAtivo = peps.FirstOrDefault(p => PepAtivo(Data(p[2]), Data(p[3]), hoje));
        var pepEncerrado = peps.FirstOrDefault(p => !PepAtivo(Data(p[2]), Data(p[3]), hoje)
            && !peps.Any(o => o[0] == p[0] && o[1] == p[1] && PepAtivo(Data(o[2]), Data(o[3]), hoje)));

        using var http = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Add("X-API-Key", chave);
        var falhas = 0;

        async Task Caso(string nome, object corpo, int status, string? resultado, string? codigo)
        {
            using var resposta = await http.PostAsJsonAsync("/api/v1/triagem", corpo, ct);
            var texto = await resposta.Content.ReadAsStringAsync(ct);
            string? obtido = null;
            string? codigos = null;
            if (resposta.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(texto);
                obtido = doc.RootElement.GetProperty("resultado").GetString();
                codigos = string.Join(",", doc.RootElement.GetProperty("motivos").EnumerateArray().Select(m => m.GetProperty("codigo").GetString()));
            }
            else
            {
                using var doc = JsonDocument.Parse(texto);
                codigos = doc.RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;
            }

            var ok = (int)resposta.StatusCode == status && obtido == resultado && (codigo is null || (codigos?.Contains(codigo, StringComparison.Ordinal) ?? false));
            falhas += ok ? 0 : 1;
            Console.WriteLine($"  [{(ok ? "OK" : "FALHOU")}] {nome}: HTTP {(int)resposta.StatusCode}, resultado {obtido ?? "-"}, motivos/código {codigos ?? "-"}");
        }

        if (vigente is not null)
        {
            await Caso("documento com sanção vigente", new { documento = vigente }, 200, "bloquear", "S01");
        }

        if (encerrada is not null)
        {
            await Caso("documento só com sanções encerradas", new { documento = encerrada }, 200, "liberar", "S02");
        }

        if (pepAtivo is not null)
        {
            var cpf = CpfComMiolo(pepAtivo[0]!);
            await Caso("PEP em exercício ou carência, com nome", new { documento = cpf, nome = pepAtivo[1] }, 200, "revisar", "P01");
            await Caso("o mesmo CPF sem nome (PEP não verificado)", new { documento = cpf }, 200, "liberar", "P03");
            await Caso("o mesmo CPF com o nome sem acentos e minúsculo", new { documento = cpf, nome = pepAtivo[1]!.ToLowerInvariant() }, 200, "revisar", "P01");
        }

        if (pepEncerrado is not null)
        {
            await Caso("foi PEP, carência encerrada", new { documento = CpfComMiolo(pepEncerrado[0]!), nome = pepEncerrado[1] }, 200, "liberar", "P02");
        }

        await Caso("CPF sem restrição", new { documento = "529.982.247-25", nome = "Fulano de Teste Inexistente" }, 200, "liberar", "OK");
        await Caso("CNPJ sem restrição", new { documento = "11.222.333/0001-81" }, 200, "liberar", "OK");
        await Caso("documento inválido", new { documento = "123" }, 422, null, "entrada_invalida");
        await Caso("corpo sem documento", new { nome = "x" }, 422, null, "entrada_invalida");

        using (var sem = new HttpClient { BaseAddress = new Uri(url) })
        {
            using var r = await sem.PostAsJsonAsync("/api/v1/triagem", new { documento = "52998224725" }, ct);
            var ok = (int)r.StatusCode == 401;
            falhas += ok ? 0 : 1;
            Console.WriteLine($"  [{(ok ? "OK" : "FALHOU")}] sem chave de API: HTTP {(int)r.StatusCode}");
        }

        Console.WriteLine(falhas == 0 ? "Todos os casos passaram." : $"{falhas} caso(s) falharam.");
        return falhas == 0;
    }

    private static DateOnly? Data(string? texto) =>
        texto is null ? null : DateOnly.ParseExact(texto, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static bool Valido(string documento) => ValidadorDeDocumento.TentarNormalizar(documento, out _);

    private static bool PepAtivo(DateOnly? fimExercicio, DateOnly? fimCarencia, DateOnly hoje) =>
        fimCarencia is not null ? fimCarencia >= hoje : fimExercicio is null || fimExercicio >= hoje;

    /// <summary>Monta um CPF válido com os 6 dígitos do meio informados (procura os 3 primeiros e os 2 finais).</summary>
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
