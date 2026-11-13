# Regras e decisões da API Motor de Crédito (.NET)

Extraído em 12/11/2026 da API atual, que continua intacta em `C:\Felipe\Programacao\DotNet\APIs\Motor-de-Credito-Net`
(fontes lidas: `DOCUMENTACAO.md`, `MotorDeTriagem.cs`, `LayoutsCgu.cs`, `EntradaTriagemValidator.cs`, `ValidadorDeDocumento.cs`,
`Dockerfile`, `k8s/`). Este arquivo é a especificação da recriação de teste em `DotNet_2` e o registro das decisões que
tomamos. No futuro, outras APIs derivarão dela. Onde houver divergência entre este texto e a API atual, vale a API atual.

## 1. O que a API faz

Recebe um **CPF ou CNPJ** (e, para CPF, o nome) e verifica nas listas públicas da CGU (Portal da Transparência):

| Lista | Conteúdo | Atualização na fonte |
|---|---|---|
| CEIS | Empresas inidôneas e suspensas | Diária |
| CNEP | Empresas punidas pela Lei Anticorrupção | Diária |
| PEP | Pessoas expostas politicamente (cargos e carência) | Mensal |

Responde `liberar`, `revisar` ou `bloquear`, sempre com os **motivos** (código e texto): atende ao direito de explicação
das decisões automatizadas (LGPD, art. 20). Usos pensados: primeira regra eliminatória do motor de crédito (antes de
consultas pagas a birôs), PLD/KYC, cadastro de clientes e fornecedores, validação de documento.

Funciona com **PostgreSQL ou Oracle**, à escolha na configuração (`Bancos__Provedor`), com as mesmas tabelas e dados nos dois.
As consultas são locais (a API não chama o governo a cada requisição): resposta típica de 3 a 15 ms.

## 2. Regras de decisão (política PROVISÓRIA: validar com risco/compliance antes de produção)

| Código | Situação | Efeito |
|---|---|---|
| `S01` | Sanção **vigente** no CEIS ou no CNEP | `bloquear` |
| `S02` | Só sanções **encerradas** | nenhum (informativo) |
| `P01` | PEP **em exercício** ou **dentro da carência** | `revisar` |
| `P02` | Foi PEP, carência encerrada | nenhum (informativo) |
| `P03` | CPF enviado **sem nome**: PEP não verificado | nenhum (informativo) |
| `OK` | Nenhuma restrição | `liberar` |

- Gravidade: `bloquear` > `revisar` > `liberar`. Com mais de um motivo, vale o mais grave; todos os motivos aparecem.
- Texto dos motivos: `S01` = "Sanção vigente em {cadastros separados por vírgula} ({n} registro(s))."; `S02` = "Possui apenas
  sanções já encerradas (informativo)."; `P01` = "Pessoa exposta politicamente ({função})." (ou "função não informada");
  `P02` = "Foi PEP; carência encerrada (informativo)."; `P03` = "PEP não verificado: informe o nome (a CGU publica o CPF
  mascarado)."; `OK` = "Nenhuma restrição nas listas consultadas."
- **Sanção vigente**: já começou (ou não tem data de início) e não terminou. Sem data final = sem prazo = vigente.
- **PEP ativo**: se há data de fim da carência, vale `fim da carência >= hoje`; senão, `sem data de fim do exercício` ou
  `fim do exercício >= hoje`. **Datas não informadas pela CGU contam como ativas** (decisão: a favor da cautela).
- **PEP não bloqueia**: exige análise reforçada (`revisar`), nunca `bloquear`.
- A checagem de PEP **só existe para CPF**. Para CNPJ, os sócios devem ser avaliados separadamente. `nome` é ignorado em CNPJ.
- **Como casa o PEP**: a CGU publica o CPF do PEP mascarado (`***.123.456-**`). Compara-se os **6 dígitos visíveis (`cpf_miolo`) e
  o nome**, normalizado (maiúsculas, sem acentos, espaços simples: "José  da Silva" = "JOSE DA SILVA"). O nome precisa ser o
  completo; abreviações não casam. Homônimos com os mesmos 6 dígitos são raros, mas possíveis: por isso PEP leva a `revisar`.
