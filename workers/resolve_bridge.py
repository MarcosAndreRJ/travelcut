# -*- coding: utf-8 -*-
"""
Bridge DaVinci Resolve (TravelCut Studio) — protocolo "resolve-bridge/1".

Uso (pelo app .NET, via ProcessRunner):
    python workers/resolve_bridge.py < pedido.json > resposta.json

- Lê UM pedido JSON do stdin e escreve UMA resposta JSON (uma linha) no stdout.
- Logs somente no stderr. Mensagens de erro contêm apenas nomes de arquivo, nunca caminhos completos.
- Dependências: somente stdlib + módulo DaVinciResolveScript fornecido pela instalação do Resolve.
- Segurança: nenhum comando abre projeto existente para escrita. Escrita só em projeto NOVO
  com nome iniciado por "TravelCut_" (o projeto novo é o atual; a troca só ocorre no apply_assembly).
  Leitura e exportação (query_timeline, export_project, render_preview) exigem que o projeto ATUAL do
  Resolve seja exatamente o pedido e comece com "TravelCut_"; senão PROJECT_NOT_CURRENT. Nunca há LoadProject
  nesses comandos.
  render_preview altera as configurações de render (e a timeline atual) somente do projeto TravelCut_* atual,
  nunca de projeto do usuário; é a única alteração em projeto existente, e só para projeto TravelCut_*.
- Confinamento: pedidos com caminho de saída levam allowedOutputRoot (<raiz>\3-Trabalho\editor); snapshot,
  exportação e render devem ficar sob essa raiz (PERMISSION_DENIED). Mídia só em <raiz>\1-Bruto ou <raiz>\2-Assets.

ESTADO: escrito e revisado por leitura. NÃO executado na VM de desenvolvimento (sem Python e sem Resolve).
Suposições da API (todas a confirmar no host, ver docs/18-ROTEIRO-HOMOLOGACAO-DAVINCI.md):
  - convenção de endFrame em AppendToTimeline (inclusiva ou exclusiva; controlada por endFrameInclusive);
  - recordFrame relativo a timeline.GetStartFrame();
  - ImportMedia com 29,97 fps e VFR; GetClipProperty("File Path") para casar item por caminho;
  - ExportProject(nome, caminho) e fluxo de render (SetRenderSettings, AddRenderJob, StartRendering).
"""

import json
import os
import sys
import time
import traceback

SCHEMA = "resolve-bridge/1"
MANIFEST_SCHEMA = "editor-assembly/1"
PROJECT_PREFIX = "TravelCut_"
MAX_PROJECT_NAME = 160

DEFAULT_API_DIR = os.path.join(
    os.environ.get("PROGRAMDATA") or r"C:\ProgramData",
    "Blackmagic Design", "DaVinci Resolve", "Support", "Developer", "Scripting")
DEFAULT_LIB_PATH = r"C:\Program Files\Blackmagic Design\DaVinci Resolve\fusionscript.dll"

# Métodos verificados pelo probe (hasattr). Ver docs/18 para a verificação no host.
PROBED_METHODS = (
    "CreateProject", "ImportMedia", "CreateEmptyTimeline", "AppendToTimeline",
    "GetItemListInTrack", "ExportProject", "AddRenderJob", "SetSetting",
    "SaveProject", "SetCurrentTimeline", "GetMediaPool", "SetRenderSettings",
    "StartRendering", "IsRenderingInProgress", "SetCurrentRenderFormatAndCodec",
)

# Caracteres proibidos em nomes de projeto (também evitam sair do diretório em nomes derivados).
FORBIDDEN_NAME_CHARS = ("/", "\\", ":", "*", "?", '"', "<", ">", "|")

SCRIPTING_DISABLED_MESSAGE = (
    "Scripting externo indisponível. Abra o DaVinci Resolve (Studio) e habilite "
    "Preferências > Sistema > Geral > \"External scripting using\" = \"Local\"; depois tente de novo."
)

NOT_INSTALLED_MESSAGE = (
    "Módulo DaVinciResolveScript não encontrado. Confirme a instalação do DaVinci Resolve "
    "e as variáveis RESOLVE_SCRIPT_API / RESOLVE_SCRIPT_LIB."
)


class BridgeError(Exception):
    """Erro controlado do bridge: código estável + mensagem sem caminhos completos."""

    def __init__(self, code, message):
        super(BridgeError, self).__init__(message)
        self.code = code
        self.message = message


def log(message):
    """Log de diagnóstico: somente stderr."""
    try:
        sys.stderr.write("[resolve_bridge] " + message + "\n")
        sys.stderr.flush()
    except Exception:
        pass


def now():
    return time.monotonic()


def is_int(value):
    return isinstance(value, int) and not isinstance(value, bool)


# ---------------------------------------------------------------------------
# Funções puras (testáveis sem Resolve)
# ---------------------------------------------------------------------------

def end_frame_for_api(start_frame, end_exclusive, end_inclusive):
    """Converte o fim EXCLUSIVO do manifesto para a convenção da API."""
    if end_exclusive <= start_frame:
        raise ValueError("intervalo vazio")
    return end_exclusive - 1 if end_inclusive else end_exclusive


def absolute_record_frame(timeline_start_frame, record_frame):
    """recordFrame do manifesto é relativo ao início da timeline; a API usa absoluto."""
    return int(timeline_start_frame) + int(record_frame)


def relative_record_frame(item_start_frame, timeline_start_frame):
    """Converte o início absoluto de um item da timeline para relativo ao início."""
    return int(item_start_frame) - int(timeline_start_frame)


