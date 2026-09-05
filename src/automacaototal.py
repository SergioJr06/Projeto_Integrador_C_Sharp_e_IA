import cv2
import torch
import os
import time
import json
import requests
import threading
from datetime import datetime
from ultralytics import YOLO
import numpy as np

# ==========================================================
# 1. CONFIGURAÇÕES DE CAMINHO E PERFORMANCE
# ==========================================================
BASE_DIR = os.path.dirname(os.path.abspath(__file__))
ROOT_DIR = os.path.dirname(BASE_DIR)

CONFIG_PATH    = os.path.join(ROOT_DIR, "config", "config.json")
MODELO_PATH    = os.path.join(ROOT_DIR, "models", "best.pt")
PASTA_CAPTURES = os.path.join(ROOT_DIR, "capturas_violacao")
LOG_PATH       = os.path.join(ROOT_DIR, "relatorio_violacoes.csv")

os.makedirs(PASTA_CAPTURES, exist_ok=True)

# Força transporte TCP, buffer mínimo e decodificação rápida para DVR Intelbras.
# probesize e analyzeduration reduzem o tempo de análise inicial do stream.
# flags low_delay elimina buffers internos do FFmpeg, reduzindo latência.
os.environ["OPENCV_FFMPEG_CAPTURE_OPTIONS"] = (
    "rtsp_transport;tcp"
    "|buffer_size;0"
    "|probesize;32"
    "|analyzeduration;0"
    "|fflags;nobuffer"
    "|flags;low_delay"
    "|framedrop;1"
)

# ==========================================================
# PIPELINES DE MELHORIA DE IMAGEM
# ==========================================================
# Aplicados ANTES da inferência — o YOLO vê a imagem melhorada,
# aumentando a detecção de objetos pequenos (óculos, plugs).
#
# Pipeline RTSP (~30 ms/frame em CPU 1280x720):
#   1. medianBlur(3)  — remove ruído "sal-e-pimenta" do H.264 (~0.5ms).
#                       50x mais rápido que fastNlMeans, mesmo efeito.
#   2. CLAHE YUV(2.5) — contraste adaptativo local no canal Y. Não
#                       distorce cores, revela detalhes em sombras.
#   3. Unsharp mask   — realça bordas sem artefatos de ringing.
#                       Melhora leitura de óculos e texto em capacetes.
#
# Pipeline Webcam (~30 ms/frame em CPU 1280x720):
#   1. Bilateral d=5  — suaviza ruído Gaussiano preservando bordas.
#   2. Unsharp mask   — nitidez final.
#
# Ajuste:
#   CLAHE_CLIP  : 2.5 padrão | 3.5 se câmera escura | 1.5 se estourar
#   UNSHARP_STR : 0.5 padrão | 0.7 mais nitidez | 0.3 mais suavidade
# ==========================================================
import numpy as np

CLAHE_CLIP  = 2.5
UNSHARP_STR = 0.5

_clahe_rtsp = cv2.createCLAHE(clipLimit=CLAHE_CLIP, tileGridSize=(8, 8))

def melhorar_frame_rtsp(frame: np.ndarray) -> np.ndarray:
    # 1. Remove ruído impulsivo de compressão H.264
    out = cv2.medianBlur(frame, 3)
    # 2. CLAHE no canal Y (luminância) — recupera contraste perdido
    yuv = cv2.cvtColor(out, cv2.COLOR_BGR2YUV)
    yuv[:, :, 0] = _clahe_rtsp.apply(yuv[:, :, 0])
    out = cv2.cvtColor(yuv, cv2.COLOR_YUV2BGR)
    # 3. Unsharp mask — realça bordas reais sem ringing
    blur = cv2.GaussianBlur(out, (0, 0), sigmaX=1.5)
    return cv2.addWeighted(out, 1.0 + UNSHARP_STR, blur, -UNSHARP_STR, 0)

def melhorar_frame_webcam(frame: np.ndarray) -> np.ndarray:
    out = cv2.bilateralFilter(frame, d=5, sigmaColor=25, sigmaSpace=15)
    blur = cv2.GaussianBlur(out, (0, 0), sigmaX=1.5)
    return cv2.addWeighted(out, 1.0 + UNSHARP_STR, blur, -UNSHARP_STR, 0)