- Lista de sanções na resposta: **todas** (vigentes e encerradas), com `vigente` true/false.

## 3. Entrada

`POST /api/v1/triagem`, `Content-Type: application/json`, corpo de até **4 KB**:

| Campo | Regras |
|---|---|
| `documento` | Obrigatório. CPF ou CNPJ, com ou sem pontuação (`.`, `-`, `/`, espaços). **11 a 20 caracteres** (tamanho de documento = 20 em toda a base). Dígitos verificadores conferidos. Aceita o **CNPJ alfanumérico** da Receita (`12.ABC.345/01DE-35`; módulo 11, pesos 2 a 9, caractere vale o ASCII menos 48) |
| `nome` | Opcional. Até 200 caracteres, sem caracteres de controle. Vazio ou só espaços conta como não informado |

- Espaços nas pontas são removidos antes de validar. POST (e não GET) para o documento não aparecer em URL, log de proxy nem histórico.
- **Campo desconhecido no JSON é ACEITO e ignorado** (decisão: manter assim nas duas APIs).
- Nada vindo da entrada é ecoado em mensagem de erro (CPF, nome).

## 4. Resposta 200 (snake_case)

`documento` (CPF **mascarado** `***.982.247-**`; CNPJ formatado `19.182.685/0001-62`), `tipo` (`CPF`/`CNPJ`), `resultado`,
`motivos[]` (`codigo`, `texto`; sempre ao menos um), `sancoes[]` (`cadastro`, `categoria`, `orgao_sancionador`, `uf_orgao`,
`data_inicio`, `data_fim` (nulo = sem prazo), `numero_processo`, `vigente`), `pep` (`verificado`, `ativo`, `funcoes[]`),
`consultado_em` (`AAAA-MM-DD`), `fonte` (`banco`, `arquivos[]` da CGU carregados, `carregado_em`).
A origem (`fonte`) fica em cache por **5 minutos**. O CPF nunca volta completo.

## 5. Dados e carga (ferramenta MotorCredito.Carga, em C#)

- Comandos (todos com `--banco postgres|oracle|todos`): `migrar` (usuários, schemas, tabelas; repetível), `carregar` (baixa CEIS, CNEP e
  PEP e **substitui** as tabelas `sancoes` e `peps`, com o usuário da aplicação: `select`, `insert` e `delete`, sem `update`; `--pasta` usa CSVs já baixados), `comparar` (PostgreSQL x Oracle campo a campo), `testar-api --url`.
- O Portal mantém **só o arquivo mais recente** (os anteriores dão 403): a ferramenta procura o do dia e **recua até 7 dias**
  (CEIS/CNEP) ou **3 meses** (PEP). Vem em ZIP; usa só o nome do arquivo, nunca o caminho de dentro do zip.
- CSV da CGU: separador `;`, codificação **ISO-8859-1 (latin-1)**, não UTF-8. Cada linha é validada; arquivo vazio ou fora do layout é
  rejeitado inteiro.
- **Carga atômica por banco**: se algo falhar, nada é gravado e os dados anteriores continuam valendo. A API não precisa reiniciar.
- Cada registro leva `arquivo_origem` (nome do CSV), `origem_carga` (marca da aplicação) e `carregado_em`.
- Tabelas: `sancoes` (CEIS + CNEP; chave única `cadastro` + `codigo_sancao`; índice em `documento`; colunas do CSV: tipo de pessoa,
  documento, nomes, razão social, processo, categoria, valor da multa (só CNEP), datas, publicação, órgão sancionador, UF, esfera,
  fundamentação, observações...) e `peps` (`cpf_mascarado`, `cpf_miolo`, `nome`, função, órgão, datas de exercício e carência;
  índice em `cpf_miolo` + `nome`). CPF dos sancionados vem completo da CGU; documento malformado na fonte é gravado como veio.
- Agendamento sugerido: todo dia às 7h (Agendador de Tarefas do Windows). **Ainda não agendado.**
- Tempos típicos da carga (160 mil linhas): ~10 s no PostgreSQL, 15 a 45 s no Oracle.

## 6. Banco