def is_travelcut_project_name(name):
    """Nome de projeto aceito: prefixo TravelCut_, tamanho limitado e sem caracteres perigosos."""
    if not isinstance(name, str):
        return False
    if not name.startswith(PROJECT_PREFIX):
        return False
    if len(name) <= len(PROJECT_PREFIX) or len(name) > MAX_PROJECT_NAME:
        return False
    if any(ord(ch) < 32 for ch in name):
        return False
    if any(sep in name for sep in FORBIDDEN_NAME_CHARS):
        return False
    return True


def validate_manifest(manifest):
    """Valida o manifesto editor-assembly/1. Lança BridgeError com código estável."""
    if not isinstance(manifest, dict):
        raise BridgeError("INVALID_MANIFEST", "Manifesto inválido: não é um objeto JSON.")
    if manifest.get("schemaVersion") != MANIFEST_SCHEMA:
        raise BridgeError("CONTRACT_VERSION_MISMATCH", "schemaVersion do manifesto não suportado.")
    if not is_travelcut_project_name(manifest.get("editorProjectName")):
        raise BridgeError("PERMISSION_DENIED", "Nome de projeto sem prefixo TravelCut_ ou inválido.")
    timeline_name = manifest.get("timelineName")
    if not isinstance(timeline_name, str) or not timeline_name.strip():
        raise BridgeError("INVALID_MANIFEST", "timelineName ausente.")
    rate = manifest.get("timelineFrameRate")
    if not isinstance(rate, str) or not rate.strip():
        raise BridgeError("INVALID_MANIFEST", "timelineFrameRate ausente.")
    media = manifest.get("media")
    clips = manifest.get("clips")
    if not isinstance(media, list) or not isinstance(clips, list):
        raise BridgeError("INVALID_MANIFEST", "Listas media/clips ausentes.")
    if len(clips) == 0:
        raise BridgeError("NO_CLIPS", "Manifesto sem clipes.")

    media_ids = set()
    for item in media:
        if not isinstance(item, dict) or not item.get("mediaId") or not item.get("absolutePath"):
            raise BridgeError("INVALID_MANIFEST", "Entrada de mídia incompleta.")
        media_ids.add(str(item["mediaId"]))

    for clip in clips:
        if not isinstance(clip, dict):
            raise BridgeError("INVALID_MANIFEST", "Clipe inválido.")
        if not clip.get("clipId"):
            raise BridgeError("INVALID_MANIFEST", "Clipe sem clipId.")
        if str(clip.get("mediaId")) not in media_ids:
            raise BridgeError("INVALID_MANIFEST", "Clipe referencia mídia ausente do manifesto.")
        for key in ("trackIndex", "sourceStartFrame", "sourceEndFrameExclusive", "recordFrame", "durationFrames"):
            if not is_int(clip.get(key)):
                raise BridgeError("INVALID_MANIFEST", "Campo de frame inválido: " + key + ".")
        if clip["trackIndex"] < 1:
            raise BridgeError("INVALID_MANIFEST", "trackIndex deve ser >= 1.")
        if clip["sourceStartFrame"] < 0 or clip["sourceEndFrameExclusive"] <= clip["sourceStartFrame"]:
            raise BridgeError("INVALID_MANIFEST", "Intervalo de origem vazio ou negativo.")
        if clip["recordFrame"] < 0 or clip["durationFrames"] <= 0:
            raise BridgeError("INVALID_MANIFEST", "recordFrame negativo ou duração não positiva.")
    return manifest


def build_clip_info(clip, timeline_start_frame, end_inclusive):
    """Monta o dicionário clipInfo (sem mediaPoolItem) para AppendToTimeline."""
    start = int(clip["sourceStartFrame"])
    end_exclusive = int(clip["sourceEndFrameExclusive"])
    return {
        "startFrame": start,
        "endFrame": end_frame_for_api(start, end_exclusive, end_inclusive),
        "recordFrame": absolute_record_frame(timeline_start_frame, clip["recordFrame"]),
        "trackIndex": int(clip["trackIndex"]),
    }


def match_observed_items(expected, observed, tolerance=0):
    """
    Casa clipes esperados com itens observados na timeline.
    expected: [{clipId, trackIndex, recordFrame(relativo), durationFrames}]
    observed: [{trackIndex, recordFrame(relativo), durationFrames, ...}]
    Retorna lista alinhada a expected: {clipId, item (ou None), matches}.
    matches é tri-state:
      True  = posição, duração e in-point (leftOffset) conferidos;
      False = posição/trilha ausente ou duração/in-point divergente;
      None  = posição e duração batem, mas o in-point NÃO foi observado (leftOffset ausente).
              Não conta como conferido: a etapa fica PARTIAL_REAL com IN_POINT_NOT_VERIFIED.
    Casa por recordFrame (tolerância) e trilha. Cada item observado é usado no máximo uma vez.
    """
    used = set()
    results = []
    for exp in expected:
        found = None
        for index, obs in enumerate(observed):
            if index in used:
                continue
            if obs.get("trackIndex") != exp["trackIndex"]:
                continue
            if abs(int(obs["recordFrame"]) - int(exp["recordFrame"])) > tolerance:
                continue
            found = index
            break
        if found is None:
            results.append({"clipId": exp["clipId"], "item": None, "matches": False})
            continue
        used.add(found)
        obs = observed[found]
        duration_ok = abs(int(obs["durationFrames"]) - int(exp["durationFrames"])) <= tolerance
        observed_left = obs.get("leftOffsetFrames")
        expected_start = exp.get("sourceStartFrame")
        if not duration_ok:
            matches = False
        elif observed_left is None or expected_start is None:
            matches = None  # in-point não observado: não é conferido
        else:
            matches = abs(int(observed_left) - int(expected_start)) <= tolerance
        results.append({"clipId": exp["clipId"], "item": obs, "matches": matches})
    return results


