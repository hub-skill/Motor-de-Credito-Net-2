-- =============================================================================
-- Motor de Crédito .NET 2 (recriação de teste): listas da CGU, Oracle 21c (banco plugável XEPDB1)
-- Executado pela ferramenta (dotnet run --project src/MotorCredito.Carga -- migrar) como administrador.
-- <dono> = MOTOR_CREDITO_NET_2 (sem login) e <usuario_app> = MOTOR_APP_NET_2 são trocados pela ferramenta.
-- Cada comando termina numa linha com "/". Reexecutar é seguro: "já existe" é ignorado.
-- A aplicação só LÊ estas tabelas (select), pelos sinônimos.
-- =============================================================================

CREATE TABLE <dono>.sancoes (
    id NUMBER GENERATED ALWAYS AS IDENTITY,
    cadastro VARCHAR2(10) NOT NULL,
    codigo_sancao NUMBER(12) NOT NULL,
    tipo_pessoa CHAR(1),
    documento VARCHAR2(20) NOT NULL,
    nome_sancionado VARCHAR2(200),
    nome_informado_orgao VARCHAR2(200),
    razao_social VARCHAR2(200),
    nome_fantasia VARCHAR2(100),
    numero_processo VARCHAR2(80),
    categoria VARCHAR2(120),
    valor_multa NUMBER(15,2),
    data_inicio DATE,
    data_fim DATE,
    data_publicacao DATE,
    publicacao VARCHAR2(150),
    detalhamento_publicacao CLOB,
    data_transito_julgado DATE,
    abrangencia VARCHAR2(100),
    orgao_sancionador VARCHAR2(300),
    uf_orgao CHAR(2),
    esfera_orgao VARCHAR2(20),
    fundamentacao_legal CLOB,
    data_origem DATE,
    origem_informacao VARCHAR2(150),
    observacoes CLOB,
    arquivo_origem VARCHAR2(60) NOT NULL,
    origem_carga VARCHAR2(10) DEFAULT 'DOTNET2' NOT NULL,
    carregado_em TIMESTAMP WITH TIME ZONE DEFAULT SYSTIMESTAMP NOT NULL,
    CONSTRAINT pk_sancoes PRIMARY KEY (id),
    CONSTRAINT uk_sancoes_cadastro_codigo UNIQUE (cadastro, codigo_sancao),
    CONSTRAINT ck_sancoes_cadastro CHECK (cadastro IN ('CEIS', 'CNEP')),
    CONSTRAINT ck_sancoes_tipo_pessoa CHECK (tipo_pessoa IN ('F', 'J')),
    CONSTRAINT ck_sancoes_valor_multa CHECK (valor_multa >= 0)
)
/
CREATE INDEX <dono>.idx_sancoes_documento ON <dono>.sancoes (documento)
/
COMMENT ON TABLE <dono>.sancoes IS 'Sanções do CEIS e do CNEP (CGU/Portal da Transparência). Recarregada a cada arquivo diário.'
/
COMMENT ON COLUMN <dono>.sancoes.id IS 'Identificador interno.'
/
COMMENT ON COLUMN <dono>.sancoes.cadastro IS 'Lista de origem: CEIS ou CNEP.'
/
COMMENT ON COLUMN <dono>.sancoes.codigo_sancao IS 'Código da sanção na CGU. Único por cadastro.'
/
COMMENT ON COLUMN <dono>.sancoes.tipo_pessoa IS 'F = pessoa física, J = pessoa jurídica. Nulo quando a CGU não informa.'
/
COMMENT ON COLUMN <dono>.sancoes.documento IS 'CPF ou CNPJ do sancionado, só dígitos e letras (CNPJ alfanumérico), como veio no arquivo.'
/
COMMENT ON COLUMN <dono>.sancoes.nome_sancionado IS 'Nome do sancionado.'
/
COMMENT ON COLUMN <dono>.sancoes.nome_informado_orgao IS 'Nome informado pelo órgão sancionador.'
/
COMMENT ON COLUMN <dono>.sancoes.razao_social IS 'Razão social no cadastro da Receita (PJ).'
/
COMMENT ON COLUMN <dono>.sancoes.nome_fantasia IS 'Nome fantasia no cadastro da Receita (PJ).'
/
COMMENT ON COLUMN <dono>.sancoes.numero_processo IS 'Número do processo administrativo ou judicial.'
/
COMMENT ON COLUMN <dono>.sancoes.categoria IS 'Categoria da sanção (impedimento, suspensão, inidoneidade, multa...).'
/
COMMENT ON COLUMN <dono>.sancoes.valor_multa IS 'Valor da multa em reais (somente CNEP).'
/
COMMENT ON COLUMN <dono>.sancoes.data_inicio IS 'Início da sanção. Nulo = já vigente.'
/
COMMENT ON COLUMN <dono>.sancoes.data_fim IS 'Fim da sanção. Nulo = sem prazo determinado.'
/
COMMENT ON COLUMN <dono>.sancoes.data_publicacao IS 'Data de publicação da sanção.'
/
COMMENT ON COLUMN <dono>.sancoes.publicacao IS 'Meio de publicação (ex.: Diário Oficial).'
/
COMMENT ON COLUMN <dono>.sancoes.detalhamento_publicacao IS 'Detalhamento do meio de publicação.'
/
COMMENT ON COLUMN <dono>.sancoes.data_transito_julgado IS 'Data do trânsito em julgado, quando houver.'
/
COMMENT ON COLUMN <dono>.sancoes.abrangencia IS 'Abrangência da sanção.'
/
COMMENT ON COLUMN <dono>.sancoes.orgao_sancionador IS 'Órgão que aplicou a sanção.'
/
COMMENT ON COLUMN <dono>.sancoes.uf_orgao IS 'UF do órgão sancionador.'
/
COMMENT ON COLUMN <dono>.sancoes.esfera_orgao IS 'Esfera do órgão: FEDERAL, ESTADUAL ou MUNICIPAL.'
/
COMMENT ON COLUMN <dono>.sancoes.fundamentacao_legal IS 'Fundamentação legal da sanção.'
/
COMMENT ON COLUMN <dono>.sancoes.data_origem IS 'Data da informação na origem.'
/
COMMENT ON COLUMN <dono>.sancoes.origem_informacao IS 'Órgão de origem da informação.'
/
COMMENT ON COLUMN <dono>.sancoes.observacoes IS 'Observações da CGU.'
/
COMMENT ON COLUMN <dono>.sancoes.arquivo_origem IS 'Nome do arquivo de onde a linha foi carregada (ex.: 20261110_CEIS.csv).'
/
COMMENT ON COLUMN <dono>.sancoes.origem_carga IS 'Aplicação que gravou a linha (DOTNET2 = recriação de teste).'
/
COMMENT ON COLUMN <dono>.sancoes.carregado_em IS 'Momento da carga.'
/
CREATE TABLE <dono>.peps (
    id NUMBER GENERATED ALWAYS AS IDENTITY,
    cpf_mascarado VARCHAR2(20) NOT NULL,
    cpf_miolo VARCHAR2(20) NOT NULL,
    nome VARCHAR2(100) NOT NULL,
    sigla_funcao VARCHAR2(10),
    descricao_funcao VARCHAR2(100),
    nivel_funcao VARCHAR2(10),
    nome_orgao VARCHAR2(100),
    data_inicio_exercicio DATE,
    data_fim_exercicio DATE,
    data_fim_carencia DATE,
    arquivo_origem VARCHAR2(60) NOT NULL,
    origem_carga VARCHAR2(10) DEFAULT 'DOTNET2' NOT NULL,
    carregado_em TIMESTAMP WITH TIME ZONE DEFAULT SYSTIMESTAMP NOT NULL,
    CONSTRAINT pk_peps PRIMARY KEY (id)
)
/
CREATE INDEX <dono>.idx_peps_cpf_miolo ON <dono>.peps (cpf_miolo)
/
COMMENT ON TABLE <dono>.peps IS 'Pessoas expostas politicamente (CGU/Portal da Transparência). Recarregada a cada arquivo mensal.'
/
COMMENT ON COLUMN <dono>.peps.id IS 'Identificador interno.'
/
COMMENT ON COLUMN <dono>.peps.cpf_mascarado IS 'CPF como publicado pela CGU, mascarado: ***.999.999-**.'
/
COMMENT ON COLUMN <dono>.peps.cpf_miolo IS 'Os 6 dígitos visíveis do CPF. Cruzamento com clientes: cpf_miolo + nome.'
/
COMMENT ON COLUMN <dono>.peps.nome IS 'Nome da pessoa exposta politicamente.'
/
COMMENT ON COLUMN <dono>.peps.sigla_funcao IS 'Sigla da função (ex.: VEREAD, PREFEI).'
/
COMMENT ON COLUMN <dono>.peps.descricao_funcao IS 'Descrição da função exercida.'
/
COMMENT ON COLUMN <dono>.peps.nivel_funcao IS 'Nível da função, quando informado.'
/
COMMENT ON COLUMN <dono>.peps.nome_orgao IS 'Órgão de exercício.'
/
COMMENT ON COLUMN <dono>.peps.data_inicio_exercicio IS 'Início do exercício da função.'
/
COMMENT ON COLUMN <dono>.peps.data_fim_exercicio IS 'Fim do exercício. Nulo = não informada pela CGU.'
/
COMMENT ON COLUMN <dono>.peps.data_fim_carencia IS 'Fim da carência (continua PEP até esta data). Nulo = não informada.'
/
COMMENT ON COLUMN <dono>.peps.arquivo_origem IS 'Nome do arquivo de onde a linha foi carregada (ex.: 202609_PEP.csv).'
/
COMMENT ON COLUMN <dono>.peps.origem_carga IS 'Aplicação que gravou a linha (DOTNET2 = recriação de teste).'
/
COMMENT ON COLUMN <dono>.peps.carregado_em IS 'Momento da carga.'
/
GRANT SELECT, INSERT, DELETE ON <dono>.sancoes TO <usuario_app>
/
GRANT SELECT, INSERT, DELETE ON <dono>.peps TO <usuario_app>
/
CREATE OR REPLACE SYNONYM <usuario_app>.sancoes FOR <dono>.sancoes
/
CREATE OR REPLACE SYNONYM <usuario_app>.peps FOR <dono>.peps
/