- PostgreSQL: banco `motor_credito`, schema próprio da API, usuário de aplicação **sem superusuário, sem criar banco nem papel**.
  Sessão da API **somente leitura** (`default_transaction_read_only`) com `statement_timeout`; sem Kerberos (`GssEncryptionMode.Disable`).
- Oracle: banco plugável `XEPDB1` (**nunca o CDB `XE`**); schema dono **sem login** (`NO AUTHENTICATION`); usuário da aplicação só com
  `CREATE SESSION` e privilégios de objeto, acessando por **sinônimos**.
- **Nomes na recriação de teste (decisão de 12/11/2026): sufixo `_2` para não colidir com a API antiga**, que continua usando os nomes originais.

  | | API antiga (não mexer) | Recriação em DotNet_2 |
  |---|---|---|
  | PostgreSQL, banco | `motor_credito` | `motor_credito` (o mesmo banco, schema novo) |
  | PostgreSQL, schema | `credito_net` | `credito_net_2` |
  | PostgreSQL, usuário da aplicação | `motor_app_net` | `motor_app_net_2` |
  | Oracle, schema dono (sem login) | `MOTOR_CREDITO_NET` | `MOTOR_CREDITO_NET_2` |
  | Oracle, usuário da aplicação | `MOTOR_APP_NET` | `MOTOR_APP_NET_2` |

  Oracle continua no `XEPDB1`. Em ambos os bancos o dono do schema e o usuário da aplicação continuam separados.
- Senhas só no `.env` (fora do git). Consultas sempre parametrizadas. A API só lê as listas; quem as grava é a ferramenta `carregar`, com o usuário da aplicação (decisão do item 6, 13/11/2026: opção B).
- Configuração principal (`__` separa seções): `Api__ApiKey` (obrigatória, mínimo 32 caracteres), `Api__AllowedHosts__0..`,
  `Api__AllowedOrigins__0..`, `Api__EnableDocs` (padrão false; desligar em produção), `Api__RequestTimeoutSeconds` (10),
  `Api__RateLimitPorMinuto` (60), `Bancos__Provedor`, `Bancos__Postgres__*`, `Bancos__Oracle__*`, `Admin__*` (só para `migrar` e `comparar`; a carga usa `Bancos__*`).

## 7. Contrato HTTP

- **Autenticação**: header `X-API-Key`, comparada em tempo constante. Sem chave ou errada: 401 com `WWW-Authenticate: APIKey`.
  **Sem chave, até rota inexistente e método errado dão 401** (a API não revela quais rotas existem).
- **Erros em RFC 9457** (`application/problem+json`) com **exatamente** `title`, `status`, `code` e `request_id` (sem `type`, sem `traceId`).
  Decidir pelo `code`, nunca pelo `title`. Mensagens em português.

| HTTP | `code` | Quando |
|---|---|---|
| 400 | `requisicao_invalida` | JSON ilegível: sintaxe, `NaN`/`Infinity`, UTF-8 inválido, aninhamento acima de **64 níveis** |
| 401 | `http_401` | Sem chave ou chave errada (inclusive rota/método inexistente sem chave) |
| 404 / 405 | `http_404` / `http_405` | Rota inexistente / método errado (com chave válida) |
| 413 | `corpo_muito_grande` | Corpo acima de 4 KB |
| 422 | `entrada_invalida` | Documento inválido, nome com controle, **JSON válido com forma errada** (tipo errado, lista ou texto no lugar do objeto, `null`), corpo vazio |
| 429 | `limite_excedido` | Limite por IP (60/min), com `Retry-After: 60` |
| 500 | `erro_interno` | Erro inesperado (stack trace só no log, nunca na resposta) |
| 503 | `banco_indisponivel` | Banco fora, senha errada, pool esgotado |
| 504 | `banco_timeout` / `timeout` | Consulta lenta no banco / requisição lenta demais |

- Repetir só 429, 503 e 504, com espera crescente; nunca 400, 413 e 422. Na dúvida (503/504), **não aprovar a venda sem triagem**: tratar como pendente.
- Headers em **toda** resposta, inclusive as de erro: `X-Request-ID`, `Cache-Control: no-store`, `X-Content-Type-Options: nosniff`,
  `X-Frame-Options: DENY`, `Strict-Transport-Security`. Os headers são definidos em `OnStarting` (o tratador de exceções do
  ASP.NET faz `Response.Clear()` e apagaria os definidos antes).
