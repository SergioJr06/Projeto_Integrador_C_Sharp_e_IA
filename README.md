# SafeGuard IA: monitoramento de EPIs com visão computacional
 
**Detecta trabalhadores sem Equipamento de Proteção Individual em câmeras, em tempo real, e transforma cada infração em um registro auditável, com foto, horário e câmera, em um painel web para gestores.**
 
![Painel](docs/InterfaceHome.png)
 
## Resumo
 
| | |
|---|---|
| **Problema** | A fiscalização de EPIs feita por pessoas é passiva e sujeita a falhas por cansaço ou desatenção. |
| **Solução** | Um modelo YOLOv8 detecta a ausência de EPI nas câmeras, e cada infração confirmada vira um registro com foto de evidência. |
| **Entrega** | Motor de IA (Python), API REST (ASP.NET Core), banco PostgreSQL em Docker e painel web (Blazor). |
| **Destaques** | Regras de EPI por câmera aplicadas em cerca de 1 s, sem reiniciar. Alerta só após 10 quadros seguidos e com intervalo de 30 s. API protegida por chave. Painel com login (PBKDF2). Gráficos próprios que funcionam offline. |
| **Contexto** | Projeto Integrador (C# e IA). [individual ou em equipe: descreva em uma linha o seu papel] |
 
## O que ele faz
 
| Camada | Recursos |
|---|---|
| **Motor de IA** | Detecta a ausência de 7 EPIs (capacete, óculos, luvas, botas, protetor auricular, colete e máscara) em webcam ou câmera IP (RTSP). Confirma a infração por 10 quadros consecutivos e respeita 30 s entre alertas da mesma câmera. Envia o alerta em uma *thread* separada, sem travar o vídeo. Publica seu estado em `config/status_motor.json`. |
| **API** | `POST /api/monitoramento` valida os campos, o tamanho (até 10 MB) e a assinatura JPEG da foto. Consulta a lista e o detalhe dos registros. Exige a chave `X-Api-Key`. |
| **Painel** | Login com sessão por cookie. Indicadores (infrações no período, ocorrências hoje, EPI mais recorrente, confiança média da IA). Gráficos por tipo de EPI e por dia. Filtros por período e câmera, tabela paginada, foto ampliada e exportação em CSV. Página **Câmeras e EPIs** para definir o que cada câmera fiscaliza. Responsivo e acessível pelo celular na mesma rede Wi-Fi. |
 
![Detecção](docs/Infracao.png)
 
## Arquitetura
 
```mermaid
flowchart LR
    CAM["Câmera ou vídeo<br/>webcam / RTSP"] --> IA["Motor de IA<br/>Python · YOLOv8 · OpenCV"]
    IA -->|"POST multipart + X-Api-Key<br/>(thread separada)"| API["API REST<br/>ASP.NET Core · EF Core"]
    API --> DB[("PostgreSQL 15<br/>Docker")]
    API --> FOTOS["Fotos de evidência<br/>em disco"]
    DB --> PAINEL["Painel Blazor<br/>login · gráficos · CSV"]
    FOTOS --> PAINEL
    PAINEL -->|"config/config.json"| IA
    IA -->|"config/status_motor.json"| PAINEL
```
 
Caminho de uma infração:
 
```mermaid
sequenceDiagram
    participant M as Motor de IA
    participant A as API
    participant B as PostgreSQL
    participant P as Painel
    M->>M: detecta ausência de EPI por 10 quadros seguidos
    M->>A: POST /api/monitoramento (foto JPEG, metadados e X-Api-Key)
    A->>A: valida chave, campos, 10 MB e assinatura JPEG
    A->>A: grava a foto com nome único
    A->>B: salva o caminho e os metadados
    P->>A: GET /api/monitoramento
    A-->>P: últimos 500 registros
    P-->>P: gráficos, histórico e foto ampliada
```
 
## Decisões de engenharia
 
| Decisão | Por quê |
|---|---|
| **IA e produto separados por uma API** | O motor em Python só detecta e envia. Regra de negócio, persistência e interface ficam em C#. Dá para trocar o modelo sem mexer no painel. |
| **Envio do alerta em *thread* separada** | A detecção continua em tempo real mesmo se a API estiver lenta ou fora do ar. |
| **Confirmação por 10 quadros e intervalo de 30 s** | Evita falsos positivos de um quadro isolado e alertas repetidos da mesma pessoa. |
| **Foto em disco, só o caminho no banco** | Mantém o PostgreSQL leve e evita binários na tabela `infracoes`. |
| **Configuração por câmera em JSON, com recarga automática** | O gestor muda as regras no painel e o motor aplica em cerca de 1 s, inclusive trocando a fonte da câmera, sem reiniciar. |
| **Formato numérico fixo na API (cultura invariável)** | A IA envia `0.87`. Em Windows pt-BR a leitura padrão interpretaria como `87` e a API devolveria erro 400. |
| **Gráficos em SVG próprios, sem CDN** | O painel funciona offline, na rede interna de uma fábrica. |
| **Dados de demonstração quando a API está fora do ar** | O painel avisa que não são infrações reais, em vez de quebrar. |
 
## Segurança
 
| Controle | Como funciona |
|---|---|
| **Chave de API** | O middleware exige o cabeçalho `X-Api-Key` em todas as rotas `/api`, com comparação em tempo constante. O `iniciar.bat` gera uma chave aleatória a cada execução e a entrega à API, ao painel e ao motor, então ela não fica salva em nenhum arquivo. |
| **Login do painel** | Senhas guardadas como hash PBKDF2 (SHA-256, 100 000 iterações, sal aleatório). Cookie `HttpOnly` e `SameSite=Lax`, com expiração de 8 h renovada pelo uso. |
| **Validação do upload** | Campos obrigatórios, confiança entre 0 e 1, limite de 10 MB e conferência da assinatura JPEG, em vez de confiar na extensão. |
| **Fotos de evidência** | O painel só entrega arquivos com nome no formato de GUID (`^[a-f0-9]{32}\.jpg$`) e exige login, o que impede a leitura de outros caminhos. |
| **Segredos fora do Git** | O `.gitignore` bloqueia `.env`, as fotos geradas, o CSV de relatório e o estado do motor. |
 
O que **não** está coberto está na seção [Limitações](#limitações).
 
## Modelo de IA
 
| Item | Detalhe |
|---|---|
| **Arquitetura** | YOLOv8 (Ultralytics), pesos em `models/best.pt` |
| **Classes de ausência** | `sem capacete`, `sem oculos`, `sem luvas`, `sem bota`, `sem protetor auricular`, `sem colete`, `sem mascara` |
| **Origem das imagens** | [de onde vieram, quantas imagens e quais classes] |
| **Treino** | [treinado por mim com o dataset acima, ou modelo pronto; versão (n, s ou m)] |
| **Métricas** | [mAP, precisão e recall. Escreva só o que tiver medido] |
 
O alerta de um EPI só dispara se o modelo tiver a classe de ausência correspondente, com os nomes acima (minúsculos, sem acento). Para usar outro modelo, substitua `models/best.pt` mantendo esses nomes.
 
## Stack
 
| Camada | Tecnologias |
|---|---|
| IA | Python 3.10 a 3.12, YOLOv8 (Ultralytics), PyTorch, OpenCV, NumPy, Requests |
| Backend | C#, .NET 10, ASP.NET Core Web API, Entity Framework Core, Npgsql |
| Banco | PostgreSQL 15 em Docker (`docker-compose`) |
| Frontend | C#, Blazor (componentes Razor), gráficos em SVG e JavaScript próprios, CSS próprio |
| Ferramentas | Docker Desktop, Visual Studio ou VS Code, Git |
 
## Como rodar
 
**Pré-requisitos**
 
| Item | Observação |
|---|---|
| Docker Desktop | Precisa estar aberto durante o uso |
| SDK .NET 10 | O *SDK*, não só o *runtime*: https://dotnet.microsoft.com/download/dotnet/10.0 |
| Python 3.10 a 3.12 | Marque *Add python.exe to PATH* na instalação |
| Webcam ou câmera IP | Fonte `0` é a webcam local |
| Git | Para clonar o repositório |
 
**Portas:** banco `5433`, API `5168`, painel `5080`.
 
```bash
git clone https://github.com/SergioJr06/Projeto_Integrador_C_Sharp_e_IA.git
cd Projeto_Integrador_C_Sharp_e_IA
```
 
### Opção 1: Windows, um clique (recomendada)
 
Com o Docker Desktop aberto, dê dois cliques em **`iniciar.bat`**. Ele verifica os requisitos, cria o ambiente Python (a primeira vez demora alguns minutos), sobe o banco, a API e o painel, pergunta se deve ligar o motor de IA e abre o navegador. Para encerrar, use **`parar.bat`**.
 
### Opção 2: manual
 
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
 
Use sempre o perfil `http` na API, porque o motor envia para `http://localhost:5168`. Neste modo a chave de API é opcional: para ligá-la, veja [Chave de API](#chave-de-api).
 
### Opção 3: Visual Studio Code
 
Abra a pasta, instale as extensões recomendadas e use **Terminal → Executar Tarefa → "Iniciar tudo (sem IA)"** ou **"Iniciar tudo (com IA)"**. Para depurar: **Executar e Depurar → "API + Painel (depurar)"**.
 
## Como usar
 
1. Abra **http://localhost:5080** e entre com o usuário `admin`. A senha de desenvolvimento aparece no final da execução do `iniciar.bat`. Troque-a antes de apresentar ou publicar (veja [Usuários, perfis e permissões](#usuários-perfis-e-permissões)).
2. **Painel:** acompanhe indicadores, gráficos e histórico. Use os filtros, clique em **Visualizar** para ver a evidência e em **Exportar CSV** para baixar os dados.
3. **Câmeras e EPIs** (somente administrador): marque os EPIs de cada câmera, edite nome e fonte (`0` é a webcam, ou um endereço RTSP), escolha **Usar esta câmera** e clique em **Salvar e aplicar**. O painel mostra se o motor está online e se aplicou a configuração.
4. **Usuários e permissões** (somente administrador): crie usuários e escolha a câmera de cada um. **Relatórios:** gere relatórios descritivos e personalizados.
5. Com o motor rodando, cada infração confirmada aparece no painel com a foto.

### Configuração das câmeras (`config/config.json`)
 
Também pode ser editado à mão:
 
```json
{
  "cameras": {
    "cam_0": { "nome": "Webcam Local", "source": 0, "require_helmet": true, "require_glasses": false },
    "cam_1": { "nome": "Linha 1", "source": "rtsp://USUARIO:SENHA@192.168.0.10:554/stream", "require_helmet": true, "require_gloves": true }
  }
}
```
 
Chaves disponíveis: `require_helmet`, `require_glasses`, `require_gloves`, `require_boots`, `require_ear_protection`, `require_vest`, `require_mask`. Não commite o `config.json` com senha de câmera.
 
### Acesso pelo celular (mesma rede Wi-Fi)
 
1. Execute **`liberar_celular.bat` como administrador** (uma única vez).
2. Rode `iniciar.bat`. No final ele mostra endereços como `http://192.168.0.15:5080`.
3. Abra esse endereço no celular e faça login.
Se não abrir: deixe a rede do computador como **Privada**, desative o "isolamento de clientes" no roteador e use o IP do adaptador Wi-Fi ou Ethernet (ignore IPs de Docker e WSL).
 
### Onde ficam as fotos de evidência
 
As fotos ficam em `SafeGuardBackend/wwwroot/evidencias/` e o banco guarda só o caminho. **Banco (volume do Docker) e fotos (pasta do projeto) são independentes**: ao rodar de outra pasta, os registros antigos continuam no painel, mas as fotos antigas aparecem como "Sem imagem". Para fixar um local, defina em `SafeGuardBackend/appsettings.json`:
 
```json
"Evidencias": { "Pasta": "C:\\SafeGuardIA\\evidencias" }
```
 
Deixe vazio para usar `wwwroot/evidencias`. Para recomeçar do zero: `docker compose down -v`.
 
## API
 
| Método | Rota | Descrição |
|---|---|---|
| `POST` | `/api/monitoramento` | Registra uma infração. `multipart/form-data`: `CameraId`, `EpisFaltantes`, `NivelConfianca` (0 a 1), `Imagem` (JPEG, até 10 MB) |
| `GET` | `/api/monitoramento` | Últimos 500 registros |
| `GET` | `/api/monitoramento/{id}` | Detalhe de um registro |
 
```bash
curl -H "X-Api-Key: SUA_CHAVE" http://localhost:5168/api/monitoramento
```
 
Exemplos de requisição: `SafeGuardBackend/SafeGuardBackend.http` (extensão *REST Client* no VS Code ou o próprio Visual Studio).
 
**Banco:** tabela `infracoes` (`id`, `camera_id`, `epis_faltantes`, `nivel_confianca`, `caminho_evidencia`, `data_hora`), criada por `database/init.sql` em um volume novo. Se o volume já existia, rode `docker compose down -v` e suba de novo. As credenciais de desenvolvimento estão em `docker-compose.yml` e `SafeGuardBackend/appsettings.json`.
 
### Chave de API
 
A API protege as rotas `/api/*` com o cabeçalho `X-Api-Key`. A chave é opcional: sem ela configurada, a API funciona como antes.
 
| Processo | Variável de ambiente |
|---|---|
| API e painel | `Api__Chave` |
| Motor de IA | `SAFEGUARD_API_KEY` |
 
Os três precisam receber o **mesmo valor**. O `iniciar.bat` já faz isso sozinho. Na execução manual, defina as variáveis antes de iniciar cada processo, por exemplo no PowerShell:
 
```powershell
$env:Api__Chave = "troque-por-um-valor-longo-e-aleatorio"
$env:SAFEGUARD_API_KEY = $env:Api__Chave
```
 
### Usuários, perfis e permissões

O administrador cadastra os usuários no painel, em **Usuários e permissões** (nome, login, senha, perfil e câmera). O cadastro fica em `config/usuarios.json`, com a senha sempre como hash PBKDF2, e está no `.gitignore`.

| Perfil | O que pode fazer |
|---|---|
| **Administrador** | Tudo: painel e relatórios de todas as câmeras, **Câmeras e EPIs**, **Usuários e permissões**. |
| **Usuário** | Somente leitura, apenas da câmera pela qual é responsável: painel, gráficos, fotos de evidência, exportação CSV e **Relatórios** descritivos personalizáveis. Não altera EPIs nem usuários. |

Como a regra é aplicada:

- As páginas de administração exigem o perfil `admin`. Quem não é administrador vê “Acesso negado”.
- O painel, as fotos (`/evidencias/...`) e o CSV filtram sempre pela câmera do usuário no servidor. Mudar o endereço no navegador não mostra outra câmera.
- O cadastro é conferido a cada requisição: remover um usuário, trocar seu perfil ou sua câmera vale imediatamente.
- Não é possível remover a si mesmo, alterar o próprio perfil nem tirar o único administrador.

Na primeira execução, o `usuarios.json` é criado a partir de `Auth:Usuarios` do `SafeGuardFrontend/appsettings.json` (o usuário `admin` inicial). **Esqueceu a senha do administrador?** Apague `config/usuarios.json` e reinicie o painel: ele volta ao estado do `appsettings.json`. Para gerar o hash de uma senha para o `appsettings.json`:

```bash
python gerar_hash_senha.py
```

### Relatórios

Em **Relatórios**, o usuário escolhe título, período, câmera (administrador) e quais blocos incluir: indicadores, resumo descritivo em texto, infrações por EPI, ocorrências por dia, lista de ocorrências e miniaturas das evidências. Também pode acrescentar observações. **Imprimir / salvar em PDF** usa a impressão do navegador, e o CSV respeita o mesmo período.

## Estrutura do projeto
 
```
├── SafeGuardBackend/         API REST (controllers, modelos, EF Core, wwwroot/evidencias)
├── SafeGuardFrontend/        Painel Blazor (páginas, serviços, login, gráficos)
├── src/automacaototal.py     Motor de IA (YOLOv8 + OpenCV)
├── src/config_watcher.py     Recarga da configuração e publicação do estado do motor
├── models/best.pt            Modelo YOLO treinado
├── config/config.json        Câmeras, câmera ativa e EPIs exigidos (editado pelo painel)
├── config/usuarios.json      Usuários, perfis e câmera de cada um (criado pelo painel, fora do Git)
├── database/init.sql         Criação da tabela infracoes
├── docker-compose.yml        PostgreSQL
├── iniciar.bat / parar.bat   Inicia e encerra tudo no Windows
├── liberar_celular.bat       Libera a porta do painel no firewall
├── gerar_hash_senha.py       Gera o hash PBKDF2 de uma senha do painel
├── requirements.txt          Dependências Python
└── SafeGuardIA.sln           Solução .NET
```
 
## Solução de problemas
 
| Sintoma | O que fazer |
|---|---|
| `No .NET SDKs were found` | Instale o **SDK** do .NET 10 (não só o runtime), feche o terminal e rode de novo. |
| `Python was not found` | Instale o Python 3.10 a 3.12 e marque *Add to PATH*, ou desative os aliases da Microsoft Store. |
| Erro de conexão com o banco | Abra o Docker Desktop e confira se a porta 5433 está livre. |
| `relation "infracoes" does not exist` | `docker compose down -v` e depois `docker compose up -d`. |
| Motor mostra "Backend C# offline" | A API não está rodando. Inicie-a com o perfil `http`. |
| Motor mostra `Erro do servidor C#: 401` | A chave do motor (`SAFEGUARD_API_KEY`) é diferente da da API. |
| Painel mostra dados de demonstração com a API no ar | A chave do painel (`Api__Chave`) é diferente da da API. |
| Painel não abre no celular | Veja *Acesso pelo celular*. |
| Gráficos vazios ou com aparência antiga após atualizar | Pressione **Ctrl+F5** no navegador. |
| "Motor de IA: desligado" no painel, mas a janela do motor está aberta | O motor roda de **outra pasta** do projeto. Rode `parar.bat` e inicie só pelo `iniciar.bat` da pasta atual. |
| Registros antigos aparecem com "Sem imagem" | As fotos ficaram na pasta de outra cópia. Copie os `.jpg` de `SafeGuardBackend/wwwroot/evidencias/` para a pasta atual. |
 
## Limitações
 
| Limitação | Detalhe |
|---|---|
| **Credenciais de desenvolvimento** | O banco e o usuário inicial do painel usam credenciais de desenvolvimento. Troque-as antes de qualquer uso real. |
| **Chave de API única** | A chave é compartilhada entre os processos. As permissões por perfil e câmera valem no painel, não na API. |
| **Fotos em `/evidencias` na API** | Não exigem a chave. Por isso a API deve ficar apenas em rede local. O painel entrega as fotos com login. |
| **Sem HTTPS no uso local** | O tráfego entre motor, API e painel é HTTP. |
| **Painel e motor na mesma máquina** | Eles trocam `config.json` e `status_motor.json` por arquivo. |
| **Sem testes automatizados** | A verificação foi manual. |
| **Só o banco em contêiner** | API, painel e motor rodam direto na máquina. |
 
## Próximos passos
 
| Melhoria | Por quê |
|---|---|
| Colocar API e painel no `docker-compose` | Subir tudo, menos o motor com câmera, com um só comando. |
| Testes automatizados da API e do `AuthService` | Proteger a validação de upload e o login contra regressões. |
| Tokens por serviço e proteção de `/evidencias` | Substituir a chave compartilhada e fechar a limitação das fotos. |
| Trocar a troca de arquivos por mensagens | Permitir que painel e motor rodem em máquinas diferentes. |
| Avaliar o modelo em um conjunto separado de imagens | Documentar precisão e recall por classe de EPI. |