def compute_final_status(failed, rejected_count, divergent_count):
    """
    READY_REAL: tudo ok e TODOS os matches verdadeiros; PARTIAL_REAL: rejeição, divergência ou in-point não
    verificado (o chamador conta como divergente todo match diferente de True); ERROR: falha de etapa.
    """
    if failed:
        return "ERROR"
    if rejected_count or divergent_count:
        return "PARTIAL_REAL"
    return "READY_REAL"


def summarize_probe_status(methods):
    """READY se nenhum método confirmado ausente; READY_PARTIAL se algum False. None = não verificável."""
    if any(value is False for value in methods.values()):
        return "READY_PARTIAL"
    return "READY"


def build_render_settings(output_dir, custom_name):
    return {
        "SelectAllFrames": True,
        "TargetDir": output_dir,
        "CustomName": custom_name,
    }


PREVIEW_NAME_PREFIX = "TravelCut_preview"


def preview_custom_name(utc_time=None):
    """CustomName único por job de render: TravelCut_preview_<utc yyyymmddThhmmss>."""
    return PREVIEW_NAME_PREFIX + "_" + time.strftime("%Y%m%dT%H%M%S", utc_time or time.gmtime())


def find_new_output(output_dir, before_names, extension=".mp4", name_prefix=None):
    """
    Arquivo produzido após o início do render (o mais recente com a extensão). Com name_prefix, só conta
    arquivos cujo nome começa com ele (o CustomName do job), para não pegar saídas de outros renders.
    """
    candidates = []
    for name in os.listdir(output_dir):
        if name in before_names or not name.lower().endswith(extension):
            continue
        if name_prefix is not None and not name.startswith(name_prefix):
            continue
        candidates.append(os.path.join(output_dir, name))
    if not candidates:
        return None
    return max(candidates, key=os.path.getmtime)


def read_track_items(raw_items, track_index, timeline_start_frame):
    """Converte itens crus (dicts já lidos da API) em itens relativos, sem depender da API."""
    observed = []
    for raw in raw_items or []:
        start = raw.get("start")
        if not is_int(start):
            continue
        observed.append({
            "editorItemId": raw.get("uniqueId") if isinstance(raw.get("uniqueId"), str) else None,
            "trackIndex": track_index,
            "recordFrame": relative_record_frame(start, timeline_start_frame),
            "durationFrames": raw.get("duration") if is_int(raw.get("duration")) else None,
            "leftOffsetFrames": raw.get("leftOffset") if is_int(raw.get("leftOffset")) else None,
            "inPointObserved": is_int(raw.get("leftOffset")),
            "mediaName": os.path.basename(raw["name"]) if isinstance(raw.get("name"), str) else None,
        })
    return observed


def normalize_path(path):
    return os.path.normcase(os.path.normpath(path))


# ---------------------------------------------------------------------------
# Confinamento de caminhos (sem tocar disco além de realpath)
# ---------------------------------------------------------------------------

ARTIFACT_TAIL = os.path.join("3-Trabalho", "editor")


def validate_allowed_root(root):
    """allowedOutputRoot: absoluto e terminando em 3-Trabalho\\editor (case-insensitive). Devolve o caminho real."""
    if not isinstance(root, str) or not root or not os.path.isabs(root):
        raise BridgeError("INVALID_REQUEST", "allowedOutputRoot obrigatório (caminho absoluto de 3-Trabalho/editor).")
    real = os.path.realpath(root)
    tail = os.path.normcase(os.sep + ARTIFACT_TAIL)
    if not os.path.normcase(real).endswith(tail):
        raise BridgeError("PERMISSION_DENIED", "allowedOutputRoot deve terminar em 3-Trabalho\\editor.")
    return real


def path_is_inside(path, folder):
    """Caminho real dentro de folder (estrito: a própria pasta não conta). Comparação case-insensitive no Windows."""
    real_path = os.path.normcase(os.path.realpath(path))
    real_folder = os.path.normcase(os.path.realpath(folder)).rstrip(os.sep) + os.sep
    return real_path.startswith(real_folder)


def require_inside_root(path, allowed_root, key):
    if not path_is_inside(path, allowed_root):
        raise BridgeError("PERMISSION_DENIED", key + " fora de 3-Trabalho\\editor do projeto.")
    return path


def media_folders_for(allowed_root):
    """1-Bruto e 2-Assets da mesma raiz de projeto (pai de 3-Trabalho)."""
    project_root = os.path.dirname(os.path.dirname(allowed_root))
    return [os.path.join(project_root, "1-Bruto"), os.path.join(project_root, "2-Assets")]


def media_is_allowed(absolute_path, folders):
    if folders is None:
        return False  # sem raiz permitida: fecha por padrão
    return any(path_is_inside(absolute_path, folder) for folder in folders)


# ---------------------------------------------------------------------------
# Acesso ao Resolve (só aqui há dependência do DaVinciResolveScript)
# ---------------------------------------------------------------------------

def load_resolve_module():
    api_dir = os.environ.get("RESOLVE_SCRIPT_API") or DEFAULT_API_DIR
    lib_path = os.environ.get("RESOLVE_SCRIPT_LIB") or DEFAULT_LIB_PATH
    os.environ.setdefault("RESOLVE_SCRIPT_API", api_dir)
    os.environ.setdefault("RESOLVE_SCRIPT_LIB", lib_path)
    modules_dir = os.path.join(api_dir, "Modules")
    if os.path.isdir(modules_dir) and modules_dir not in sys.path:
        sys.path.insert(0, modules_dir)
    try:
        import DaVinciResolveScript as dvr  # noqa: E402  (importação tardia por design)
    except ImportError:
        raise BridgeError("NOT_INSTALLED", NOT_INSTALLED_MESSAGE)
    except Exception as exc:
        raise BridgeError("NOT_INSTALLED", NOT_INSTALLED_MESSAGE + " (" + type(exc).__name__ + ")")
    return dvr


