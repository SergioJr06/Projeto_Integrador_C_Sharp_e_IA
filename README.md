# SafeGuard IA — Monitoramento Inteligente de EPIs

Sistema que usa **visão computacional** para detectar, em tempo real, trabalhadores sem os Equipamentos de Proteção Individual (EPIs) obrigatórios — capacete, óculos, luvas, botas e protetor auricular — e registrar cada infração com foto de evidência, horário e câmera. Os gestores acompanham tudo em um painel web com gráficos, histórico e exportação de dados.

> Projeto Integrador (C# e IA). Em ambientes industriais, a fiscalização humana é passiva e sujeita a falhas por cansaço ou desatenção; o SafeGuard IA automatiza essa supervisão para reduzir acidentes de trabalho e apoiar o cumprimento das Normas Regulamentadoras (NRs).

---

## Como funciona

```
Câmera / Vídeo → Motor de IA (YOLOv8) → API Backend (ASP.NET Core) → PostgreSQL + Evidências → Painel Blazor
```

1. O **motor de IA** (Python) lê a câmera, detecta a ausência de EPIs e, após confirmar a infração por vários quadros seguidos, envia o alerta (`multipart/form-data`) à API em uma *thread* separada, sem travar o vídeo.
2. A **API** valida os dados, grava a foto em `wwwroot/evidencias/` com nome único e salva no banco **apenas o caminho** e os metadados (nunca binários no banco).
3. O **painel** mostra indicadores, gráficos, histórico com as evidências e permite escolher quais EPIs cada câmera exige.

## Funcionalidades

**Motor de IA**
- Detecção em tempo real com YOLOv8, webcam local ou câmera IP (RTSP).
- EPIs exigidos configuráveis **por câmera** (`config/config.json`), relidos automaticamente a cada ~1 s, sem reiniciar.
- Confirmação por 10 quadros consecutivos e intervalo mínimo de 30 s entre alertas da mesma câmera, evitando alertas repetidos.
- Teclas **1, 2, 3** trocam de câmera; **q** encerra.

**API (ASP.NET Core)**
- `POST /api/monitoramento` com validação de campos, limite de 10 MB e verificação da assinatura JPEG.
- Consulta de registros (lista e detalhe).

**Painel (Blazor)**
- Login com usuário e senha (cookie de sessão, senhas com hash PBKDF2).
- Indicadores: infrações no período, ocorrências hoje, EPI mais recorrente, confiança média da IA.
- Gráficos (ApexCharts): infrações por tipo de EPI e histórico de alertas por dia.
- Filtros por período e câmera, tabela paginada, visualização ampliada da evidência e exportação em CSV.
- Página **Câmeras e EPIs** para configurar o que cada câmera fiscaliza.
- Interface empresarial e responsiva; acessível pelo celular na mesma rede Wi-Fi.

## Tecnologias

| Camada | Tecnologias |
|---|---|
| Inteligência artificial | Python 3.10–3.12, YOLOv8 (Ultralytics), PyTorch, OpenCV, NumPy, Requests |
| Backend | C#, .NET 10, ASP.NET Core Web API, Entity Framework Core, Npgsql |
| Banco de dados | PostgreSQL 15 em Docker (`docker-compose`) |
| Frontend | C#, Blazor (ASP.NET Core), ApexCharts, CSS próprio |
| Ferramentas | Docker Desktop, Visual Studio / VS Code, Git |

## Estrutura do projeto

```
├── SafeGuardBackend/        API REST (controllers, modelos, EF Core, wwwroot/evidencias)
├── SafeGuardFrontend/       Painel Blazor (páginas, serviços, login, gráficos)
├── src/automacaototal.py    Motor de IA (YOLOv8 + OpenCV)
├── models/best.pt           Modelo YOLO treinado
├── config/config.json       Câmeras e EPIs exigidos por câmera
├── database/init.sql        Criação da tabela infracoes
├── docker-compose.yml       PostgreSQL
├── iniciar.bat / parar.bat  Inicia e encerra tudo no Windows
├── liberar_celular.bat      Libera a porta do painel no firewall (acesso pelo celular)
├── requirements.txt         Dependências Python
└── SafeGuardIA.sln          Solução .NET
```

## Pré-requisitos

- **Docker Desktop** (aberto durante o uso)
- **SDK .NET 10** (o *SDK*, não só o *runtime*) — https://dotnet.microsoft.com/download/dotnet/10.0
- **Python 3.10 a 3.12** (marque *Add python.exe to PATH* na instalação)
- Uma webcam ou câmera IP
- Git (para clonar o repositório)

## Instalação e execução

### Opção 1 — Windows, um clique (recomendada)

```bash
git clone https://github.com/SEU_USUARIO/NOME_DO_REPOSITORIO.git
cd NOME_DO_REPOSITORIO
```

Com o Docker Desktop aberto, dê dois cliques em **`iniciar.bat`**. Ele verifica os requisitos, cria o ambiente Python (a primeira vez demora alguns minutos), sobe o banco, a API (porta 5168), o painel (porta 5080), pergunta se deve ligar o motor de IA e abre o navegador. Para encerrar, use **`parar.bat`**.

### Opção 2 — Manual (qualquer sistema)

Em terminais separados, a partir da pasta do projeto:

```bash
# 1) Banco de dados
docker compose up -d

# 2) API (http://localhost:5168)
dotnet run --project SafeGuardBackend/SafeGuardBackend.csproj --launch-profile http

# 3) Painel (http://localhost:5080)
dotnet run --project SafeGuardFrontend/SafeGuardFrontend.csproj --urls http://0.0.0.0:5080

# 4) Motor de IA
python -m venv .venv
.venv\Scripts\activate            # Linux/macOS: source .venv/bin/activate
pip install -r requirements.txt
python src/automacaototal.py
```

> Use sempre o perfil `http` na API: o motor de IA envia para `http://localhost:5168`.

### Opção 3 — Visual Studio Code

Abra a pasta do projeto, instale as extensões recomendadas e use **Terminal → Executar Tarefa → "Iniciar tudo (sem IA)"** ou **"Iniciar tudo (com IA)"**. Para depurar: **Executar e Depurar → "API + Painel (depurar)"**.

## Como usar

1. Abra **http://localhost:5080** e entre com o usuário padrão **`admin`** / senha **`SafeGuard@2026`** (troque antes de apresentar ou publicar — veja *Usuários e senhas*).
2. **Painel:** acompanhe indicadores, gráficos e o histórico. Use os filtros de período e câmera, clique em **Visualizar** para ver a evidência e em **Exportar CSV** para baixar os dados.
3. **Câmeras e EPIs:** marque os EPIs que cada câmera deve fiscalizar, edite nome e fonte (`0` = webcam; ou endereço RTSP) e clique em **Salvar alterações**. O motor de IA aplica em ~1 segundo.
4. Com o motor de IA rodando, cada infração confirmada aparece no painel com a foto.

Se a API estiver fora do ar, o painel mostra **dados de demonstração** com um aviso (não são infrações reais).

### Configuração das câmeras (`config/config.json`)

Também pode ser editado à mão:

```json
{
  "cameras": {
    "cam_0": { "nome": "Webcam Local", "source": 0, "require_helmet": true, "require_glasses": false },
    "cam_1": { "nome": "Linha 1", "source": "rtsp://usuario:senha@192.168.0.10:554/stream", "require_helmet": true, "require_gloves": true }
  }
}
```

Chaves disponíveis: `require_helmet`, `require_glasses`, `require_gloves`, `require_boots`, `require_ear_protection`, `require_vest`, `require_mask`.

### Acesso pelo celular (mesma rede Wi-Fi)

1. Execute **`liberar_celular.bat` como administrador** (uma única vez).
2. Rode `iniciar.bat`; no final ele mostra endereços como `http://192.168.0.15:5080`.
3. Abra esse endereço no celular e faça login.

Se não abrir: deixe a rede do computador como **Privada**, desative "isolamento de clientes" no roteador e use o IP do adaptador Wi-Fi/Ethernet (ignore IPs de Docker/WSL).

## API

| Método | Rota | Descrição |
|---|---|---|
| `POST` | `/api/monitoramento` | Registra uma infração. `multipart/form-data`: `CameraId`, `EpisFaltantes`, `NivelConfianca` (0 a 1), `Imagem` (JPEG, até 10 MB) |
| `GET` | `/api/monitoramento` | Últimos 500 registros |
| `GET` | `/api/monitoramento/{id}` | Detalhe de um registro |

Exemplo de teste: `SafeGuardBackend/SafeGuardBackend.http` (extensão *REST Client* no VS Code, ou o próprio Visual Studio).

**Banco:** tabela `infracoes` (`id`, `camera_id`, `epis_faltantes`, `nivel_confianca`, `caminho_evidencia`, `data_hora`), criada automaticamente por `database/init.sql` em um volume novo. Conexão local: `localhost:5433`, banco `safeguard_db`, usuário `admin`, senha `senha123`. Se o volume já existia, rode `docker compose down -v` e suba de novo.

## Modelo de IA

O arquivo `models/best.pt` é uma rede YOLOv8 treinada para detectar EPIs e suas ausências. Para o alerta de um EPI funcionar, o modelo precisa ter a classe de ausência correspondente, com estes nomes (minúsculos, sem acento): `sem capacete`, `sem oculos`, `sem luvas`, `sem bota`, `sem protetor auricular`, `sem colete`, `sem mascara`. Para usar outro modelo, substitua `models/best.pt` mantendo esses nomes.

## Usuários e senhas

Ficam em `SafeGuardFrontend/appsettings.json` (`Auth:Usuarios`), sempre como hash PBKDF2. Para gerar o hash de uma nova senha:

```bash
python -c "import hashlib,os,base64,getpass; s=os.urandom(16); p=getpass.getpass().encode(); h=hashlib.pbkdf2_hmac('sha256',p,s,100000,32); print(f'pbkdf2\$100000\${base64.b64encode(s).decode()}\${base64.b64encode(h).decode()}')"
```

Cole o resultado em `SenhaHash`. Em produção, prefira variáveis de ambiente (`Auth__Usuarios__0__SenhaHash`).

## Solução de problemas

| Sintoma | O que fazer |
|---|---|
| `No .NET SDKs were found` | Instale o **SDK** .NET 10 (não só o runtime), feche o terminal e rode de novo. |
| `Python was not found` | Instale o Python 3.10–3.12 e marque *Add to PATH*, ou desative os aliases da Microsoft Store. |
| Erro de conexão com o banco | Abra o Docker Desktop; confira se a porta 5433 está livre. |
| `relation "infracoes" does not exist` | `docker compose down -v` e depois `docker compose up -d`. |
| IA mostra "Backend C# offline" | A API não está rodando; inicie-a com o perfil `http`. |
| Painel não abre no celular | Veja *Acesso pelo celular*. |

## Limitações e segurança

- Projeto acadêmico: as credenciais do banco (`admin`/`senha123`) e o usuário padrão do painel são para desenvolvimento. Troque-os antes de qualquer uso real.
- O login protege o **painel**; a API (`/api/monitoramento`) e as imagens servidas por ela não têm autenticação e devem ficar apenas em rede local.
- O CORS da API está liberado apenas para origens locais.
- A página de câmeras grava em `config/config.json`, então painel e motor de IA precisam rodar na mesma máquina.

## Equipe

| Integrante | Responsabilidade |
|---|---|
| Wesley | Backend (API, banco e armazenamento de evidências) |
| Ana | Frontend (painel, gráficos e histórico) |
| Sérgio | Motor de IA (YOLOv8) |
