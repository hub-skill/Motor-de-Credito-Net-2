# Decisões da recriação de teste (12/11/2026)

Registro do que foi decidido sem perguntar, como o `PROMPT.md` pediu, e do que **precisa da sua decisão** (seção 1).

## 0. Respondido por você (12/11/2026) e aplicado

| Assunto | Resposta | O que foi feito |
|---|---|---|
| `Cache-Control: no-store` no template (item 3 abaixo) | sim | Corrigido nos **dois templates** da skill `criar-api` (.NET: `SecurityHeadersMiddleware`; Python: middleware do `main.py`), com asserções nos testes genéricos |
| Corpo do `/health/ready` (item 5 abaixo) | opção 2: o módulo `bancos` traz o corpo com banco | Módulo `bancos` (.NET e Python) ganhou `RespostaDeSaudeComBanco` / `app/bancos/saude.py`, testes de unidade, passo no LEIAME e Postman; o template base segue genérico. Esta API passou a usar o arquivo do módulo |
| Campo desconhecido (item 4) | mantém | nada mudou: aceito e ignorado |
| Quem carrega os dados (item 6) | opção B (confirmada em 13/11/2026) | **aplicada**: a ferramenta `carregar` usa o usuário da aplicação, que tem `select`, `insert` e `delete` em `sancoes`/`peps` (sem `update`); o administrador só roda `migrar`. Testes de permissão e documentação atualizados; carga real feita nos dois bancos |
| Docker Desktop ligado (item 11) | mantém | continua ligado |
| Demais itens | "mantenha como está" | sem mudança |

A skill foi revalidada nos 6 cenários (Python base, com JSON e com todos os módulos; .NET idem): Python 55, 75 e 170 testes, ruff, format, mypy e bandit limpos, cobertura 100%, 100% e 94,02%; .NET 0 avisos, format sem mudanças, 48, 68 e 154 testes. A cópia em `Caude_Skill\API` foi sincronizada, **sem commit nem push**.

## 1. Precisava da sua decisão (itens ainda em aberto: 1, 2, 7, 8, 9, 10, 12, 14)

| # | Assunto | O que fiz | Alternativa / pergunta |
|---|---|---|---|
| 1 | `pep.funcoes` repete a mesma função | a nova lista **uma função por registro de PEP encontrado** (ex.: `["VEREADOR","VEREADOR"]`); a antiga mostra cada função uma vez, em ordem alfabética (visto na comparação) | Quer sem repetição e ordenada, como na antiga? É uma linha em `TriagemService`. Escolhi não alinhar à antiga para o teste mostrar o que sai só da especificação |
| 2 | Ordem de `sancoes[]` | vigentes primeiro, depois encerradas, e dentro de cada grupo por `cadastro` e `codigo_sancao` | A antiga devolve na ordem do banco. Qual é o contrato? |
| 3 | `Cache-Control: no-store` faltava no **template da skill** | está no arquivo de regras (seção 7) e a nova API não cumpria: corrigi no `SecurityHeadersMiddleware` e testei. A comparação lado a lado achou (a antiga manda) | Levar essa correção ao template `criar-api` (.NET e Python)? Não mexi na skill |
| 4 | Campo desconhecido no JSON | **aceito e ignorado** (decisão que vale nas duas APIs antigas); o módulo `entrada-json` da skill manda 422 | Manter? Se mudar, vale para as três |
| 5 | Corpo de `/health/ready` | `{"status":"ok","banco":"postgres"}` e 503 `{"status":"banco_indisponivel"}`, como no arquivo de regras; o template da skill responde `indisponivel` | Corrigir o template da skill para o formato com banco? |
| 6 | Quem carrega os dados | a ferramenta `carregar` roda como o **usuário da aplicação** (`select`, `insert`, `delete` em `sancoes`/`peps`), como na antiga | Decidido: opção B (13/11/2026) |
| 7 | Retenção e acesso à `movimentacao`/`log_erros` | só está documentado; nada agendado. Nome gravado como veio (regra herdada) | Prazo, quem consulta, se mascara o nome |
| 8 | Custo da movimentação | **não medi** nesta recriação (na antiga: -31% a -43% de vazão) | Medir? Pool de escrita próprio e `synchronous_commit=off` seguem como opção |
| 9 | Kubernetes | manifestos gerados e validados sem cluster (kustomize e kubeconform: 6 recursos válidos). **Não apliquei em cluster**: o Kubernetes do Docker Desktop está desligado e só você liga (Settings > Kubernetes) | Ligar e eu valido (rollout, probes, restart); HPA, PDB e NetworkPolicy continuam sem validação possível aqui |
| 10 | CD, carga diária agendada, política de triagem (provisória), PF/PJ, dono do risco | não feitos; mesmas pendências da antiga | — |
| 11 | O Docker Desktop estava desligado | **liguei** para validar a imagem; continua rodando, com a imagem `motor-credito-net-2:0.1.0` (258 MB) | Desligar? |
| 12 | Repositório git | rodei `git init` em `DotNet_2` (para ver o que muda); **nenhum commit**, `.env`, `.env.docker`, `k8s/overlays/local/segredos.env` e `dados-cgu/` estão ignorados | Quer o repositório, ou apago o `.git`? |
| 13 | Banco da API antiga | a comparação lado a lado gravou linhas de teste na movimentação da antiga (165 de movimentação, 75 de log de erros). Apaguei só essas (última janela de 3 horas) e zerei o contador, com a sua liberação do banco. Nada mais foi alterado nela | — |
| 14 | Senha do Oracle | limite de 30 caracteres nesta instalação (ORA-00972 com 32); a do `.env` tem 24 | Pôr no README da skill `oracle` |

