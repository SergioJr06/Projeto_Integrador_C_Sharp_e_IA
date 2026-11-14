"""
Leitura e monitoramento do config/config.json (editado pelo painel web) e
publicação do status do motor de IA em config/status_motor.json.

Fica separado do script principal para poder ser testado sem câmera nem modelo.
"""
import json
import os
import time


class ConfigWatcher:
    """Detecta quando o config.json mudou (por data de modificação), no máximo 1x por intervalo."""

    def __init__(self, caminho, intervalo=1.0):
        self.caminho = caminho
        self.intervalo = intervalo
        self._mtime = None
        self._ultima_checagem = 0.0

    def carregar(self):
        mtime = os.path.getmtime(self.caminho)
        # Marca como visto ANTES de ler: um JSON inválido não é relido (e não gera erro) a cada segundo.
        self._mtime = mtime
        self._ultima_checagem = time.time()
        with open(self.caminho, "r", encoding="utf-8-sig") as f:
            dados = json.load(f)
        cameras = dados.get("cameras")
        if not isinstance(cameras, dict) or not cameras:
            raise ValueError("config.json sem nenhuma camera em 'cameras'")
        return dados

    def mudou(self):
        agora = time.time()
        if agora - self._ultima_checagem < self.intervalo:
            return False
        self._ultima_checagem = agora
        try:
            return os.path.getmtime(self.caminho) != self._mtime
        except OSError:
            return False


def decidir_camera(dados, atual, ativa_anterior):
    """
    Define qual câmera monitorar.
    - Se o campo 'ativa' do arquivo mudou (escolhido no painel), ele vence.
    - Senão, mantém a câmera atual (inclusive quando trocada pelo teclado).
    - Se a câmera atual foi removida, volta para 'ativa', 'cam_0' ou a primeira.
    """
    cameras = dados["cameras"]
    ativa = dados.get("ativa")
    if ativa != ativa_anterior and ativa in cameras:
        return ativa
    if atual in cameras:
        return atual
    if ativa in cameras:
        return ativa
    if "cam_0" in cameras:
        return "cam_0"
    return next(iter(cameras))


def epis_ativos(config_camera):
    return sorted(k for k, v in config_camera.items() if k.startswith("require_") and v is True)


def _escrever_json_atomico(caminho, dados):
    temp = caminho + ".tmp"
    for tentativa in range(3):
        try:
            with open(temp, "w", encoding="utf-8") as f:
                json.dump(dados, f, ensure_ascii=True)
            os.replace(temp, caminho)
            return True
        except OSError:
            time.sleep(0.05)
    return False


class StatusPublisher:
    """Publica o estado do motor (a cada ~2 s, ou na hora com forcar=True) para o painel exibir."""

    def __init__(self, caminho, intervalo=2.0):
        self.caminho = caminho
        self.intervalo = intervalo
        self._ultimo = 0.0

    def publicar(self, estado, id_camera, config_camera, revisao="", forcar=False):
        agora = time.time()
        if not forcar and agora - self._ultimo < self.intervalo:
            return
        self._ultimo = agora
        _escrever_json_atomico(self.caminho, {
            "online": True,
            "estado": estado,
            "camera": id_camera,
            "nome": str(config_camera.get("nome", id_camera)),
            "source": str(config_camera.get("source", "")),
            "epis": epis_ativos(config_camera),
            "revisao": str(revisao),
            "atualizado_em": agora,
        })

    def offline(self):
        _escrever_json_atomico(self.caminho, {"online": False, "atualizado_em": time.time()})