def connect_resolve():
    dvr = load_resolve_module()
    resolve = dvr.scriptapp("Resolve")
    if resolve is None:
        raise BridgeError("SCRIPTING_DISABLED", SCRIPTING_DISABLED_MESSAGE)
    return resolve


def safe_call(obj, name, *args):
    """Chama método se existir; exceções viram None (uso só no probe e leituras opcionais)."""
    if obj is None:
        return None
    function = getattr(obj, name, None)
    if function is None:
        return None
    try:
        return function(*args)
    except Exception:
        return None


def method_presence(obj, name):
    """True/False conforme hasattr; None quando o objeto não pode ser obtido sem projeto aberto."""
    if obj is None:
        return None
    return hasattr(obj, name)


def read_version(resolve):
    version = safe_call(resolve, "GetVersionString")
    if isinstance(version, str) and version:
        return version
    parts = safe_call(resolve, "GetVersion")
    if isinstance(parts, (list, tuple)) and parts:
        return ".".join(str(part) for part in parts)
    return None


def find_timeline(project, timeline_name):
    count = safe_call(project, "GetTimelineCount")
    if not is_int(count):
        return None
    for index in range(1, count + 1):  # índice 1-based na API
        timeline = safe_call(project, "GetTimelineByIndex", index)
        if timeline is not None and safe_call(timeline, "GetName") == timeline_name:
            return timeline
    return None


def read_raw_track_items(timeline, track_index):
    """Lê itens da trilha de vídeo e devolve dicts crus para read_track_items."""
    items = safe_call(timeline, "GetItemListInTrack", "video", track_index) or []
    raw_items = []
    for item in items:
        left = safe_call(item, "GetLeftOffset") if hasattr(item, "GetLeftOffset") else None
        raw_items.append({
            "start": safe_call(item, "GetStart"),
            "duration": safe_call(item, "GetDuration"),
            "leftOffset": left,
            "uniqueId": safe_call(item, "GetUniqueId") if hasattr(item, "GetUniqueId") else None,
            "name": safe_call(item, "GetName"),
        })
    return raw_items


# ---------------------------------------------------------------------------
# Estado do apply_assembly (etapas, rejeições, mapeamento)
# ---------------------------------------------------------------------------

PLANNED_STEPS = (
    "create_project",
    "set_timeline_frame_rate",
    "import_media",
    "create_timeline",
    "apply_edit_plan",
    "query_timeline",
    "save_project",
    "create_snapshot",
)


class ApplyState(object):
    def __init__(self, manifest, snapshot_path, end_inclusive, allowed_root):
        self.manifest = manifest
        self.project_name = manifest["editorProjectName"]
        self.timeline_name = manifest["timelineName"]
        self.frame_rate = manifest["timelineFrameRate"]
        self.media = manifest["media"]
        self.clips = manifest["clips"]
        self.snapshot_path = snapshot_path
        self.end_inclusive = end_inclusive
        self.allowed_root = allowed_root
        self.media_folders = media_folders_for(allowed_root) if allowed_root else None
        self.steps = []
        self.rejected = []
        self.warnings = []
        self.mappings = []
        self.warning_clip_ids = set()
        self.project = None
        self.timeline = None
        self.timeline_start = None
        self.media_items = {}
        self.missing_media = set()
        self.media_codes = {}
        self.appended_clips = []
        self.snapshot_absolute = None
        self.editor_version = None
        self.error_code = None
        self.error_message = None
        self.resolve = None

    def add_step(self, operation, status, message, started):
        self.steps.append({
            "operation": operation,
            "status": status,
            "message": message,
            "durationMs": int((now() - started) * 1000),
        })

    def reject_clip(self, clip_id, code, message):
        self.rejected.append({"kind": "clip", "clipId": clip_id, "code": code, "message": message})

    def warn(self, code, message, clip_id=None):
        self.warnings.append({"code": code, "message": message, "clipId": clip_id})

    def finalize_skipped(self):
        """Etapas planejadas que não executaram ficam como 'skipped' com motivo."""
        done = set(step["operation"] for step in self.steps)
        for operation in PLANNED_STEPS:
            if operation in done:
                continue
            if operation == "create_snapshot" and self.snapshot_path is None:
                message = "Snapshot não solicitado."
            else:
                message = "Não executado por falha em etapa anterior."
            self.steps.append({"operation": operation, "status": "skipped", "message": message, "durationMs": None})

    def divergent_count(self):
        return sum(1 for mapping in self.mappings if mapping.get("matches") is not True)

    def to_response(self):
        if self.error_code is not None:
            status = "ERROR"
        else:
            status = compute_final_status(
                failed=any(step["status"] == "failed" for step in self.steps),
                rejected_count=len(self.rejected),
                divergent_count=self.divergent_count())
        ok = status != "ERROR"
        response = {
            "ok": ok,
            "status": status,
            "errorCode": None if ok else (self.error_code or "APPLY_FAILED"),
            "errorMessage": None if ok else (self.error_message or "Falha ao aplicar o manifesto."),
            "editorProjectName": self.project_name,
            "timelineName": self.timeline_name,
            "editorVersion": self.editor_version,
            "steps": self.steps,
            "clipMappings": self.mappings,
            "rejected": self.rejected,
            "warnings": self.warnings,
            "snapshotAbsolutePath": self.snapshot_absolute,
        }
        return response