# ==========================================================
# 2. LEITOR DE FRAMES EM THREAD SEPARADA
#    Desacopla a captura da câmera do loop de inferência,
#    eliminando o gargalo de I/O e aumentando o FPS efetivo.
# ==========================================================
class CameraReader:
    """
    Captura frames continuamente em background thread.
    Para webcam local aguarda inicialização antes de liberar o loop principal.
    Para RTSP usa CAP_FFMPEG com transporte TCP e configurações de baixa latência.
    """

    def __init__(self, source, warmup_timeout: float = 10.0):
        source_str = str(source)
        self.is_local = source_str.isdigit()

        if self.is_local:
            print(f"  Abrindo webcam local (index {source_str})...")
            self.cap = cv2.VideoCapture(int(source_str))
            time.sleep(1.0)
        else:
            print(f"  Abrindo stream RTSP: {source_str}...")
            self.cap = cv2.VideoCapture(source_str, cv2.CAP_FFMPEG)

        # Buffer de 1 frame: sempre descarta frames antigos e exibe o mais recente.
        # Fundamental para baixa latência em DVRs com alta compressão H.264.
        self.cap.set(cv2.CAP_PROP_BUFFERSIZE, 1)

        self.frame   = None
        self.sucesso = False
        self.lock    = threading.Lock()
        self._stop   = threading.Event()

        self._thread = threading.Thread(target=self._loop, daemon=True)
        self._thread.start()

        deadline = time.time() + warmup_timeout
        while time.time() < deadline:
            with self.lock:
                if self.sucesso and self.frame is not None:
                    break
            time.sleep(0.05)

    def _loop(self):
        while not self._stop.is_set():
            ok, frame = self.cap.read()
            with self.lock:
                self.sucesso = ok
                if ok and frame is not None:
                    self.frame = frame
            if not ok:
                time.sleep(0.05)

    def ler(self):
        with self.lock:
            return self.sucesso, (self.frame.copy() if self.frame is not None else None)

    def aberta(self):
        return self.cap.isOpened()

    def liberar(self):
        self._stop.set()
        self._thread.join(timeout=2)
        self.cap.release()


# ==========================================================
# 3. FUNÇÕES DE APOIO
# ==========================================================
def enviar_infracao_api(frame, camera_id, epis_faltantes, confianca):
    url = "http://localhost:5000/api/monitoramento" # URL temporária da futura API em C#
    try:
        _, buffer = cv2.imencode('.jpg', frame)
        dados = {"CameraId": camera_id, "EpisFaltantes": epis_faltantes, "NivelConfianca": confianca}
        arquivos = {"Imagem": ("infracao.jpg", buffer.tobytes(), "image/jpeg")}
        
        resposta = requests.post(url, data=dados, files=arquivos, timeout=5)
        if resposta.status_code == 200:
            print(f"  [API] Sucesso! C# recebeu a infração da {camera_id}.")
        else:
            print(f"  [API] Erro do servidor C#: {resposta.status_code}")
    except Exception as e:
        print(f"  [API] Backend C# offline: {e}")


def carregar_config() -> dict:
    """Lê o config.json do disco e retorna o dicionário de câmeras."""
    with open(CONFIG_PATH, 'r') as f:
        return json.load(f)["cameras"]


def mapear_ids_risco(config_camera: dict, modelo_names: dict) -> list:
    """
    Retorna IDs das classes de INFRAÇÃO ativas (sem capacete, sem óculos...).
    Usado para lógica de alerta e registro de violação.
    """
    mapeamento = {
        "require_helmet":        "sem capacete",
        "require_glasses":       "sem oculos",
        "require_gloves":        "sem luvas",
        "require_boots":         "sem bota",
        "require_ear_protection":"sem protetor auricular",
        "require_mask":          "sem mascara",
        "require_vest":          "sem colete",
    }
    ids_perigo = []
    for chave, exigido in config_camera.items():
        if exigido is True and chave in mapeamento:
            termo = mapeamento[chave]
            for id_cls, nome_cls in modelo_names.items():
                if termo in nome_cls.lower():
                    ids_perigo.append(id_cls)
    return ids_perigo