## 2. Decisões tomadas e motivo

1. **Estrutura**: dois projetos em `src` (`MotorCredito.Api` e `MotorCredito.Carga`, que referencia a API), em vez de quatro (Api, Core, Dados, Carga). Os módulos da skill (documentos, bancos, movimentação) são arquivos que entram no projeto da API; a ferramenta reaproveita deles as conexões, a tradução de erros e o validador de documentos. Menos peças para um teste. Quando outras APIs derivarem desta, separar `Core` vale a pena.
2. **Nomes de banco**: PostgreSQL banco `motor_credito`, schema `credito_net_2`, usuário `motor_app_net_2`; Oracle `MOTOR_CREDITO_NET_2` (dono, sem login) e `MOTOR_APP_NET_2`. Marca de carga `origem_carga = 'DOTNET2'`.
3. **Sem restrição de unicidade em `peps`**: homônimos com o mesmo CPF mascarado existem nos dados da CGU. O `comparar` compara as linhas inteiras como multiconjuntos (uma chave parcial dava falsa diferença).
4. **Carga atômica**: apaga e grava as duas tabelas numa transação por banco; arquivo com problema é rejeitado inteiro, antes de tocar no banco. PostgreSQL com `copy` binário (~4 s); Oracle com array bind em lotes (~44 s).
5. **`.env`**: criado em `DotNet_2` com chave de API e senhas **novas** (aleatórias, nunca impressas de propósito). Dois valores de senha apareceram uma vez na saída do terminal por engano; regerei os dois antes de usá-los. Copiei do `.env` da antiga **só** os administradores e os endereços dos bancos, sem mostrar valores.
6. **Testes que tocam banco** ficam em projeto separado (`MotorCredito.Api.BancosTests`, `Categoria=Bancos`), nunca rodam no CI, e apagam o que gravam (nas rotas de teste) ao terminar.
7. **Entrada do `WebApplicationFactory` nos testes de banco**: `Program` existe na API e na ferramenta; usei um tipo público da API como ponto de entrada.
8. **Hora e data da consulta** em horário de Brasília (um teste cobre 22h em Brasília = 01h UTC do dia seguinte).
9. **CI**: `.github/workflows/ci.yml` (format, build com avisos como erro, testes sem banco, pacotes vulneráveis) nas branches `dev` e `staging`.
10. **Cobertura**: o template .NET não define mínimo; não medi cobertura.
11. **Python só em edições de texto e na geração dos SQL** (script no scratchpad, fora do projeto); toda ferramenta, teste e comparação entregues são C#.
12. **Pacote novo**: `Microsoft.Extensions.TimeProvider.Testing` (relógio falso nos testes).
13. **Postman**: coleção adaptada (rota, corpos, `X-Requisicao`, `health/ready` com banco). Não rodei no Postman (sem Node/newman): exercitei os corpos por `curl` e os mesmos casos passam no `testar-api`.

