# Motor de Crédito .NET 2 – Documentação de uso

Recriação de **teste** da API de triagem de sanções e PEP, feita do zero com as skills `criar-api`, `docker`, `kubernetes` e as
de banco, a partir de `REGRAS-E-DECISOES.md`. Versão 0.1.0. As decisões tomadas na recriação estão em `DECISOES-DO-TESTE.md`.

## Sumário

1. Para que serve
2. Como funciona
3. Começando em 5 minutos
4. Referência da API
5. Regras de negócio
6. Erros
7. Exemplos de integração
8. Operação
9. Configuração
10. Segurança e LGPD
11. Limitações
12. Solução de problemas

## 1. Para que serve

Recebe um **CPF ou CNPJ** (e, para CPF, o nome) e consulta as listas públicas da CGU gravadas no banco:
**CEIS** e **CNEP** (empresas e pessoas sancionadas) e **PEP** (pessoas expostas politicamente). Responde `liberar`, `revisar` ou
`bloquear`, sempre com os motivos.

Usos: primeira regra eliminatória de uma análise de crédito, PLD/KYC, cadastro de clientes e fornecedores, validação de documento.

Fontes: Portal da Transparência (CGU). CEIS e CNEP são diários, PEP é mensal. Dados carregados em 12/11/2026:
`20261112_CEIS.csv`, `20261112_CNEP.csv` e `202609_PEP.csv` (25.501 sanções e 133.875 registros de PEP).

## 2. Como funciona

```
cliente --POST /api/v1/triagem--> API (.NET 10) --consulta--> PostgreSQL ou Oracle (listas da CGU)
                                      |
                                      +--grava--> movimentacao e log_erros (mesmo banco)
```

- A API **não chama o governo**: lê as tabelas locais. A ferramenta `MotorCredito.Carga` baixa os arquivos e substitui as tabelas.
- O banco é escolhido por `Bancos__Provedor` (`postgres` ou `oracle`); as tabelas e os dados são os mesmos nos dois (conferido
  com `comparar`).
- Cada requisição com chave de API válida ganha um **ULID** (26 caracteres, gerado na API, sem banco) e é gravada na `movimentacao`, com a resposta e o erro (se houver), em segundo plano; o ULID volta no header `X-Requisicao`.

## 3. Começando em 5 minutos

Pré-requisitos: .NET SDK 10, PostgreSQL e/ou Oracle locais, arquivo `.env` na raiz (modelo na seção 9).

```bash
# 1) usuários, schemas, tabelas e permissões (administrador do .env); pode repetir. A carga (2) usa o usuário da aplicação
dotnet run --project src/MotorCredito.Carga -- migrar
# 2) baixar CEIS, CNEP e PEP da CGU e gravar (substitui os dados; atômico por banco)
dotnet run --project src/MotorCredito.Carga -- carregar
# 3) conferir que os dois bancos têm os mesmos dados
dotnet run --project src/MotorCredito.Carga -- comparar
# 4) subir a API
dotnet run --project src/MotorCredito.Api --urls http://localhost:5080
# 5) casos reais de ponta a ponta (escolhidos nos dados carregados)
dotnet run --project src/MotorCredito.Carga -- testar-api --url http://localhost:5080
```

Chamada de exemplo:

```bash
curl -X POST http://localhost:5080/api/v1/triagem \
  -H "X-API-Key: $Api__ApiKey" -H "Content-Type: application/json" \
  -d '{"documento":"529.982.247-25","nome":"Maria de Teste"}'
```

Resposta real (12/11/2026):

```json
{"documento":"***.982.247-**","tipo":"CPF","resultado":"liberar",
 "motivos":[{"codigo":"OK","texto":"Nenhuma restrição nas listas consultadas."}],
 "sancoes":[],"pep":{"verificado":true,"ativo":false,"funcoes":[]},"consultado_em":"2026-11-12",
 "fonte":{"banco":"postgres","arquivos":["202609_PEP.csv","20261112_CEIS.csv","20261112_CNEP.csv"],
          "carregado_em":"2026-11-12T21:53:14.635717+00:00"}}
```

## 4. Referência da API

### Autenticação

Header `X-API-Key` com a chave configurada (`Api__ApiKey`, mínimo 32 caracteres), comparada em tempo constante. Sem chave ou
com chave errada: 401 com `WWW-Authenticate: APIKey`. Sem chave, até rota inexistente e método errado respondem 401.