def mapear_ids_ok(config_camera: dict, modelo_names: dict) -> list:
    """
    Retorna IDs das classes de EPI PRESENTE ativas (capacete, óculos...).
    Usado apenas para exibição visual com caixa verde — não afeta alertas.
    Só inclui EPIs que estão marcados como obrigatórios no config da câmera:
    se require_helmet=False, o capacete não aparece mesmo quando detectado.
    """
    mapeamento_ok = {
        "require_helmet":         "capacete",
        "require_glasses":        "oculos",
        "require_gloves":         "luvas",
        "require_boots":          "bota",
        "require_ear_protection": "protetor auricular",
        "require_mask":           "mascara",
        "require_vest":           "colete",
    }
    ids_ok = []
    for chave, exigido in config_camera.items():
        if exigido is True and chave in mapeamento_ok:
            termo = mapeamento_ok[chave]
            for id_cls, nome_cls in modelo_names.items():
                nome = nome_cls.lower()
                # Garante que é a classe positiva: contém o termo mas NÃO contém "sem "
                if termo in nome and "sem " not in nome:
                    ids_ok.append(id_cls)
    return ids_ok


# ==========================================================
# 4. INICIALIZAÇÃO
# ==========================================================
print("\nCarregando modelo YOLO...")
modelo = YOLO(MODELO_PATH)

USE_GPU = torch.cuda.is_available()
device  = 0 if USE_GPU else "cpu"
half    = USE_GPU

if USE_GPU:
    gpu_name = torch.cuda.get_device_name(0)
    print(f"  Dispositivo : GPU — {gpu_name} (FP16 ativado)")
else:
    print("  Dispositivo : CPU (FP16 desativado)")

# ==========================================================
# 5. LOOP PRINCIPAL
# ==========================================================
ID_ATUAL   = "cam_0"
MAP_TECLAS = {ord('1'): "cam_0", ord('2'): "cam_1", ord('3'): "cam_2"}

CONFIG_RELOAD_INTERVAL = 30

# ── Cria a janela UMA ÚNICA VEZ ───────────────────────────
# WINDOW_NORMAL   : janela redimensionavel — o usuario maximiza pelo
#                   botao da propria janela do Windows normalmente.
# WINDOW_FREERATIO: desabilita o lock de aspect ratio do OpenCV,
#                   eliminando a area cinza quando a resolucao da camera
#                   (ex: 704x480 do DVR Intelbras) difere do monitor 16:9.
# Criar aqui, fora de qualquer loop, evita janelas duplicadas ao
# reconectar ou trocar de camera.
WINDOW_NAME = "SafeGuard IA - Monitor"
cv2.namedWindow(WINDOW_NAME, cv2.WINDOW_NORMAL | cv2.WINDOW_FREERATIO)

WINDOW_NAME = "SafeGuard IA - Monitor"
cv2.namedWindow(WINDOW_NAME, cv2.WINDOW_NORMAL | cv2.WINDOW_FREERATIO)

cv2.setWindowProperty(WINDOW_NAME, cv2.WND_PROP_FULLSCREEN, cv2.WINDOW_FULLSCREEN)