def run_step(state, operation, function):
    """Executa uma etapa crítica. function() -> (valor, mensagem); BridgeError registra falha e propaga."""
    started = now()
    try:
        value, message = function()
    except BridgeError as exc:
        state.add_step(operation, "failed", exc.message, started)
        raise
    except Exception as exc:
        # Exceção crua da API vira falha registrada da etapa (nunca "skipped" por engano).
        message = "Exceção " + type(exc).__name__ + " na etapa " + operation + "; veja o log do bridge."
        log(operation + " exceção " + type(exc).__name__)
        log(traceback.format_exc())
        state.add_step(operation, "failed", message, started)
        raise BridgeError("BRIDGE_EXCEPTION", message)
    state.add_step(operation, "ok", message, started)
    return value


def apply_flow(state):
    resolve = connect_resolve()
    state.resolve = resolve
    pm = safe_call(resolve, "GetProjectManager")
    if pm is None:
        raise BridgeError("PROJECT_MANAGER_UNAVAILABLE", "Gerenciador de projetos do Resolve indisponível.")

    def create_project():
        project = pm.CreateProject(state.project_name)
        if not project:
            raise BridgeError(
                "PROJECT_EXISTS",
                "Não foi possível criar o projeto novo '" + state.project_name + "': nome já existe "
                "ou criação recusada. Nenhum projeto existente foi aberto.")
        note = "Projeto novo criado."
        current = safe_call(pm, "GetCurrentProject")
        if safe_call(current, "GetName") != state.project_name:
            # Troca de projeto já confirmada pelo usuário no apply: carrega explicitamente o projeto novo.
            loaded = pm.LoadProject(state.project_name)
            if not loaded:
                raise BridgeError("PROJECT_NOT_CURRENT", "Projeto novo criado, mas não ficou como atual.")
            project = loaded
            note = "Projeto novo criado e carregado como atual (troca confirmada no apply)."
        return project, note

    state.project = run_step(state, "create_project", create_project)

    def set_frame_rate():
        result = state.project.SetSetting("timelineFrameRate", state.frame_rate)
        if not result:
            raise BridgeError("SET_SETTING_FAILED", "Resolve recusou timelineFrameRate=" + state.frame_rate + ".")
        return True, "timelineFrameRate=" + state.frame_rate

    run_step(state, "set_timeline_frame_rate", set_frame_rate)

    def import_media():
        media_pool = state.project.GetMediaPool()
        if media_pool is None:
            raise BridgeError("MEDIA_POOL_UNAVAILABLE", "Media pool indisponível no projeto novo.")
        paths = []
        for entry in state.media:
            path = entry["absolutePath"]
            media_id = str(entry["mediaId"])
            if not media_is_allowed(path, state.media_folders):
                # Mídia fora de 1-Bruto/2-Assets da raiz do projeto: não importa e o clipe é recusado com PERMISSION_DENIED.
                state.missing_media.add(media_id)
                state.media_codes[media_id] = "PERMISSION_DENIED"
                state.warn("PERMISSION_DENIED", "Mídia fora de 1-Bruto/2-Assets: " + os.path.basename(path), None)
                continue
            if os.path.isfile(path):
                paths.append(path)
            else:
                state.missing_media.add(media_id)
                state.media_codes[media_id] = "MEDIA_NOT_FOUND"
                state.warn("MEDIA_NOT_FOUND", "Arquivo ausente: " + os.path.basename(path), None)
        imported = media_pool.ImportMedia(paths) if paths else []
        by_path = {}
        for item in imported or []:
            reported = safe_call(item, "GetClipProperty", "File Path")
            if isinstance(reported, str) and reported:
                by_path[normalize_path(reported)] = item
        for entry in state.media:
            media_id = str(entry["mediaId"])
            if media_id in state.missing_media:
                continue
            item = by_path.get(normalize_path(entry["absolutePath"]))
            if item is None:
                state.missing_media.add(media_id)
                state.warn("MEDIA_NOT_FOUND", "Mídia não importada: " + os.path.basename(entry["absolutePath"]), None)
            else:
                state.media_items[media_id] = item
        # Sem mídia importada, não falha aqui: apply_clips recusa cada clipe com o código por mídia (por clipe).
        total = len(state.media)
        found = len(state.media_items)
        return media_pool, "%d de %d mídias importadas." % (found, total)

    media_pool = run_step(state, "import_media", import_media)

    def create_timeline():
        timeline = media_pool.CreateEmptyTimeline(state.timeline_name)
        if not timeline:
            raise BridgeError("TIMELINE_CREATE_FAILED", "Resolve não criou a timeline '" + state.timeline_name + "'.")
        if state.project.SetCurrentTimeline(timeline) is False:
            raise BridgeError("TIMELINE_CREATE_FAILED", "Não foi possível tornar a timeline atual.")
        start = safe_call(timeline, "GetStartFrame")
        if not is_int(start):
            raise BridgeError("TIMELINE_START_UNKNOWN", "GetStartFrame() não retornou frame inteiro.")
        state.timeline = timeline
        state.timeline_start = start
        return True, "Timeline criada; início absoluto=%d." % start

    run_step(state, "create_timeline", create_timeline)

    def apply_clips():
        for clip in state.clips:
            clip_id = str(clip["clipId"])
            media_id = str(clip["mediaId"])
            if media_id in state.missing_media or media_id not in state.media_items:
                state.reject_clip(clip["clipId"], state.media_codes.get(media_id, "MEDIA_NOT_FOUND"),
                                  "Mídia ausente, fora de 1-Bruto/2-Assets ou não importada; clipe não aplicado.")
                continue
            info = build_clip_info(clip, state.timeline_start, state.end_inclusive)
            info["mediaPoolItem"] = state.media_items[media_id]
            try:
                result = media_pool.AppendToTimeline([info])
            except Exception as exc:
                state.reject_clip(clip["clipId"], "CLIP_APPEND_FAILED",
                                  "Resolve recusou o clipe (" + type(exc).__name__ + ").")
                log("append falhou para clipe " + clip_id + ": " + type(exc).__name__)
                continue
            if not result:
                state.reject_clip(clip["clipId"], "CLIP_APPEND_FAILED", "Resolve não aplicou o clipe.")
                continue
            state.appended_clips.append(clip)
        if not state.appended_clips:
            raise BridgeError("APPLY_FAILED", "Nenhum clipe foi aplicado à timeline.")
        return True, "%d de %d clipes aplicados." % (len(state.appended_clips), len(state.clips))

    run_step(state, "apply_edit_plan", apply_clips)

    def query_timeline_step():
        track_indices = sorted(set(int(clip["trackIndex"]) for clip in state.appended_clips))
        observed = []
        for track in track_indices:
            observed.extend(read_track_items(
                read_raw_track_items(state.timeline, track), track, state.timeline_start))
        expected = []
        for clip in state.appended_clips:
            expected.append({
                "clipId": clip["clipId"],
                "trackIndex": int(clip["trackIndex"]),
                "recordFrame": int(clip["recordFrame"]),
                "durationFrames": int(clip["durationFrames"]),
                "sourceStartFrame": int(clip["sourceStartFrame"]),
            })
        matched = match_observed_items(expected, observed)
        for entry in matched:
            item = entry["item"]
            state.mappings.append({
                "clipId": entry["clipId"],
                "editorItemId": item["editorItemId"] if item else None,
                "observedRecordFrame": item["recordFrame"] if item else None,
                "observedDurationFrames": item["durationFrames"] if item else None,
                "leftOffsetFrames": item["leftOffsetFrames"] if item else None,
                "matches": entry["matches"],
            })
            if entry["matches"] is False:
                state.warn("TIMELINE_DIVERGENCE",
                           "Clipe não confere com a timeline observada (posição/duração/in-point).", entry["clipId"])
            elif entry["matches"] is None:
                state.warn("IN_POINT_NOT_VERIFIED",
                           "Posição e duração batem, mas o in-point não foi observado (leftOffset não exposto pela API).",
                           entry["clipId"])
        verified_count = sum(1 for entry in matched if entry["matches"] is True)
        unverified_count = sum(1 for entry in matched if entry["matches"] is None)
        if unverified_count:
            # Sem a palavra "conferidos": in-point não verificado não é conferência completa.
            return True, ("Clipes com posição, duração e in-point verificados: %d de %d; "
                          "in-point não observado em %d (não verificado)."
                          % (verified_count, len(matched), unverified_count))
        return True, "%d de %d clipes conferidos na timeline." % (verified_count, len(matched))

    run_step(state, "query_timeline", query_timeline_step)

    def save_project():
        # Sem LoadProject aqui: só salva se o projeto novo já é o atual (create_project cuida da troca).
        current = safe_call(pm, "GetCurrentProject")
        if safe_call(current, "GetName") != state.project_name:
            raise BridgeError("PROJECT_NOT_CURRENT", "Projeto novo não está como atual; nada foi salvo.")
        if not pm.SaveProject():
            raise BridgeError("SAVE_FAILED", "Resolve não salvou o projeto novo.")
        return True, "Projeto salvo."

    run_step(state, "save_project", save_project)

    if state.snapshot_path is None:
        state.add_step("create_snapshot", "skipped", "Snapshot não solicitado.", now())
    else:
        def create_snapshot():
            if not pm.ExportProject(state.project_name, state.snapshot_path):
                raise BridgeError("SNAPSHOT_FAILED", "Resolve não exportou o snapshot do projeto novo.")
            if not os.path.isfile(state.snapshot_path):
                raise BridgeError("SNAPSHOT_FAILED", "Arquivo de snapshot não foi gerado.")
            state.snapshot_absolute = state.snapshot_path
            return state.snapshot_path, "Snapshot exportado (" + os.path.basename(state.snapshot_path) + ")."

        run_step(state, "create_snapshot", create_snapshot)

    state.editor_version = read_version(resolve)