### `POST /api/v1/triagem`

Corpo JSON de até **4 KB**:

| Campo | Tipo | Obrigatório | Regras |
|---|---|---|---|
| `documento` | texto | sim | CPF ou CNPJ, com ou sem `.` `-` `/` e espaços; 11 a 20 caracteres; dígitos verificadores conferidos; aceita o CNPJ alfanumérico |
| `nome` | texto | não | até 200 caracteres, sem caracteres de controle; vazio ou só espaços = não informado; ignorado em CNPJ |

Campo desconhecido no JSON é **aceito e ignorado**. Resposta 200 (campos em snake_case):

| Campo | Conteúdo |
|---|---|
| `documento` | CPF mascarado (`***.982.247-**`) ou CNPJ formatado (`11.222.333/0001-81`) |
| `tipo` | `CPF` ou `CNPJ` |
| `resultado` | `liberar`, `revisar` ou `bloquear` |
| `motivos[]` | `codigo` e `texto`; sempre ao menos um |
| `sancoes[]` | todas as sanções do documento (vigentes primeiro): `cadastro`, `categoria`, `orgao_sancionador`, `uf_orgao`, `data_inicio`, `data_fim` (nulo = sem prazo), `numero_processo`, `vigente` |
| `pep` | `verificado`, `ativo`, `funcoes[]` (descrição de cada registro encontrado) |
| `consultado_em` | data da consulta (horário de Brasília) |
| `fonte` | `banco`, `arquivos[]` carregados e `carregado_em` (cache de 5 minutos) |

### Outros endpoints

| Rota | Auth | O que faz |
|---|---|---|
| `GET /health/live` | não | `{"status":"ok"}`; não consulta o banco |
| `GET /health/ready` | não | `{"status":"ok","banco":"postgres"}` ou 503 `{"status":"banco_indisponivel"}` |
| `GET /metrics` | não | Prometheus (deixe só na rede interna) |
| `GET /openapi/v1.json`, `/scalar` | não | só com `Api__EnableDocs=true` (desligado por padrão) |

### Limites

60 requisições por minuto por IP (health e metrics ficam de fora); 10 s por requisição; 5 s por consulta ao banco; corpo de 4 KB.

### Headers de resposta

Em **toda** resposta, inclusive as de erro: `X-Request-ID`, `Cache-Control: no-store`, `X-Content-Type-Options: nosniff`,
`X-Frame-Options: DENY`, `Strict-Transport-Security`. Nas rotas com movimentação, `X-Requisicao` (ULID, ex.: `01JC4M7Z9Q8T2X5V6B3N0WYKHD`): informe-o ao suporte.

## 5. Regras de negócio

A política é **provisória**: validar com risco/compliance antes de produção.

| Código | Situação | Efeito |
|---|---|---|
| `S01` | sanção vigente no CEIS ou CNEP | `bloquear` |
| `S02` | só sanções encerradas | informativo |
| `P01` | PEP em exercício ou na carência | `revisar` |
| `P02` | foi PEP, carência encerrada | informativo |
| `P03` | CPF sem nome: PEP não verificado | informativo |
| `OK` | nada encontrado | `liberar` |

- Vale a decisão mais grave (`bloquear` > `revisar` > `liberar`); todos os motivos aparecem.
- Sanção vigente: já começou (ou sem data de início) e não terminou (sem data final = sem prazo; o último dia conta).
- PEP ativo: com fim de carência, vale `fim da carência >= hoje`; sem ele, sem fim de exercício ou fim de exercício `>= hoje`.
  Datas não informadas contam como ativas (cautela).
- PEP só para CPF. A CGU publica o CPF mascarado: casa-se pelos 6 dígitos visíveis **e** pelo nome (maiúsculas, sem acentos,
  espaços simples). O nome precisa ser completo; homônimos são possíveis, por isso PEP vai para `revisar`.

## 6. Erros

Formato RFC 9457 (`application/problem+json`) com exatamente `title`, `status`, `code` e `request_id`. Decida pelo `code`.

