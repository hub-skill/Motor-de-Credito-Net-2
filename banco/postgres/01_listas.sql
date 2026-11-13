-- =============================================================================
-- Motor de Crédito .NET 2 (recriação de teste): listas da CGU, PostgreSQL 15+
-- Executado pela ferramenta (dotnet run --project src/MotorCredito.Carga -- migrar) como administrador, no banco
-- motor_credito. <schema> = credito_net_2 e <usuario_app> = motor_app_net_2 são trocados pela ferramenta.
-- Pode ser executado de novo: o que existe é mantido. A aplicação só LÊ estas tabelas (select).
-- =============================================================================

create schema if not exists <schema>;
comment on schema <schema> is 'Motor de crédito .NET 2: listas da CGU e movimentação da API.';

create table if not exists <schema>.sancoes (
    id bigint generated always as identity,
    cadastro varchar(10) not null,
    codigo_sancao bigint not null,
    tipo_pessoa char(1),
    documento varchar(20) not null,
    nome_sancionado varchar(200),
    nome_informado_orgao varchar(200),
    razao_social varchar(200),
    nome_fantasia varchar(100),
    numero_processo varchar(80),
    categoria varchar(120),
    valor_multa numeric(15,2),
    data_inicio date,
    data_fim date,
    data_publicacao date,
    publicacao varchar(150),
    detalhamento_publicacao text,
    data_transito_julgado date,
    abrangencia varchar(100),
    orgao_sancionador varchar(300),
    uf_orgao char(2),
    esfera_orgao varchar(20),
    fundamentacao_legal text,
    data_origem date,
    origem_informacao varchar(150),
    observacoes text,
    arquivo_origem varchar(60) not null,
    origem_carga varchar(10) not null default 'DOTNET2',
    carregado_em timestamptz not null default now(),
    constraint pk_sancoes primary key (id),
    constraint uk_sancoes_cadastro_codigo unique (cadastro, codigo_sancao),
    constraint ck_sancoes_cadastro check (cadastro in ('CEIS', 'CNEP')),
    constraint ck_sancoes_tipo_pessoa check (tipo_pessoa in ('F', 'J')),
    constraint ck_sancoes_valor_multa check (valor_multa >= 0)
);

create index if not exists idx_sancoes_documento on <schema>.sancoes (documento);

comment on table <schema>.sancoes is 'Sanções do CEIS e do CNEP (CGU/Portal da Transparência). Recarregada a cada arquivo diário.';
comment on column <schema>.sancoes.id is 'Identificador interno.';
comment on column <schema>.sancoes.cadastro is 'Lista de origem: CEIS ou CNEP.';
comment on column <schema>.sancoes.codigo_sancao is 'Código da sanção na CGU. Único por cadastro.';
comment on column <schema>.sancoes.tipo_pessoa is 'F = pessoa física, J = pessoa jurídica. Nulo quando a CGU não informa.';
comment on column <schema>.sancoes.documento is 'CPF ou CNPJ do sancionado, só dígitos e letras (CNPJ alfanumérico), como veio no arquivo.';
comment on column <schema>.sancoes.nome_sancionado is 'Nome do sancionado.';
comment on column <schema>.sancoes.nome_informado_orgao is 'Nome informado pelo órgão sancionador.';
comment on column <schema>.sancoes.razao_social is 'Razão social no cadastro da Receita (PJ).';
comment on column <schema>.sancoes.nome_fantasia is 'Nome fantasia no cadastro da Receita (PJ).';
comment on column <schema>.sancoes.numero_processo is 'Número do processo administrativo ou judicial.';
comment on column <schema>.sancoes.categoria is 'Categoria da sanção (impedimento, suspensão, inidoneidade, multa...).';
comment on column <schema>.sancoes.valor_multa is 'Valor da multa em reais (somente CNEP).';
comment on column <schema>.sancoes.data_inicio is 'Início da sanção. Nulo = já vigente.';
comment on column <schema>.sancoes.data_fim is 'Fim da sanção. Nulo = sem prazo determinado.';
comment on column <schema>.sancoes.data_publicacao is 'Data de publicação da sanção.';
comment on column <schema>.sancoes.publicacao is 'Meio de publicação (ex.: Diário Oficial).';
comment on column <schema>.sancoes.detalhamento_publicacao is 'Detalhamento do meio de publicação.';
comment on column <schema>.sancoes.data_transito_julgado is 'Data do trânsito em julgado, quando houver.';
comment on column <schema>.sancoes.abrangencia is 'Abrangência da sanção.';
comment on column <schema>.sancoes.orgao_sancionador is 'Órgão que aplicou a sanção.';
comment on column <schema>.sancoes.uf_orgao is 'UF do órgão sancionador.';
comment on column <schema>.sancoes.esfera_orgao is 'Esfera do órgão: FEDERAL, ESTADUAL ou MUNICIPAL.';
comment on column <schema>.sancoes.fundamentacao_legal is 'Fundamentação legal da sanção.';
comment on column <schema>.sancoes.data_origem is 'Data da informação na origem.';
comment on column <schema>.sancoes.origem_informacao is 'Órgão de origem da informação.';
comment on column <schema>.sancoes.observacoes is 'Observações da CGU.';
comment on column <schema>.sancoes.arquivo_origem is 'Nome do arquivo de onde a linha foi carregada (ex.: 20261110_CEIS.csv).';
comment on column <schema>.sancoes.origem_carga is 'Aplicação que gravou a linha (DOTNET2 = recriação de teste).';
comment on column <schema>.sancoes.carregado_em is 'Momento da carga.';

