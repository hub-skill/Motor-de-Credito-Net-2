Estamos em C:\Felipe\Programacao\DotNet_2. Esta pasta é um TESTE: quero recriar do zero a API "Motor de Crédito"
em C# (.NET) usando as skills criar-api, docker, kubernetes, postgresql-naming-schemas, oracle-naming-sinonimos
e conexao-*. No futuro, outras APIs diferentes derivarão desta.

Contexto:
- A API antiga (C:\Felipe\Programacao\DotNet\APIs\Motor-de-Credito-Net) CONTINUA como está. Não altere nada nela,
  não use o schema nem os usuários de banco dela e não copie código dela: ela serve só de referência para comparar o resultado.
- Todas as regras e decisões estão em DotNet_2\REGRAS-E-DECISOES.md. Leia o arquivo inteiro antes de começar e
  trate-o como a especificação. Se algo faltar ou for ambíguo, pergunte; não invente.

Faça:
1. Use a skill criar-api com entrada JSON (padrão), C#, e inclua os módulos de movimentação e log de erros.
2. Implemente as regras de triagem, a validação do documento, as respostas e o contrato de erros do arquivo.
3. Crie a ferramenta de carga em C# (migrar, carregar, comparar, testar-api), para PostgreSQL e Oracle.
4. Use schemas e usuários NOVOS, para não colidir com os da API antiga (a tabela está na seção 6 do arquivo de regras):
   PostgreSQL: banco motor_credito, schema credito_net_2, usuário motor_app_net_2.
   Oracle (XEPDB1): schema dono MOTOR_CREDITO_NET_2 (sem login) e usuário MOTOR_APP_NET_2.
   Peça as senhas pelo .env (nunca no chat) e nunca as imprima nem commite.
5. Dockerfile e manifestos do Kubernetes pelas skills docker e kubernetes.
6. Testes (unitários, integração e de banco), format e build sem avisos, e cobertura mínima exigida pelo template.
7. No fim, rode a API nova e a antiga lado a lado com os mesmos casos de teste e me mostre as diferenças,
   com uma explicação para cada uma.
8. Registre em DotNet_2\DECISOES-DO-TESTE.md toda decisão que você tomar e todo ponto em que se afastou do arquivo de regras.

Não faça commit nem push sem eu pedir. Mostre o plano antes de começar a implementar.