| HTTP | `code` | Quando |
|---|---|---|
| 400 | `requisicao_invalida` | JSON ilegível: sintaxe, `NaN`/`Infinity`, UTF-8 inválido, mais de 64 níveis |
| 401 | `http_401` | sem chave, chave errada, rota ou método inexistente sem chave |
| 404 / 405 | `http_404` / `http_405` | rota inexistente / método errado (com chave) |
| 413 | `corpo_muito_grande` | corpo acima de 4 KB |
| 422 | `entrada_invalida` | documento inválido, nome inválido, forma errada (tipo, `null`, lista, corpo vazio) |
| 429 | `limite_excedido` | limite por IP; `Retry-After: 60` |
| 500 | `erro_interno` | erro inesperado (detalhes só no log) |
| 503 | `banco_indisponivel` | banco fora ou senha errada |
| 504 | `banco_timeout` / `timeout` | consulta ou requisição lenta |

Repita só 429, 503 e 504, com espera crescente; nunca 400, 413 e 422. Na dúvida, não aprove sem a triagem.
Toda resposta 4xx/5xx de requisição com chave válida também vai para a tabela `log_erros` (só metadados, nunca documento nem nome).

## 7. Exemplos de integração

**Postman**: `postman/Motor-de-Credito-Net-2.postman_collection.json`. Preencha `api_key` (variável secreta, vem vazia) e confira
`base_url` (padrão `http://localhost:5080`). Os pedidos de erro e de saúde trazem testes de status e de formato.

**curl**: ver a seção 3.

**C#**:

```csharp
using var http = new HttpClient { BaseAddress = new Uri("http://localhost:5080") };
http.DefaultRequestHeaders.Add("X-API-Key", chave);
var resposta = await http.PostAsJsonAsync("/api/v1/triagem", new { documento = "52998224725", nome = "Maria de Teste" });
if (resposta.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout)
{
    // repetir com espera crescente; na dúvida, tratar como pendente
}
```

## 8. Operação

### Migração e carga

`MotorCredito.Carga` (C#): `migrar` (usuários, schemas, tabelas, funções e permissões; repetível), `carregar` (baixa CEIS, CNEP e PEP
e substitui as tabelas `sancoes` e `peps` numa única transação por banco, **com o usuário da aplicação** (`Bancos__*`), não com o administrador; `--pasta` usa CSV já baixados), `comparar` (PostgreSQL x Oracle linha a linha),
`testar-api` (casos reais) e `comparar-apis` (os mesmos casos em duas APIs no ar). `--banco postgres|oracle|todos`.
O Portal mantém só o arquivo mais recente; a ferramenta recua até 7 dias (CEIS/CNEP) ou 3 meses (PEP). Carga medida: PostgreSQL ~4 s,
Oracle ~44 s. **Agendamento diário ainda não existe.**

### Container

```bash
docker build -t motor-credito-net-2:0.1.0 .
docker run -d --name motor-net2 -p 8081:8080 --env-file .env.docker motor-credito-net-2:0.1.0
```

Imagem `aspnet:10.0-noble-chiseled-extra`, 258 MB, usuário não root (UID 1654), porta 8080, sem `HEALTHCHECK` (as probes do Kubernetes
verificam). `.env.docker` sai do `.env` sem os administradores e com o banco em `host.docker.internal`. Verificado em 12/11/2026:
hadolint sem avisos (só a informação DL3066 do template), trivy HIGH/CRITICAL corrigíveis = 0, `health/ready` 200 com PostgreSQL e com Oracle,
chamada real 200, sem chave 401, banco fora: `live` 200, `ready` 503 e `503 banco_indisponivel` sem o container cair.

### Kubernetes

`k8s/base` (Deployment, Service, HPA, PDB, NetworkPolicy) e overlays `local` e `producao`. O overlay local passa no
`kubectl kustomize` e no kubeconform (6 recursos válidos). **Não foi aplicado em cluster** (o Kubernetes do Docker Desktop estava
desligado). O rate limit é por pod: com N réplicas o limite real é N vezes o valor configurado.

### Movimentação e retenção

Tabelas `movimentacao` (chave `requisicao_id`, o ULID) e `log_erros` (gravado na mesma transação), com CPF mascarado; sem contador. Não há numeração sequencial por ano.
A aplicação só insere em `movimentacao` e `log_erros` (sem leitura, alteração ou exclusão). O nome é gravado como veio. **Defina o prazo de retenção**
(exemplo no cabeçalho de `banco/*/02_movimentacao.sql`) e quem acessa as tabelas.

### Testes