## 3. O que consultei da API antiga (lições, não código)

Segundo o seu aviso, nada foi copiado ou colado. Mesmo assim, para ser transparente, **li partes do código e da documentação da antiga** e escrevi o código novo à parte:

- Fatos da CGU que não estão no arquivo de regras: nomes das colunas dos CSV (`LayoutsCgu`), endereço e padrão de download, recuo de 7 dias/3 meses, formato de data e de valor.
- Como criar usuário e schema (`NO AUTHENTICATION`, quota), quais erros do Oracle ignorar ao reexecutar.
- Textos e regras da triagem, que eu já tinha no arquivo de regras; reli a antiga para conferir.
- **Um ponto aplicado e desfeito**: li como a antiga monta `pep.funcoes` e a ordem das sanções, alinhei a nova e, quando você avisou, **desfiz** (itens 1 e 2 acima). O `Cache-Control` não veio do código, veio da especificação e do resultado da comparação.

## 4. Comparação lado a lado (62 casos, as duas APIs no ar, PostgreSQL)

Casos: documentos reais (sanção vigente, só encerradas, PEP ativo e encerrado, com e sem nome), formatos de documento, nome (200/201 caracteres, controle, tab, vazio, nulo, tipo errado), formas de JSON (vazio, `null`, lista, texto, malformado, `NaN`, `Infinity`, 64 e 65 níveis, UTF-8 inválido, 4 KB), autenticação, rotas, métodos, health e docs. Comparados: status, tipo, campos, `code`, `title`, resultado, motivos, sanções, PEP e cabeçalhos.

- **1ª rodada: 0 de 62 iguais**, por uma causa só: `Cache-Control: no-store` (faltava na nova).
- **Depois da correção: 56 de 62 iguais.** As 6 diferenças:
  - 4 casos de PEP: só `pep.funcoes` (decisão 1 acima). Resultado, motivos e documento são iguais.
  - 2 casos de `/openapi/v1.json`: **configuração**, não código. A antiga roda com `Api__EnableDocs=true` e a nova com `false` (padrão da skill).
- Os dados das duas diferem só no dia da carga (25.501 sanções na nova), sem efeito nos casos escolhidos.

## 5. Verificações (12/11/2026)

| Verificação | Resultado |
|---|---|
| `dotnet format --verify-no-changes` | sem mudanças |
| `dotnet build` | 0 avisos, 0 erros (avisos são erro) |
| Pacotes vulneráveis (inclusive transitivos) | nenhum nos 6 projetos |
| Testes sem banco | 197 de 197 (113 unitários, 70 de integração, 14 da ferramenta) |
| Testes com PostgreSQL e Oracle reais | 10 de 10 (leitura, permissões negadas, numeração, resposta uma vez só, chave estrangeira, API de ponta a ponta) |
| Migração | repetível nos dois bancos |
| Carga | 25.501 sanções e 133.875 PEPs nos dois; `comparar`: iguais |
| `testar-api` (casos reais) | 11 de 11 com PostgreSQL e 11 de 11 com Oracle |
| Docker | build ok, usuário 1654, hadolint só com informação, trivy HIGH/CRITICAL = 0, container com PostgreSQL, com Oracle e com o banco fora (live 200, ready 503, 503 `banco_indisponivel`) |
| Kubernetes | `kubectl kustomize` do overlay local e kubeconform: 6 válidos; **sem cluster** |

## 6. Para a skill `criar-api` (achados, nada alterado nela)

- (Corrigido em 12/11) O template não mandava `Cache-Control: no-store` (a especificação pede em toda resposta).
- (Corrigido em 12/11) O corpo de `/health/ready` do template (`indisponivel`) diferia do definido no arquivo de regras: agora o módulo `bancos` traz o corpo com banco.
- O módulo `entrada-json` assume campo desconhecido = 422; os Motores aceitam e ignoram.
- Os testes do módulo `movimentacao` e do template assumem a rota de texto e `{"texto": ...}`: ao adaptar, ajustar várias asserções (corpo ≤ 4 KB para o teste de profundidade, CPF mascarado no conteúdo, `ListasFalsas` nas fábricas).
- O limite de 30 caracteres da senha do Oracle não está documentado.