create table if not exists <schema>.peps (
    id bigint generated always as identity,
    cpf_mascarado varchar(20) not null,
    cpf_miolo varchar(20) not null,
    nome varchar(100) not null,
    sigla_funcao varchar(10),
    descricao_funcao varchar(100),
    nivel_funcao varchar(10),
    nome_orgao varchar(100),
    data_inicio_exercicio date,
    data_fim_exercicio date,
    data_fim_carencia date,
    arquivo_origem varchar(60) not null,
    origem_carga varchar(10) not null default 'DOTNET2',
    carregado_em timestamptz not null default now(),
    constraint pk_peps primary key (id)
);

create index if not exists idx_peps_cpf_miolo on <schema>.peps (cpf_miolo);

comment on table <schema>.peps is 'Pessoas expostas politicamente (CGU/Portal da Transparência). Recarregada a cada arquivo mensal.';
comment on column <schema>.peps.id is 'Identificador interno.';
comment on column <schema>.peps.cpf_mascarado is 'CPF como publicado pela CGU, mascarado: ***.999.999-**.';
comment on column <schema>.peps.cpf_miolo is 'Os 6 dígitos visíveis do CPF. Cruzamento com clientes: cpf_miolo + nome.';
comment on column <schema>.peps.nome is 'Nome da pessoa exposta politicamente.';
comment on column <schema>.peps.sigla_funcao is 'Sigla da função (ex.: VEREAD, PREFEI).';
comment on column <schema>.peps.descricao_funcao is 'Descrição da função exercida.';
comment on column <schema>.peps.nivel_funcao is 'Nível da função, quando informado.';
comment on column <schema>.peps.nome_orgao is 'Órgão de exercício.';
comment on column <schema>.peps.data_inicio_exercicio is 'Início do exercício da função.';
comment on column <schema>.peps.data_fim_exercicio is 'Fim do exercício. Nulo = não informada pela CGU.';
comment on column <schema>.peps.data_fim_carencia is 'Fim da carência (continua PEP até esta data). Nulo = não informada.';
comment on column <schema>.peps.arquivo_origem is 'Nome do arquivo de onde a linha foi carregada (ex.: 202609_PEP.csv).';
comment on column <schema>.peps.origem_carga is 'Aplicação que gravou a linha (DOTNET2 = recriação de teste).';
comment on column <schema>.peps.carregado_em is 'Momento da carga.';

-- Acesso da aplicação às listas: lê (a API) e substitui o conteúdo (a ferramenta de carga, que usa o mesmo usuário):
-- select, insert e delete; sem update, sem criar objetos.
grant usage on schema <schema> to <usuario_app>;
grant select, insert, delete on <schema>.sancoes, <schema>.peps to <usuario_app>;
