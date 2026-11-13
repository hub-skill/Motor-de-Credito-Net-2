using System.Globalization;
using System.IO.Compression;

namespace MotorCredito.Carga;

/// <summary>
/// Baixa do Portal da Transparência o arquivo mais recente de CEIS e CNEP (diários) ou PEP (mensal). O Portal só
/// mantém o arquivo mais recente (os anteriores respondem 403): recua até 7 dias (CEIS/CNEP) ou 3 meses (PEP).
/// </summary>
public sealed class Baixador(HttpClient http)
{
    private const string Endereco = "https://portaldatransparencia.gov.br/download-de-dados";

    public async Task<string> BaixarAsync(string tipo, string pastaDestino, DateOnly hoje, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(tipo);
        var periodos = tipo == "pep"
            ? Enumerable.Range(0, 4).Select(i => hoje.AddMonths(-i).ToString("yyyyMM", CultureInfo.InvariantCulture))
            : Enumerable.Range(0, 8).Select(i => hoje.AddDays(-i).ToString("yyyyMMdd", CultureInfo.InvariantCulture));

        foreach (var periodo in periodos)
        {
            using var resposta = await http.GetAsync(new Uri($"{Endereco}/{tipo}/{periodo}"), cancellationToken).ConfigureAwait(false);
            if (!resposta.IsSuccessStatusCode)
            {
                continue;
            }

            using var memoria = new MemoryStream();
            await resposta.Content.CopyToAsync(memoria, cancellationToken).ConfigureAwait(false);
            memoria.Position = 0;
            using var zip = new ZipArchive(memoria, ZipArchiveMode.Read);
            var entrada = zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase));
            if (entrada is null)
            {
                continue;
            }

            Directory.CreateDirectory(pastaDestino);
            // Só o nome do arquivo: nunca o caminho que vem dentro do zip.
            var destino = Path.Combine(pastaDestino, Path.GetFileName(entrada.Name));
            using (var origem = await entrada.OpenAsync(cancellationToken).ConfigureAwait(false))
            using (var arquivo = File.Create(destino))
            {
                await origem.CopyToAsync(arquivo, cancellationToken).ConfigureAwait(false);
            }

            return destino;
        }

        throw new InvalidOperationException($"Nenhum arquivo {tipo.ToUpperInvariant()} disponível no Portal.");
    }
}