- **Health** (sem autenticação e **fora do rate limit**, porque as probes do Kubernetes saem do IP do nó): `/health/live` = `{"status":"ok"}`
  (não consulta o banco); `/health/ready` = `{"status":"ok","banco":"postgres|oracle"}` ou 503 `{"status":"banco_indisponivel"}`
  (`Degraded` também é 503). `/metrics` (Prometheus): só na rede interna. `/openapi/v1.json` e `/scalar`: só com `EnableDocs`.
- Limites: 60 req/min por IP; 10 s por requisição; 5 s por consulta ao banco; 4 KB de corpo.
- **JSON estrito mas tolerante a campo extra**: tipos respeitados (sem converter `"2"` em número), obrigatório ausente e `null` = 422.
  Detalhe técnico: `JsonReaderException` (sintaxe) = 400, as demais = 422; um middleware mede a profundidade e valida o UTF-8.
- O ambiente de teste da API roda como **Production** (o padrão do `WebApplicationFactory` é Development e escondeu um erro real).

## 8. Movimentação e log de erros (decisões de 12/11/2026)

> **Atualizado em 13/11/2026:** o identificador deixou de ser `ano` + contador e passou a ser um **ULID** gerado na API (sem banco no caminho da resposta); a gravação é uma só, em segundo plano, com a resposta e o erro na mesma transação; sem tabela de contador nem funções de numeração; a aplicação só tem `INSERT`. O texto abaixo descreve a versão 1; vale o que está nesta nota e na skill `criar-api` (módulo `movimentacao`, versão 2).

- Cada requisição **com chave de API válida** (menos `/health/*` e `/metrics`) é gravada na tabela `movimentacao`: o que chegou (qualquer
  formato, como texto) e, na **mesma linha**, o que a API respondeu. Chave: **`ano` + `requisicao`**; `requisicao` é um contador que
  **volta a 1 todo ano** (ano no horário de Brasília). Header `X-Requisicao: 2026-000123`.
- Colunas: `metodo`, `rota`, `requisicao_conteudo`, `requisicao_em`, `situacao_requisicao`, `status_http`, `resposta_conteudo`,
  `resposta_em`, `situacao_resposta`. Situações: requisição `OK` = aceita, `ER` = recusada (4xx); resposta `OK` = 2xx, `ER` = 4xx e 5xx
  (uma falha 5xx é `OK` na requisição e `ER` na resposta).
- **CPF mascarado** no conteúdo gravado (requisição e resposta), em qualquer formato. **O nome é gravado como veio** (decisão do usuário).
  A chave de API nunca é gravada. Conteúdo até **32 KB** (cortado com aviso); corpo acima do limite da API grava só o tamanho.
- **Sem chave válida: 401 e nada gravado** (nem movimentação, nem log de erros): varreduras não enchem o banco.
- Numeração e chegada gravadas **num único comando do banco** (função no PostgreSQL, procedure no Oracle; `SECURITY DEFINER`/direitos do dono),
  sem repetir número em concorrência. A API espera o banco por **1 s**: sem número, a requisição segue **sem movimentação e sem header**.
- A **resposta** é gravada em segundo plano (fila de 1000, lotes de 100; ao desligar grava o que sobrou, até 5 s). Com o banco fora, a fila
  enche e as respostas a mais se perdem (aviso no log). Uma resposta já gravada nunca é sobrescrita.
- O usuário da aplicação só tem **INSERT** em `movimentacao` e `log_erros` (não lê, não altera, não apaga). Consulta e limpeza: administrador.
- `log_erros`: toda resposta 4xx/5xx, só metadados (`ano`, `requisicao` como **primeiras colunas, obrigatórias**, `request_id`, método, rota,
  status, `code`, título, tipo da exceção só no 500, duração, ambiente). **Nunca** CPF, CNPJ, nome ou corpo. O usuário da aplicação **só insere**.
  Erro sem número de requisição **não é gravado** (só o log da aplicação). Tabela antiga guardada como `log_erros_v1`.