while True:
    try:
        if not os.path.exists(CONFIG_PATH):
            print(f"  [ERRO] Config nao encontrada: {CONFIG_PATH}")
            time.sleep(5)
            continue

        data_config = carregar_config()
        config      = data_config[ID_ATUAL]

        cam = CameraReader(config["source"])

        if not cam.aberta():
            print(f"  [AVISO] Nao foi possivel abrir: {config['nome']}. Nova tentativa em 3s...")
            cam.liberar()
            time.sleep(3)
            continue

        print(f"\n  Monitorando: {config['nome']}")
        IDs_PERIGO = mapear_ids_risco(config, modelo.names)
        IDs_OK     = mapear_ids_ok(config, modelo.names)

        contador_violacao  = 0
        tempo_ultima_foto  = 0.0
        COOLDOWN_FOTO      = 30
        LIMITE_CONFIRMACAO = 10

        FALHAS_MAXIMAS  = 20
        falhas_seguidas = 0

        trocar_camera  = False
        frame_contador = 0

        # ── Persistência de boxes ──────────────────────────
        # Guarda as últimas boxes detectadas e por quantos frames
        # elas ainda devem ser exibidas mesmo sem nova detecção.
        # Resolve o "piscar" causado pela confiança oscilando
        # perto do threshold entre frames consecutivos.
        PERSIST_FRAMES   = 6   # frames que a box fica visível após sumir
        boxes_persistidas = {}  # { classe_id: (box_tensor, conf, frames_restantes) }

        while cam.aberta():
            sucesso, frame = cam.ler()

            if not sucesso or frame is None:
                falhas_seguidas += 1
                if falhas_seguidas >= FALHAS_MAXIMAS:
                    print(f"  [AVISO] {FALHAS_MAXIMAS} frames perdidos consecutivos. Reiniciando captura...")
                    break
                time.sleep(0.05)
                continue

            falhas_seguidas  = 0
            frame_contador  += 1

            # ── MELHORIA DE IMAGEM POR TIPO DE FONTE ───────
            # O frame melhorado vai tanto para a inferência quanto para a exibição.
            # O YOLO vê a imagem com mais contraste e nitidez — melhora a
            # detecção de objetos pequenos como óculos e protetor auricular.
            if cam.is_local:
                frame_proc = melhorar_frame_webcam(frame)
            else:
                frame_proc = melhorar_frame_rtsp(frame)

            # ── RECARREGA CONFIG EM TEMPO REAL ─────────────
            # Relê o JSON a cada CONFIG_RELOAD_INTERVAL frames e
            # recalcula IDs_PERIGO incondicionalmente — sem comparação —
            # garantindo que qualquer mudança salva no painel web
            # seja aplicada imediatamente, mesmo em edge cases.
            if frame_contador % CONFIG_RELOAD_INTERVAL == 0:
                try:
                    data_config = carregar_config()
                    config      = data_config.get(ID_ATUAL, config)
                    IDs_PERIGO  = mapear_ids_risco(config, modelo.names)
                    IDs_OK      = mapear_ids_ok(config, modelo.names)
                except Exception as e:
                    print(f"  [AVISO] Falha ao reler config: {e}")

            # ── INFERÊNCIA ─────────────────────────────────
            # conf=0.15: captura óculos e protetores auriculares que antes
            #            eram descartados com conf 0.15–0.24.
            # imgsz=960 para RTSP: grid maior detecta objetos pequenos
            #            em câmeras de segurança com mais precisão.
            # imgsz=736 para webcam: já está em 720p, não precisa de grid maior.
            imgsz_atual = 736 if cam.is_local else 960
            resultados = modelo.predict(
                source=frame_proc,   # frame com CLAHE + unsharp aplicados
                conf=0.15,
                iou=0.45,
                imgsz=imgsz_atual,
                device=device,
                stream=True,
                verbose=False,
            )

            result = next(iter(resultados))

            classes_detectadas = result.boxes.cls.tolist() if result.boxes is not None else []
            confiancas         = result.boxes.conf.tolist() if result.boxes is not None else []

            # ── FILTRAGEM POR CONFIANÇA POR CLASSE ─────────
            # conf=0.15 global captura candidatos fracos, mas precisamos
            # filtrar individualmente para não mostrar falsos positivos:
            #   - Classes "sem X" (infrações): mínimo 0.30 — exige confiança razoável
            #   - Classes "X presente" (EPIs OK): mínimo 0.40 — mais exigente para
            #     evitar mostrar verde onde não tem EPI de fato
            #   - Classes sem relevância (pessoa, etc.): removidas
            CONF_INFRACOES = 0.30
            CONF_EPIS_OK   = 0.40

            IDs_RELEVANTES = set(int(x) for x in IDs_PERIGO + IDs_OK)

            if result.boxes is not None and len(result.boxes) > 0:
                mascara_final = []
                for i in range(len(result.boxes)):
                    cls_id   = int(result.boxes.cls[i].item())
                    conf_val = result.boxes.conf[i].item()
                    if cls_id not in IDs_RELEVANTES:
                        mascara_final.append(False)
                    elif cls_id in [int(x) for x in IDs_PERIGO]:
                        mascara_final.append(conf_val >= CONF_INFRACOES)
                    else:
                        mascara_final.append(conf_val >= CONF_EPIS_OK)
                result.boxes = result.boxes[mascara_final]

            # Recalcula após filtragem
            classes_detectadas = result.boxes.cls.tolist() if result.boxes is not None and len(result.boxes) > 0 else []
            confiancas         = result.boxes.conf.tolist() if result.boxes is not None and len(result.boxes) > 0 else []

            # ── PERSISTÊNCIA DE BOXES (anti-piscar) ────────
            # Chaves como int para evitar bug de comparação float vs int
            classes_agora = set(int(c) for c in classes_detectadas)

            if result.boxes is not None and len(result.boxes) > 0:
                for i, cls_id in enumerate(result.boxes.cls.tolist()):
                    boxes_persistidas[int(cls_id)] = (result.boxes[i], result.boxes.conf[i].item(), PERSIST_FRAMES)

            for cls_id in list(boxes_persistidas.keys()):
                if cls_id not in classes_agora:
                    box_obj, conf_val, restantes = boxes_persistidas[cls_id]
                    if restantes > 1:
                        boxes_persistidas[cls_id] = (box_obj, conf_val, restantes - 1)
                    else:
                        del boxes_persistidas[cls_id]

            if boxes_persistidas and result.boxes is not None:
                ids_so_fantasma = set(boxes_persistidas.keys()) - classes_agora
                if ids_so_fantasma:
                    boxes_extra = [boxes_persistidas[c][0] for c in ids_so_fantasma]
                    try:
                        result.boxes.data = torch.cat([
                            result.boxes.data,
                            torch.cat([b.data for b in boxes_extra], dim=0)
                        ], dim=0)
                    except Exception:
                        pass

            # result.plot() usa as cores padrão definidas pelo YOLO no treino
            # (cada classe tem sua cor automática — azul, verde, laranja etc.)
            frame_anotado = result.plot(img=frame_proc)

            # Só considera infração se o EPI estiver ativo no config atual
            infracoes_atuais = [
                modelo.names[int(c)]
                for c in classes_detectadas
                if c in IDs_PERIGO
            ]

            detecao_perigo = len(infracoes_atuais) > 0
            agora          = time.time()

            if detecao_perigo:
                contador_violacao += 1
                if (contador_violacao >= LIMITE_CONFIRMACAO
                        and (agora - tempo_ultima_foto) > COOLDOWN_FOTO):
                    
                    maior_conf = max(confiancas) if confiancas else 0.0
                    epis = ", ".join(set(infracoes_atuais))
                    
                    # Dispara o envio em background para não derrubar o FPS do vídeo
                    threading.Thread(
                        target=enviar_infracao_api, 
                        args=(frame_anotado.copy(), ID_ATUAL, epis, maior_conf), 
                        daemon=True
                    ).start()

                    print(f"  [ALERTA] Violação detectada! Enviando para a API...")
                    tempo_ultima_foto = agora
            else:
                contador_violacao = 0

            # ── STATUS OVERLAY ─────────────────────────────
            cor_status = (0, 0, 220) if detecao_perigo else (0, 200, 80)
            txt_status = (
                f"SafeGuard IA  |  {config['nome']}  |  "
                f"{'PERIGO' if detecao_perigo else 'OK'}"
            )
            cv2.putText(
                frame_anotado, txt_status,
                (15, 38),
                cv2.FONT_HERSHEY_SIMPLEX, 0.72,
                cor_status, 2, cv2.LINE_AA
            )

            cv2.imshow(WINDOW_NAME, frame_anotado)

            key = cv2.waitKey(1) & 0xFF

            if key == ord('q'):
                cam.liberar()
                cv2.destroyAllWindows()
                exit(0)

            if key in MAP_TECLAS:
                nova_id = MAP_TECLAS[key]
                if nova_id in data_config:
                    print(f"  Alternando para: {data_config[nova_id]['nome']}")
                    ID_ATUAL = nova_id
                    trocar_camera = True
                    break

        cam.liberar()

        if not trocar_camera:
            print("  Conexao perdida. Reconectando em 3s...")
            time.sleep(3)

    except Exception as e:
        print(f"  [ERRO] {type(e).__name__}: {e}")
        time.sleep(3)