using System.Text;

namespace MotorCredito.Carga;

/// <summary>Leitor de CSV com ';' como separador, campos entre aspas (aspas duplas "" dentro do campo) e quebras de linha dentro das aspas.</summary>
public static class LeitorCsv
{
    public static IEnumerable<string[]> Ler(TextReader leitor, char separador = ';')
    {
        ArgumentNullException.ThrowIfNull(leitor);
        var campos = new List<string>();
        var atual = new StringBuilder();
        var entreAspas = false;
        var temConteudo = false;

        int c;
        while ((c = leitor.Read()) >= 0)
        {
            var ch = (char)c;
            if (entreAspas)
            {
                if (ch == '"')
                {
                    if (leitor.Peek() == '"')
                    {
                        _ = leitor.Read();
                        atual.Append('"');
                    }
                    else
                    {
                        entreAspas = false;
                    }
                }
                else
                {
                    atual.Append(ch);
                }

                continue;
            }

            switch (ch)
            {
                case '"':
                    entreAspas = true;
                    temConteudo = true;
                    break;
                case var s when s == separador:
                    campos.Add(atual.ToString());
                    atual.Clear();
                    temConteudo = true;
                    break;
                case '\r':
                    break;
                case '\n':
                    if (temConteudo || atual.Length > 0)
                    {
                        campos.Add(atual.ToString());
                        yield return [.. campos];
                    }

                    campos.Clear();
                    atual.Clear();
                    temConteudo = false;
                    break;
                default:
                    atual.Append(ch);
                    temConteudo = true;
                    break;
            }
        }

        if (temConteudo || atual.Length > 0)
        {
            campos.Add(atual.ToString());
            yield return [.. campos];
        }
    }
}
