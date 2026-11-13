# Motor de Crédito .NET 2 (recriação de teste)

API em ASP.NET Core (.NET 10) que recebe um CPF ou CNPJ (e o nome, para PEP) e consulta as listas da CGU (CEIS, CNEP e PEP)
gravadas em PostgreSQL ou Oracle. Responde `liberar`, `revisar` ou `bloquear` com os motivos. Recriada do zero a partir das
skills `criar-api`, `docker` e `kubernetes` e de `REGRAS-E-DECISOES.md`; é um **teste**, a API antiga continua como referência.

| Arquivo | O que é |
|---|---|
| `REGRAS-E-DECISOES.md` | especificação: regras de negócio e decisões herdadas |
| `PROMPT.md` | o pedido que originou esta recriação |
| `DECISOES-DO-TESTE.md` | o que foi decidido na recriação, diferenças com a antiga e o que precisa da sua decisão |
| `DOCUMENTACAO.md` | uso, referência, operação, configuração |

```
src/MotorCredito.Api     a API (POST /api/v1/triagem, health, métricas) com documentos, bancos e movimentação
src/MotorCredito.Carga   ferramenta: migrar, carregar, comparar, testar-api, comparar-apis
banco/                   scripts SQL (PostgreSQL e Oracle), embutidos na ferramenta
tests/                   UnitTests, IntegrationTests (API em memória), Carga.Tests, BancosTests (bancos reais)
k8s/  Dockerfile  postman/  .github/workflows/ci.yml
```

Começo rápido (precisa do `.env`, ver `DOCUMENTACAO.md`, seção 9):

```bash
dotnet run --project src/MotorCredito.Carga -- migrar
dotnet run --project src/MotorCredito.Carga -- carregar
dotnet run --project src/MotorCredito.Api --urls http://localhost:5080
dotnet test MotorCredito.slnx --filter "Categoria!=Bancos"
```