```bash
dotnet test MotorCredito.slnx --filter "Categoria!=Bancos"   # sem banco: 197 testes
dotnet test tests/MotorCredito.Api.BancosTests               # com PostgreSQL e Oracle reais: 10 testes
```

## 9. Configuração

Variáveis de ambiente ou `.env` na raiz (`__` separa seções; variável já definida tem prioridade; o `.env` nunca é versionado):

| Chave | Padrão | Uso |
|---|---|---|
| `Api__ApiKey` | (obrigatória) | chave do header `X-API-Key`, mínimo 32 caracteres |
| `Api__AllowedHosts__0..` | `localhost` | hosts aceitos (inclua `localhost` para as probes e o nome do Service) |
| `Api__AllowedOrigins__0..` | vazio | origens CORS |
| `Api__EnableDocs` | `false` | OpenAPI e Scalar |
| `Api__RequestTimeoutSeconds` | `10` | tempo máximo de uma requisição |
| `Api__RateLimitPorMinuto` | `60` | por IP |
| `Bancos__Provedor` | `postgres` | `postgres` ou `oracle` |
| `Bancos__Postgres__Host/Porta/Banco/Usuario/Senha/SslMode/Esquema` | `127.0.0.1`, `5432`, `motor_credito`, `motor_app_net_2`, vazio, `Prefer`, `credito_net_2` | conexão da aplicação |
| `Bancos__Oracle__DataSource/Usuario/Senha` | `localhost:1521/XEPDB1`, `MOTOR_APP_NET_2`, vazio | conexão da aplicação (senha de até 30 caracteres) |
| `Admin__Postgres__Usuario/Senha`, `Admin__Oracle__Usuario/Senha` | `postgres`, `SYSTEM` | só `migrar`, `comparar` e os testes de banco (criam objetos e conferem dados); a carga e a API não usam |

Objetos de banco: PostgreSQL schema `credito_net_2`, usuário `motor_app_net_2`; Oracle schema dono `MOTOR_CREDITO_NET_2` (sem login),
usuário `MOTOR_APP_NET_2` (acessa por sinônimos). Em `sancoes` e `peps` o usuário da aplicação tem `select`, `insert` e `delete` (a API só lê; quem grava é a ferramenta `carregar`, com este mesmo usuário); nunca `update`.

## 10. Segurança e LGPD

- CPF nunca volta completo nem aparece em log; entrada e erros não ecoam documento nem nome; documento sempre por POST.
- A chave de API nunca é gravada. Sem chave válida: 401 e nada é gravado no banco.
- Privilégio mínimo no banco: a aplicação lê as listas (a carga também as apaga e insere, sem `update`) e só insere em `movimentacao` e `log_erros`. A sessão da API
  é somente leitura e as consultas são parametrizadas. Segredos só no `.env`/variáveis de ambiente.
- Cruzar as listas com CPF de clientes é tratamento de dado pessoal: registre a finalidade (prevenção à fraude e PLD). A resposta traz
  motivos e fonte para o direito de revisão de decisões automatizadas.
- A movimentação guarda o **nome** como veio e o CPF mascarado: defina retenção e acesso.

## 11. Limitações

- Política de triagem provisória; PEP por 6 dígitos do CPF mais nome (homônimos possíveis).
- Dados só são tão novos quanto a última carga (sem agendamento ainda).
- Rate limit por pod e em memória.
- Gravar a chegada da requisição custa vazão (medida na API antiga: -31% a -43%); nesta recriação não foi medido.
- Kubernetes e CD não validados; sem integração com a Serasa.

## 12. Solução de problemas

| Sintoma | Causa provável |
|---|---|
| 401 em tudo | `X-API-Key` ausente ou diferente de `Api__ApiKey` |
| Pod reiniciando com probes 400 | `Api__AllowedHosts` sem `localhost` |
| 503 `banco_indisponivel` | banco fora, usuário ou senha errados no `.env`, schema não migrado |
| Gravações da movimentação não aparecem | tabelas não migradas, banco fora ou fila cheia (a resposta sai normalmente; há aviso no log). O `X-Requisicao` sempre existe |
| `ORA-00972` ao migrar | senha do Oracle com mais de 30 caracteres |
| `ORA-12541`/`50201` | listener do Oracle fora do ar |
| Carga falha com "colunas ausentes" | a CGU mudou o layout do CSV |
| Build falha com `MSB3021` | a API ainda está rodando e bloqueia o executável |