- **Custo medido** (PostgreSQL, notebook de 2 núcleos): a vazão máxima do .NET caiu de ~760 para ~530 req/s (-31%; com 4 clientes, -43%).
- Retenção (exemplo: 90 dias, apagando o log de erros antes da movimentação): **comando só documentado, não agendado**; prazo e acesso ainda
  precisam ser definidos por você (LGPD).

## 9. Segurança e LGPD

- Documento sempre por POST; CPF nunca volta completo e não aparece em log. Logs em JSON sem dados pessoais nem segredos; o objeto de erro
  do driver (que pode ter a senha) nunca é registrado, só o tipo.
- Privilégio mínimo no banco; segredos só no `.env` ou variáveis de ambiente. CORS e hosts permitidos configuráveis; `ForwardedHeaders` só com
  proxies confiáveis.
- Cruzar as listas com CPF de clientes é tratamento de dado pessoal: registrar a finalidade (prevenção à fraude e PLD). Decisão automatizada dá
  direito de revisão: por isso a resposta traz motivos e fonte.

## 10. Container, Kubernetes e CI

- **Docker**: imagem `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra`, usuário não root, **porta 8080**, sem `HEALTHCHECK` (a imagem
  não tem curl; quem verifica são as probes). `.dockerignore`, arquivo `.env.docker` fora do git. Acesso ao banco do Windows por `host.docker.internal`.
- **Kubernetes** (Kustomize): `k8s/base` (Deployment, Service, HPA, PDB, NetworkPolicy) e overlays `local` e `producao`. Probes em `/health/live` e
  `/health/ready`. Validado no Docker Desktop em modo **kind** (a imagem precisa ser importada no nó). HPA, PDB, NetworkPolicy e o overlay de
  produção **nunca foram validados**.
- **CI** (GitHub Actions, branches `dev` e `staging`): `dotnet format --verify-no-changes`, build com avisos como erro, testes sem os de banco real
  (`--filter "Categoria!=Bancos"`), pacotes vulneráveis (inclusive transitivos). Fluxo: trabalho na `dev`, pull request `dev` -> `staging` com merge commit,
  depois a `dev` avança até a `staging`. **Não existe CD ainda.**
- Pacotes: versões centralizadas no `Directory.Packages.props`, com a correção da dependência vulnerável do driver Oracle.

## 11. Decisões que ainda são suas (nada disso está resolvido)

- Política de triagem provisória (seção 2): validar com risco/compliance. PF ou PJ, quem é o dono do risco, estudo ou produção.
- Retenção e acesso aos dados da movimentação; mascarar ou não o nome (hoje: não).
- Custo da movimentação: aceitar ou criar conexão própria para essas escritas com `synchronous_commit = off`.
- CD, carga diária agendada, HPA/PDB/NetworkPolicy/produção, proteção de branch e Dependabot.
- API Serasa (outro projeto): nunca chamou a Serasa real; faltam credenciais e o ajuste do formato.

## 12. O que decidimos ao longo do trabalho (histórico resumido)

1. Formato de erro único nas duas APIs: **RFC 9457** com 4 campos fixos (o do .NET venceu o do Python). 429 = `limite_excedido`.
2. `WWW-Authenticate: APIKey`; 401 sem chave em rota inexistente; health em JSON nas duas.
3. JSON: corpo ilegível = 400, forma errada = 422; limite de 64 níveis; `NaN`/`Infinity` e UTF-8 inválido = 400; campo extra aceito.
4. Log de erros por schema (não compartilhado), todos os 4xx e 5xx, só INSERT para a aplicação.
5. Movimentação com `ano` + `requisicao`, só com chave válida, CPF mascarado, nome como veio.
6. Entrada em String ou JSON na skill criar-api: **sem escolha = JSON**.
7. A linguagem de uma API nova seria C#; Go foi proposto e recusado.
8. Testes em C# para trabalho .NET, sem Python; python e .NET não se misturam nos templates.
9. O repositório Skills (público) fica **sem CI**; validação manual antes de cada push.