def apply_assembly(manifest, snapshot_path, end_inclusive, allowed_root=None):
    state = ApplyState(manifest, snapshot_path, end_inclusive, allowed_root)
    try:
        apply_flow(state)
    except BridgeError as exc:
        state.error_code = exc.code
        state.error_message = exc.message
    except Exception as exc:
        log("apply_assembly exceção " + type(exc).__name__)
        log(traceback.format_exc())
        state.error_code = "BRIDGE_EXCEPTION"
        state.error_message = "Exceção " + type(exc).__name__ + " durante a aplicação; veja o log do bridge."
    state.finalize_skipped()
    return state.to_response()


# ---------------------------------------------------------------------------
# Comandos
# ---------------------------------------------------------------------------

def required_str(req, key):
    value = req.get(key)
    if not isinstance(value, str) or not value.strip():
        raise BridgeError("INVALID_REQUEST", "Campo obrigatório ausente: " + key + ".")
    return value


def optional_str(req, key):
    value = req.get(key)
    if value is None:
        return None
    if not isinstance(value, str) or not value.strip():
        raise BridgeError("INVALID_REQUEST", "Campo inválido: " + key + ".")
    return value


def require_absolute(path, key):
    if not os.path.isabs(path):
        raise BridgeError("INVALID_REQUEST", key + " deve ser um caminho absoluto.")
    return path


