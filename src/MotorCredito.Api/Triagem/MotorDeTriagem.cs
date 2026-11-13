using System.Globalization;
using System.Text;
using MotorCredito.Api.Documentos;

namespace MotorCredito.Api.Triagem;

/// <summary>A ordem dos valores é a gravidade: a decisão mais grave prevalece.</summary>
public enum Decisao
{
    Liberar = 0,
    Revisar = 1,
    Bloquear = 2,
}

public sealed record Motivo(string Codigo, string Texto);

public sealed record ResultadoDaTriagem(
    Decisao Decisao,
    IReadOnlyList<Motivo> Motivos,
    IReadOnlyList<Sancao> SancoesVigentes,
    IReadOnlyList<Sancao> SancoesEncerradas,
    bool PepVerificado,
    bool PepAtivo,
    IReadOnlyList<RegistroPep> Peps);

/// <summary>
/// Regras da triagem (REGRAS-E-DECISOES.md, seção 2). Funções puras, sem banco. Política PROVISÓRIA: validar com a
/// área de risco/compliance. S01 sanção vigente = bloquear; S02 só encerradas = informativo; P01 PEP em exercício ou
/// na carência = revisar; P02 PEP com carência encerrada = informativo; P03 CPF sem nome = PEP não verificado.
/// </summary>
public static class MotorDeTriagem
{
    public static ResultadoDaTriagem Triar(
        TipoDocumento tipo,
        string? nome,
        IReadOnlyList<Sancao> sancoes,
        IReadOnlyList<RegistroPep> candidatosPep,
        DateOnly hoje)
    {
        ArgumentNullException.ThrowIfNull(sancoes);
        ArgumentNullException.ThrowIfNull(candidatosPep);

        var decisao = Decisao.Liberar;
        var motivos = new List<Motivo>();

        var vigentes = sancoes.Where(s => SancaoVigente(s, hoje)).ToList();
        var encerradas = sancoes.Where(s => !SancaoVigente(s, hoje)).ToList();
        if (vigentes.Count > 0)
        {
            var cadastros = string.Join(", ", vigentes.Select(s => s.Cadastro).Distinct().Order(StringComparer.Ordinal));
            decisao = Decisao.Bloquear;
            motivos.Add(new Motivo(
                "S01", string.Create(CultureInfo.InvariantCulture, $"Sanção vigente em {cadastros} ({vigentes.Count} registro(s)).")));
        }
        else if (encerradas.Count > 0)
        {
            motivos.Add(new Motivo("S02", "Possui apenas sanções já encerradas (informativo)."));
        }

        // PEP só existe para CPF: em CNPJ o nome é ignorado.
        var pepVerificado = false;
        var pepAtivo = false;
        List<RegistroPep> peps = [];
        if (tipo == TipoDocumento.Cpf)
        {
            if (string.IsNullOrWhiteSpace(nome))
            {
                motivos.Add(new Motivo(
                    "P03", "PEP não verificado: informe o nome (a CGU publica o CPF mascarado)."));
            }
            else
            {
                pepVerificado = true;
                var alvo = NormalizarNome(nome);
                peps = candidatosPep.Where(p => NormalizarNome(p.Nome) == alvo).ToList();
                var ativos = peps.Where(p => PepAtivo(p, hoje)).ToList();
                if (ativos.Count > 0)
                {
                    pepAtivo = true;
                    decisao = decisao > Decisao.Revisar ? decisao : Decisao.Revisar;
                    var funcao = string.IsNullOrWhiteSpace(ativos[0].DescricaoFuncao)
                        ? "função não informada"
                        : ativos[0].DescricaoFuncao;
                    motivos.Add(new Motivo("P01", $"Pessoa exposta politicamente ({funcao})."));
                }
                else if (peps.Count > 0)
                {
                    motivos.Add(new Motivo("P02", "Foi PEP; carência encerrada (informativo)."));
                }
            }
        }

        if (motivos.Count == 0)
        {
            motivos.Add(new Motivo("OK", "Nenhuma restrição nas listas consultadas."));
        }

        return new ResultadoDaTriagem(decisao, motivos, vigentes, encerradas, pepVerificado, pepAtivo, peps);
    }

    /// <summary>Vigente: já começou (ou não tem data de início) e não terminou (sem data final = sem prazo).</summary>
    public static bool SancaoVigente(Sancao sancao, DateOnly hoje)
    {
        ArgumentNullException.ThrowIfNull(sancao);
        return (sancao.DataInicio is null || sancao.DataInicio <= hoje)
            && (sancao.DataFim is null || sancao.DataFim >= hoje);
    }

    /// <summary>PEP no exercício e até o fim da carência. Datas não informadas contam como ativas (cautela).</summary>
    public static bool PepAtivo(RegistroPep pep, DateOnly hoje)
    {
        ArgumentNullException.ThrowIfNull(pep);
        return pep.DataFimCarencia is not null
            ? pep.DataFimCarencia >= hoje
            : pep.DataFimExercicio is null || pep.DataFimExercicio >= hoje;
    }

    /// <summary>Maiúsculas, sem acentos e com espaços simples: "José  da Silva" vira "JOSE DA SILVA".</summary>
    public static string NormalizarNome(string nome)
    {
        ArgumentNullException.ThrowIfNull(nome);
        var semAcento = new StringBuilder(nome.Length);
        foreach (var c in nome.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                semAcento.Append(char.ToUpperInvariant(c));
            }
        }

        return string.Join(' ', semAcento.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