def require_travelcut_name(name):
    if not is_travelcut_project_name(name):
        raise BridgeError("PERMISSION_DENIED", "Somente projetos com prefixo TravelCut_ podem ser acessados.")
    return name


def cmd_probe(req):
    resolve = connect_resolve()
    product = safe_call(resolve, "GetProductName")
    version = read_version(resolve)
    pm = safe_call(resolve, "GetProjectManager")
    current = safe_call(pm, "GetCurrentProject")
    open_name = safe_call(current, "GetName") if current is not None else None
    media_pool = safe_call(current, "GetMediaPool") if current is not None else None
    timeline = safe_call(current, "GetCurrentTimeline") if current is not None else None

    sources = {
        "CreateProject": pm, "ExportProject": pm, "SaveProject": pm,
        "ImportMedia": media_pool, "CreateEmptyTimeline": media_pool, "AppendToTimeline": media_pool,
        "GetItemListInTrack": timeline,
        "SetSetting": current, "AddRenderJob": current,
        "SetCurrentTimeline": current, "GetMediaPool": current, "SetRenderSettings": current,
        "StartRendering": current, "IsRenderingInProgress": current,
        "SetCurrentRenderFormatAndCodec": current,
    }
    methods = {}
    for name in PROBED_METHODS:
        methods[name] = method_presence(sources.get(name), name)

    return {
        "ok": True,
        "status": summarize_probe_status(methods),
        "productName": product if isinstance(product, str) else None,
        "version": version,
        "pythonVersion": ".".join(str(part) for part in sys.version_info[:3]),
        # Só para avisar que trocar de projeto acontecerá em operações de escrita; nada é salvo ou alterado.
        "openProjectName": open_name if isinstance(open_name, str) else None,
        "methods": methods,
        "methodsNote": "null = não verificável sem projeto aberto (Project/MediaPool/Timeline).",
    }


def read_manifest(path):
    require_absolute(path, "manifestPath")
    if not os.path.isfile(path):
        raise BridgeError("MANIFEST_NOT_FOUND", "Manifesto não encontrado (" + os.path.basename(path) + ").")
    try:
        with open(path, "r", encoding="utf-8") as handle:
            return json.load(handle)
    except ValueError:
        raise BridgeError("INVALID_MANIFEST", "Manifesto não é JSON válido.")


def cmd_apply_assembly(req):
    manifest_path = required_str(req, "manifestPath")
    snapshot_path = optional_str(req, "snapshotPath")
    end_inclusive = req.get("endFrameInclusive", True)
    if not isinstance(end_inclusive, bool):
        raise BridgeError("INVALID_REQUEST", "endFrameInclusive deve ser booleano.")
    if snapshot_path is not None:
        require_absolute(snapshot_path, "snapshotPath")
    # Confinamento ANTES de ler o manifesto: ele precisa estar dentro de <projeto>\3-Trabalho\editor.
    allowed_root = validate_allowed_root(req.get("allowedOutputRoot"))
    require_absolute(manifest_path, "manifestPath")
    require_inside_root(manifest_path, allowed_root, "manifestPath")
    if snapshot_path is not None:
        require_inside_root(snapshot_path, allowed_root, "snapshotPath")
    manifest = validate_manifest(read_manifest(manifest_path))
    return apply_assembly(manifest, snapshot_path, end_inclusive, allowed_root)


def require_current_project(pm, project_name):
    """
    Leitura/exportação/render só atuam no projeto ATUAL do Resolve, quando ele é exatamente o pedido e começa
    com TravelCut_. Nunca troca de projeto (sem LoadProject). Senão PROJECT_NOT_CURRENT.
    """
    current = safe_call(pm, "GetCurrentProject")
    current_name = safe_call(current, "GetName") if current is not None else None
    if not isinstance(current_name, str) or current_name != project_name or not is_travelcut_project_name(current_name):
        raise BridgeError("PROJECT_NOT_CURRENT",
                          "Abra o projeto TravelCut_* no Resolve (ele precisa ser o projeto atual) e tente de novo.")
    return current


def cmd_query_timeline(req):
    project_name = require_travelcut_name(required_str(req, "projectName"))
    timeline_name = required_str(req, "timelineName")
    resolve = connect_resolve()
    pm = safe_call(resolve, "GetProjectManager")
    if pm is None:
        raise BridgeError("PROJECT_MANAGER_UNAVAILABLE", "Gerenciador de projetos indisponível.")
    project = require_current_project(pm, project_name)
    timeline = find_timeline(project, timeline_name)
    if timeline is None:
        raise BridgeError("TIMELINE_NOT_FOUND", "Timeline não encontrada no projeto TravelCut_.")
    start = safe_call(timeline, "GetStartFrame")
    if not is_int(start):
        raise BridgeError("TIMELINE_START_UNKNOWN", "GetStartFrame() não retornou frame inteiro.")
    items = read_track_items(read_raw_track_items(timeline, 1), 1, start)
    return {
        "status": "READY_REAL",
        "editorProjectName": project_name,
        "timelineName": timeline_name,
        "timelineStartFrame": start,
        "items": items,
    }


def cmd_export_project(req):
    project_name = require_travelcut_name(required_str(req, "projectName"))
    output_path = require_absolute(required_str(req, "outputPath"), "outputPath")
    if not output_path.lower().endswith(".drp"):
        raise BridgeError("INVALID_REQUEST", "outputPath deve terminar em .drp.")
    allowed_root = validate_allowed_root(req.get("allowedOutputRoot"))
    require_inside_root(output_path, allowed_root, "outputPath")
    resolve = connect_resolve()
    pm = safe_call(resolve, "GetProjectManager")
    if pm is None:
        raise BridgeError("PROJECT_MANAGER_UNAVAILABLE", "Gerenciador de projetos indisponível.")
    require_current_project(pm, project_name)
    if not pm.ExportProject(project_name, output_path) or not os.path.isfile(output_path):
        raise BridgeError("EXPORT_FAILED", "Resolve não exportou o projeto (" + os.path.basename(output_path) + ").")
    return {"status": "READY_REAL", "outputAbsolutePath": output_path}


def cmd_render_preview(req):
    project_name = require_travelcut_name(required_str(req, "projectName"))
    timeline_name = required_str(req, "timelineName")
    output_dir = require_absolute(required_str(req, "outputDir"), "outputDir")
    timeout_sec = req.get("timeoutSec", 1800)
    if not is_int(timeout_sec) or not (30 <= timeout_sec <= 7200):
        raise BridgeError("INVALID_REQUEST", "timeoutSec deve estar entre 30 e 7200.")
    allowed_root = validate_allowed_root(req.get("allowedOutputRoot"))
    require_inside_root(output_dir, allowed_root, "outputDir")

    resolve = connect_resolve()
    pm = safe_call(resolve, "GetProjectManager")
    if pm is None:
        raise BridgeError("PROJECT_MANAGER_UNAVAILABLE", "Gerenciador de projetos indisponível.")
    project = require_current_project(pm, project_name)
    timeline = find_timeline(project, timeline_name)
    if timeline is None:
        raise BridgeError("TIMELINE_NOT_FOUND", "Timeline não encontrada no projeto TravelCut_.")
    if project.SetCurrentTimeline(timeline) is False:
        raise BridgeError("TIMELINE_NOT_CURRENT", "Não foi possível tornar a timeline atual para o render.")

    if hasattr(project, "SetCurrentRenderFormatAndCodec"):
        if not project.SetCurrentRenderFormatAndCodec("mp4", "H264"):
            raise BridgeError("RENDER_SETTINGS_FAILED", "Formato mp4/H264 recusado pelo Resolve.")

    os.makedirs(output_dir, exist_ok=True)
    custom_name = preview_custom_name()
    if not project.SetRenderSettings(build_render_settings(output_dir, custom_name)):
        raise BridgeError("RENDER_SETTINGS_FAILED", "Resolve recusou as configurações de render.")

    before = set(os.listdir(output_dir))
    job_id = project.AddRenderJob()
    if not job_id:
        raise BridgeError("RENDER_JOB_FAILED", "Resolve não criou o job de render.")
    if not project.StartRendering([job_id]):
        raise BridgeError("RENDER_START_FAILED", "Resolve não iniciou o render.")

    started = now()
    while project.IsRenderingInProgress():
        if now() - started > timeout_sec:
            try:
                project.StopRendering()
            except Exception:
                log("StopRendering falhou após timeout")
            raise BridgeError("TIMEOUT", "Render excedeu o tempo limite configurado.")
        time.sleep(1.0)

    job_status = safe_call(project, "GetRenderJobStatus", job_id)
    if isinstance(job_status, dict) and str(job_status.get("JobStatus", "")).lower() == "failed":
        raise BridgeError("RENDER_FAILED", "Resolve reportou falha no job de render.")

    output = find_new_output(output_dir, before, name_prefix=custom_name)
    if output is None:
        raise BridgeError("RENDER_NO_OUTPUT", "Render terminou sem arquivo .mp4 novo no diretório de saída.")
    return {"status": "READY_REAL", "outputAbsolutePath": output}


COMMANDS = {
    "probe": cmd_probe,
    "apply_assembly": cmd_apply_assembly,
    "query_timeline": cmd_query_timeline,
    "export_project": cmd_export_project,
    "render_preview": cmd_render_preview,
}


def error_response(code, message):
    return {
        "schemaVersion": SCHEMA,
        "ok": False,
        "status": "ERROR",
        "errorCode": code,
        "errorMessage": message,
    }


def process_request(raw_text):
    """Interpreta o pedido e devolve o dicionário de resposta (sem I/O)."""
    try:
        req = json.loads(raw_text)
    except ValueError:
        return error_response("INVALID_REQUEST", "Pedido não é JSON válido.")
    if not isinstance(req, dict):
        return error_response("INVALID_REQUEST", "Pedido deve ser um objeto JSON.")
    if req.get("schemaVersion") != SCHEMA:
        return error_response("UNSUPPORTED_SCHEMA", "schemaVersion do pedido não suportado.")
    command = req.get("command")
    handler = COMMANDS.get(command) if isinstance(command, str) else None
    if handler is None:
        return error_response("UNKNOWN_COMMAND", "Comando desconhecido.")
    try:
        body = handler(req)
    except BridgeError as exc:
        return error_response(exc.code, exc.message)
    except Exception as exc:
        log("comando " + str(command) + " exceção " + type(exc).__name__)
        log(traceback.format_exc())
        return error_response(
            "BRIDGE_EXCEPTION",
            "Exceção " + type(exc).__name__ + " ao executar o comando; veja o log do bridge (stderr).")

    response = {"schemaVersion": SCHEMA, "ok": True, "status": "READY_REAL",
                "errorCode": None, "errorMessage": None}
    response.update(body)
    return response


def main():
    real_stdout = sys.stdout
    # Qualquer print de biblioteca vai para o stderr; o stdout fica reservado ao protocolo.
    sys.stdout = sys.stderr
    try:
        raw = sys.stdin.buffer.read().decode("utf-8")
    except Exception:
        raw = ""
    response = process_request(raw)
    line = json.dumps(response, ensure_ascii=False, separators=(",", ":")) + "\n"
    real_stdout.buffer.write(line.encode("utf-8"))
    real_stdout.buffer.flush()
    return 0


if __name__ == "__main__":
    sys.exit(main())
