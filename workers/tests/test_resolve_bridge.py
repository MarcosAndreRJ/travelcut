# -*- coding: utf-8 -*-
"""
Testes do bridge DaVinci (funções puras + protocolo sem Resolve).

Execução no host (a partir de workers/):
    python -m unittest discover -s tests -v

ESTADO: escritos, NÃO executados na VM de desenvolvimento (sem Python).
Não dependem de DaVinciResolve; o caso "Resolve ausente" força RESOLVE_SCRIPT_API para um diretório vazio.
"""

import json
import os
import shutil
import sys
import tempfile
import time
import types
import unittest

WORKERS_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if WORKERS_DIR not in sys.path:
    sys.path.insert(0, WORKERS_DIR)

import resolve_bridge as rb  # noqa: E402


def make_manifest(**overrides):
    media_a = os.path.join(tempfile.gettempdir(), "tc_test_media_a.mp4")
    manifest = {
        "schemaVersion": "editor-assembly/1",
        "notice": "teste",
        "editorProjectName": "TravelCut_Teste_r0001_20261102T120000",
        "timelineName": "TravelCut Timeline",
        "timelineFrameRate": "29.97",
        "media": [{
            "mediaId": "11111111-1111-1111-1111-111111111111",
            "relativePath": "a.mp4",
            "absolutePath": media_a,
            "sourceFpsNum": 30000,
            "sourceFpsDen": 1001,
            "isVariableFrameRate": False,
            "durationUs": 10000000,
        }],
        "clips": [{
            "clipId": "22222222-2222-2222-2222-222222222222",
            "mediaId": "11111111-1111-1111-1111-111111111111",
            "trackIndex": 1,
            "mediaType": "video_audio",
            "sourceStartFrame": 30,
            "sourceEndFrameExclusive": 150,
            "recordFrame": 0,
            "durationFrames": 120,
            "sourceInUs": 1001000,
            "sourceOutUs": 5005000,
        }],
        "rejected": [],
        "warnings": [],
        "assemblyFingerprint": "harness",
    }
    manifest.update(overrides)
    return manifest


class FrameConversionTests(unittest.TestCase):
    def test_inclusive_end_subtracts_one(self):
        self.assertEqual(rb.end_frame_for_api(30, 150, True), 149)

    def test_exclusive_end_is_kept(self):
        self.assertEqual(rb.end_frame_for_api(30, 150, False), 150)

    def test_single_frame_inclusive(self):
        self.assertEqual(rb.end_frame_for_api(30, 31, True), 30)

    def test_empty_interval_is_rejected(self):
        with self.assertRaises(ValueError):
            rb.end_frame_for_api(30, 30, True)

    def test_record_frame_is_relative_to_timeline_start(self):
        self.assertEqual(rb.absolute_record_frame(86400, 120), 86520)

    def test_relative_record_frame_round_trip(self):
        self.assertEqual(rb.relative_record_frame(86520, 86400), 120)

    def test_build_clip_info_uses_timeline_start_and_inclusive_end(self):
        clip = make_manifest()["clips"][0]
        info = rb.build_clip_info(clip, 86400, True)
        self.assertEqual(info, {"startFrame": 30, "endFrame": 149, "recordFrame": 86400,
                                "trackIndex": 1})

    def test_build_clip_info_exclusive_convention(self):
        clip = make_manifest()["clips"][0]
        info = rb.build_clip_info(clip, 0, False)
        self.assertEqual(info["endFrame"], 150)


class ProjectNameTests(unittest.TestCase):
    def test_accepts_travelcut_prefix(self):
        self.assertTrue(rb.is_travelcut_project_name("TravelCut_Teste_r0001"))

    def test_rejects_personal_name(self):
        self.assertFalse(rb.is_travelcut_project_name("Meu Projeto Pessoal"))

    def test_rejects_prefix_only(self):
        self.assertFalse(rb.is_travelcut_project_name("TravelCut_"))

    def test_rejects_path_separator(self):
        self.assertFalse(rb.is_travelcut_project_name("TravelCut_a/b"))
        self.assertFalse(rb.is_travelcut_project_name("TravelCut_a\\b"))

    def test_rejects_control_characters(self):
        self.assertFalse(rb.is_travelcut_project_name("TravelCut_a\nb"))

    def test_rejects_non_string(self):
        self.assertFalse(rb.is_travelcut_project_name(None))
        self.assertFalse(rb.is_travelcut_project_name(42))

    def test_rejects_too_long(self):
        self.assertFalse(rb.is_travelcut_project_name("TravelCut_" + "x" * 200))


class ManifestValidationTests(unittest.TestCase):
    def test_valid_manifest_passes(self):
        manifest = make_manifest()
        self.assertIs(rb.validate_manifest(manifest), manifest)

    def test_wrong_schema(self):
        with self.assertRaises(rb.BridgeError) as ctx:
            rb.validate_manifest(make_manifest(schemaVersion="editor-assembly/9"))
        self.assertEqual(ctx.exception.code, "CONTRACT_VERSION_MISMATCH")

    def test_personal_project_name_is_denied(self):
        with self.assertRaises(rb.BridgeError) as ctx:
            rb.validate_manifest(make_manifest(editorProjectName="Pessoal_2026"))
        self.assertEqual(ctx.exception.code, "PERMISSION_DENIED")

    def test_no_clips(self):
        with self.assertRaises(rb.BridgeError) as ctx:
            rb.validate_manifest(make_manifest(clips=[]))
        self.assertEqual(ctx.exception.code, "NO_CLIPS")

    def test_clip_referencing_unknown_media(self):
        manifest = make_manifest()
        manifest["clips"][0]["mediaId"] = "99999999-9999-9999-9999-999999999999"
        with self.assertRaises(rb.BridgeError) as ctx:
            rb.validate_manifest(manifest)
        self.assertEqual(ctx.exception.code, "INVALID_MANIFEST")

    def test_empty_source_interval(self):
        manifest = make_manifest()
        manifest["clips"][0]["sourceEndFrameExclusive"] = 30
        with self.assertRaises(rb.BridgeError) as ctx:
            rb.validate_manifest(manifest)
        self.assertEqual(ctx.exception.code, "INVALID_MANIFEST")

    def test_boolean_is_not_a_frame(self):
        manifest = make_manifest()
        manifest["clips"][0]["recordFrame"] = True
        with self.assertRaises(rb.BridgeError) as ctx:
            rb.validate_manifest(manifest)
        self.assertEqual(ctx.exception.code, "INVALID_MANIFEST")

    def test_float_frame_is_rejected(self):
        manifest = make_manifest()
        manifest["clips"][0]["durationFrames"] = 120.5
        with self.assertRaises(rb.BridgeError):
            rb.validate_manifest(manifest)


class MatchingTests(unittest.TestCase):
    def expected(self, **kw):
        base = {"clipId": "c1", "trackIndex": 1, "recordFrame": 0, "durationFrames": 120}
        base.update(kw)
        return [base]

    def test_exact_match(self):
        observed = [{"trackIndex": 1, "recordFrame": 0, "durationFrames": 120, "leftOffsetFrames": 30}]
        result = rb.match_observed_items(self.expected(sourceStartFrame=30), observed)
        self.assertTrue(result[0]["matches"])
        self.assertIsNotNone(result[0]["item"])

    def test_position_and_duration_without_in_point_is_none_not_true(self):
        # A3: posição e duração batem, mas o leftOffset não foi observado: NÃO é conferido.
        observed = [{"trackIndex": 1, "recordFrame": 0, "durationFrames": 120, "leftOffsetFrames": None}]
        result = rb.match_observed_items(self.expected(sourceStartFrame=30), observed)
        self.assertIsNone(result[0]["matches"])
        self.assertIsNotNone(result[0]["item"])

    def test_duration_divergence_without_in_point_is_false(self):
        observed = [{"trackIndex": 1, "recordFrame": 0, "durationFrames": 121, "leftOffsetFrames": None}]
        result = rb.match_observed_items(self.expected(sourceStartFrame=30), observed)
        self.assertFalse(result[0]["matches"])

    def test_duration_divergence_does_not_match(self):
        observed = [{"trackIndex": 1, "recordFrame": 0, "durationFrames": 121}]
        result = rb.match_observed_items(self.expected(), observed)
        self.assertFalse(result[0]["matches"])

    def test_missing_item(self):
        result = rb.match_observed_items(self.expected(), [])
        self.assertFalse(result[0]["matches"])
        self.assertIsNone(result[0]["item"])

    def test_other_track_does_not_match(self):
        observed = [{"trackIndex": 2, "recordFrame": 0, "durationFrames": 120}]
        result = rb.match_observed_items(self.expected(), observed)
        self.assertFalse(result[0]["matches"])

    def test_tolerance_zero_rejects_off_by_one(self):
        observed = [{"trackIndex": 1, "recordFrame": 1, "durationFrames": 120}]
        result = rb.match_observed_items(self.expected(), observed)
        self.assertFalse(result[0]["matches"])

    def test_each_observed_item_used_once(self):
        expected = [
            {"clipId": "a", "trackIndex": 1, "recordFrame": 0, "durationFrames": 120, "sourceStartFrame": 0},
            {"clipId": "b", "trackIndex": 1, "recordFrame": 0, "durationFrames": 120, "sourceStartFrame": 0},
        ]
        observed = [{"trackIndex": 1, "recordFrame": 0, "durationFrames": 120, "leftOffsetFrames": 0}]
        result = rb.match_observed_items(expected, observed)
        self.assertTrue(result[0]["matches"])
        self.assertFalse(result[1]["matches"])


class FinalStatusTests(unittest.TestCase):
    def test_ready_real(self):
        self.assertEqual(rb.compute_final_status(False, 0, 0), "READY_REAL")

    def test_partial_on_rejection(self):
        self.assertEqual(rb.compute_final_status(False, 1, 0), "PARTIAL_REAL")

    def test_partial_on_divergence(self):
        self.assertEqual(rb.compute_final_status(False, 0, 1), "PARTIAL_REAL")

    def test_error_on_failed_step_even_with_rejections(self):
        self.assertEqual(rb.compute_final_status(True, 2, 2), "ERROR")

    def test_unverified_in_point_is_partial_not_ready(self):
        # Um match None (in-point não observado) entra como divergente no chamador: nunca READY_REAL.
        state = rb.ApplyState({"editorProjectName": "TravelCut_x", "timelineName": "t", "timelineFrameRate": "29.97",
                               "media": [], "clips": []}, None, True, None)
        state.mappings.append({"clipId": "c1", "matches": None})
        self.assertEqual(state.divergent_count(), 1)
        state.steps.append({"operation": "create_project", "status": "ok", "message": "", "durationMs": 1})
        self.assertEqual(state.to_response()["status"], "PARTIAL_REAL")

    def test_all_true_matches_is_ready(self):
        state = rb.ApplyState({"editorProjectName": "TravelCut_x", "timelineName": "t", "timelineFrameRate": "29.97",
                               "media": [], "clips": []}, None, True, None)
        state.mappings.append({"clipId": "c1", "matches": True})
        state.steps.append({"operation": "create_project", "status": "ok", "message": "", "durationMs": 1})
        self.assertEqual(state.to_response()["status"], "READY_REAL")

    def test_probe_status(self):
        self.assertEqual(rb.summarize_probe_status({"A": True, "B": None}), "READY")
        self.assertEqual(rb.summarize_probe_status({"A": True, "B": False}), "READY_PARTIAL")


class OutputAndSettingsTests(unittest.TestCase):
    def test_render_settings_keys(self):
        settings = rb.build_render_settings("C:\\out", "TravelCut_preview")
        self.assertIs(settings["SelectAllFrames"], True)
        self.assertEqual(settings["TargetDir"], "C:\\out")
        self.assertEqual(settings["CustomName"], "TravelCut_preview")

    def test_preview_custom_name_is_unique_per_run_in_utc_format(self):
        name = rb.preview_custom_name(time.struct_time((2026, 11, 2, 10, 15, 7, 0, 306, 0)))
        self.assertEqual(name, "TravelCut_preview_20261102T101507")
        self.assertRegex(rb.preview_custom_name(), r"^TravelCut_preview_\d{8}T\d{6}$")

    def test_find_new_output_with_prefix_ignores_other_renders(self):
        folder = tempfile.mkdtemp(prefix="tc_bridge_test_")
        try:
            with open(os.path.join(folder, "TravelCut_preview_20261102T100000.mp4"), "wb") as handle:
                handle.write(b"antigo")
            before = set(os.listdir(folder))
            with open(os.path.join(folder, "outro_projeto.mp4"), "wb") as handle:
                handle.write(b"outro")
            prefix = "TravelCut_preview_20261102T101507"
            self.assertIsNone(rb.find_new_output(folder, before, name_prefix=prefix))
            mine = os.path.join(folder, prefix + "_001.mp4")
            with open(mine, "wb") as handle:
                handle.write(b"novo")
            self.assertEqual(rb.find_new_output(folder, before, name_prefix=prefix), mine)
        finally:
            shutil.rmtree(folder, ignore_errors=True)

    def test_find_new_output_ignores_previous_files(self):
        folder = tempfile.mkdtemp(prefix="tc_bridge_test_")
        try:
            old = os.path.join(folder, "old.mp4")
            with open(old, "wb") as handle:
                handle.write(b"x")
            before = set(os.listdir(folder))
            self.assertIsNone(rb.find_new_output(folder, before))
            new = os.path.join(folder, "new.mp4")
            with open(new, "wb") as handle:
                handle.write(b"y")
            self.assertEqual(rb.find_new_output(folder, before), new)
        finally:
            shutil.rmtree(folder, ignore_errors=True)

    def test_read_track_items_is_relative_and_skips_missing_start(self):
        raw = [
            {"start": 86400, "duration": 120, "leftOffset": 30, "uniqueId": "u1", "name": "C:\\x\\clip.mp4"},
            {"start": None, "duration": 10},
        ]
        items = rb.read_track_items(raw, 1, 86400)
        self.assertEqual(len(items), 1)
        self.assertEqual(items[0]["recordFrame"], 0)
        self.assertEqual(items[0]["mediaName"], "clip.mp4")
        self.assertNotIn("C:\\x", json.dumps(items[0]))


class ProtocolTests(unittest.TestCase):
    """Protocolo sem Resolve: erros que ocorrem antes de qualquer chamada ao módulo."""

    def request(self, **fields):
        body = {"schemaVersion": "resolve-bridge/1"}
        body.update(fields)
        return json.dumps(body)

    def test_invalid_json(self):
        response = rb.process_request("{nao-json")
        self.assertFalse(response["ok"])
        self.assertEqual(response["errorCode"], "INVALID_REQUEST")

    def test_wrong_schema(self):
        response = rb.process_request(json.dumps({"schemaVersion": "x", "command": "probe"}))
        self.assertEqual(response["errorCode"], "UNSUPPORTED_SCHEMA")

    def test_unknown_command(self):
        response = rb.process_request(self.request(command="delete_everything"))
        self.assertEqual(response["errorCode"], "UNKNOWN_COMMAND")

    def test_non_string_command_does_not_crash(self):
        response = rb.process_request(self.request(command=["probe"]))
        self.assertEqual(response["errorCode"], "UNKNOWN_COMMAND")

    def test_query_refuses_personal_project_without_touching_resolve(self):
        response = rb.process_request(self.request(
            command="query_timeline", projectName="Meu Projeto", timelineName="x"))
        self.assertEqual(response["errorCode"], "PERMISSION_DENIED")

    def test_export_refuses_personal_project(self):
        response = rb.process_request(self.request(
            command="export_project", projectName="Pessoal", outputPath="C:\\out\\x.drp"))
        self.assertEqual(response["errorCode"], "PERMISSION_DENIED")

    def test_render_refuses_personal_project(self):
        response = rb.process_request(self.request(
            command="render_preview", projectName="Pessoal", timelineName="x", outputDir="C:\\out"))
        self.assertEqual(response["errorCode"], "PERMISSION_DENIED")

    def test_export_requires_drp_extension(self):
        response = rb.process_request(self.request(
            command="export_project", projectName="TravelCut_x", outputPath="C:\\out\\x.zip"))
        self.assertEqual(response["errorCode"], "INVALID_REQUEST")

    def editor_folder(self):
        """Pasta temporária <tmp>\\3-Trabalho\\editor (allowedOutputRoot de teste) e a raiz temporária."""
        root = tempfile.mkdtemp(prefix="tc_bridge_test_")
        allowed = os.path.join(root, "3-Trabalho", "editor")
        os.makedirs(allowed)
        return root, allowed

    def test_apply_missing_manifest(self):
        root, allowed = self.editor_folder()
        try:
            missing = os.path.join(allowed, "tc_no_such_manifest_123.json")
            response = rb.process_request(self.request(
                command="apply_assembly", manifestPath=missing, allowedOutputRoot=allowed))
            self.assertEqual(response["errorCode"], "MANIFEST_NOT_FOUND")
            self.assertNotIn(missing, response["errorMessage"])
        finally:
            shutil.rmtree(root, ignore_errors=True)

    def test_apply_rejects_personal_manifest_name(self):
        root, allowed = self.editor_folder()
        try:
            path = os.path.join(allowed, "m.json")
            with open(path, "w", encoding="utf-8") as handle:
                json.dump(make_manifest(editorProjectName="Pessoal_2026"), handle)
            response = rb.process_request(self.request(
                command="apply_assembly", manifestPath=path, allowedOutputRoot=allowed))
            self.assertEqual(response["errorCode"], "PERMISSION_DENIED")
            self.assertNotIn(root, response["errorMessage"])
        finally:
            shutil.rmtree(root, ignore_errors=True)

    def test_apply_refuses_manifest_outside_editor_folder_before_reading(self):
        # N2: manifesto válido e existente, mas fora de 3-Trabalho\editor: PERMISSION_DENIED sem ler o arquivo.
        root, allowed = self.editor_folder()
        try:
            outside = os.path.join(root, "fora.json")
            with open(outside, "w", encoding="utf-8") as handle:
                json.dump(make_manifest(), handle)
            response = rb.process_request(self.request(
                command="apply_assembly", manifestPath=outside, allowedOutputRoot=allowed))
            self.assertEqual(response["errorCode"], "PERMISSION_DENIED")
            self.assertNotIn(root, response["errorMessage"])
        finally:
            shutil.rmtree(root, ignore_errors=True)

    def test_apply_refuses_missing_manifest_outside_editor_folder(self):
        # Mesmo inexistente, o confinamento vem antes da verificação de existência.
        root, allowed = self.editor_folder()
        try:
            sibling = os.path.join(root, "3-Trabalho", "editor2", "m.json")
            response = rb.process_request(self.request(
                command="apply_assembly", manifestPath=sibling, allowedOutputRoot=allowed))
            self.assertEqual(response["errorCode"], "PERMISSION_DENIED")
        finally:
            shutil.rmtree(root, ignore_errors=True)

    def test_probe_without_module_is_not_installed(self):
        empty = tempfile.mkdtemp(prefix="tc_bridge_empty_")
        saved = {key: os.environ.get(key) for key in ("RESOLVE_SCRIPT_API", "RESOLVE_SCRIPT_LIB")}
        saved_module = sys.modules.pop("DaVinciResolveScript", None)
        try:
            os.environ["RESOLVE_SCRIPT_API"] = empty
            os.environ["RESOLVE_SCRIPT_LIB"] = os.path.join(empty, "missing.dll")
            response = rb.process_request(self.request(command="probe"))
            self.assertFalse(response["ok"])
            self.assertEqual(response["errorCode"], "NOT_INSTALLED")
            self.assertNotIn(empty, response["errorMessage"])
        finally:
            if saved_module is not None:
                sys.modules["DaVinciResolveScript"] = saved_module
            for key, value in saved.items():
                if value is None:
                    os.environ.pop(key, None)
                else:
                    os.environ[key] = value
            shutil.rmtree(empty, ignore_errors=True)


class InPointMatchingTests(unittest.TestCase):
    """In-point (leftOffset) entra no casamento quando a API o expõe."""

    def expected(self, source_start=30):
        return [{"clipId": "c1", "trackIndex": 1, "recordFrame": 0, "durationFrames": 120,
                 "sourceStartFrame": source_start}]

    def test_left_offset_equal_matches(self):
        observed = [{"trackIndex": 1, "recordFrame": 0, "durationFrames": 120, "leftOffsetFrames": 30}]
        self.assertTrue(rb.match_observed_items(self.expected(), observed)[0]["matches"])

    def test_left_offset_divergence_does_not_match(self):
        observed = [{"trackIndex": 1, "recordFrame": 0, "durationFrames": 120, "leftOffsetFrames": 31}]
        self.assertFalse(rb.match_observed_items(self.expected(), observed)[0]["matches"])

    def test_left_offset_absent_is_not_verified_so_matches_is_none(self):
        observed = [{"trackIndex": 1, "recordFrame": 0, "durationFrames": 120, "leftOffsetFrames": None}]
        self.assertIsNone(rb.match_observed_items(self.expected(), observed)[0]["matches"])


class ConfinementTests(unittest.TestCase):
    def setUp(self):
        self.root = tempfile.mkdtemp(prefix="tc_bridge_conf_")
        self.allowed = os.path.join(self.root, "3-Trabalho", "editor")
        os.makedirs(self.allowed)

    def tearDown(self):
        shutil.rmtree(self.root, ignore_errors=True)

    def test_allowed_root_accepts_editor_folder(self):
        self.assertTrue(rb.validate_allowed_root(self.allowed).lower().endswith(
            os.path.join("3-trabalho", "editor")))

    def test_allowed_root_is_case_insensitive(self):
        upper = os.path.join(self.root, "3-TRABALHO", "EDITOR")
        os.makedirs(upper, exist_ok=True)
        self.assertTrue(rb.validate_allowed_root(upper))

    def test_allowed_root_rejects_project_root(self):
        with self.assertRaises(rb.BridgeError) as ctx:
            rb.validate_allowed_root(self.root)
        self.assertEqual(ctx.exception.code, "PERMISSION_DENIED")

    def test_allowed_root_rejects_relative_and_missing(self):
        with self.assertRaises(rb.BridgeError) as ctx:
            rb.validate_allowed_root(os.path.join("3-Trabalho", "editor"))
        self.assertEqual(ctx.exception.code, "INVALID_REQUEST")
        with self.assertRaises(rb.BridgeError) as ctx:
            rb.validate_allowed_root(None)
        self.assertEqual(ctx.exception.code, "INVALID_REQUEST")

    def test_path_inside_needs_separator_after_folder(self):
        self.assertTrue(rb.path_is_inside(os.path.join(self.allowed, "plano", "x.drp"), self.allowed))
        self.assertFalse(rb.path_is_inside(self.allowed + "2" + os.sep + "x.drp", self.allowed))
        self.assertFalse(rb.path_is_inside(os.path.join(self.root, "fora.drp"), self.allowed))

    def test_media_folders_are_bruto_and_assets_of_same_project(self):
        folders = rb.media_folders_for(self.allowed)
        self.assertTrue(rb.media_is_allowed(os.path.join(self.root, "1-Bruto", "a.mp4"), folders))
        self.assertTrue(rb.media_is_allowed(os.path.join(self.root, "2-Assets", "b.mp4"), folders))
        self.assertFalse(rb.media_is_allowed(os.path.join(self.root, "fora", "c.mp4"), folders))
        self.assertFalse(rb.media_is_allowed(os.path.join(self.root, "1-Bruto", "a.mp4"), None))


# ---------------------------------------------------------------------------
# M8 — apply/query/export/render com módulo DaVinciResolveScript FALSO injetado em sys.modules.
# Não há Resolve nem Python na VM; estes testes exercitam o fluxo de decisão do bridge.
# ---------------------------------------------------------------------------

MEDIA_ID = "11111111-1111-1111-1111-111111111111"
NEW_PROJECT = "TravelCut_Teste_r0001_20261102T120000"


class FakeItem(object):
    """Clipe importado (MediaPool) ou item posicionado na timeline."""

    def __init__(self, path=None, start=None, duration=None, left=None, uid=None):
        self.path = path
        self.start = start
        self.duration = duration
        self.left = left
        self.uid = uid

    def GetClipProperty(self, key=None):
        return self.path if key == "File Path" else None

    def GetStart(self):
        return self.start

    def GetDuration(self):
        return self.duration

    def GetLeftOffset(self):
        return self.left

    def GetUniqueId(self):
        return self.uid

    def GetName(self):
        return self.path


class FakeTimeline(object):
    def __init__(self, name, start_frame=86400):
        self.name = name
        self.start_frame = start_frame
        self.tracks = {}

    def GetName(self):
        return self.name

    def GetStartFrame(self):
        return self.start_frame

    def GetItemListInTrack(self, kind, index):
        return list(self.tracks.get(index, []))


class FakeMediaPool(object):
    def __init__(self, project):
        self.project = project

    def ImportMedia(self, paths):
        items = []
        for path in paths:
            items.append(FakeItem(path=path))
        return items

    def CreateEmptyTimeline(self, name):
        timeline = FakeTimeline(name)
        self.project.timelines.append(timeline)
        return timeline

    def AppendToTimeline(self, infos):
        project = self.project
        timeline = project.current_timeline
        for info in infos:
            start = info["startFrame"]
            end = info["endFrame"]
            # Convenção da API simulada: endFrame inclusivo (padrão) ou exclusivo (api_end_inclusive=False).
            duration = (end - start + 1) if project.api_end_inclusive else (end - start)
            left = (start + project.api_left_delta) if project.api_expose_left else None
            item = FakeItem(path=info["mediaPoolItem"].path, start=info["recordFrame"], duration=duration,
                            left=left, uid="item-%d" % (len(project.appended_infos) + 1))
            project.appended_infos.append({key: value for key, value in info.items() if key != "mediaPoolItem"})
            timeline.tracks.setdefault(info["trackIndex"], []).append(item)
        return True


class FakeProject(object):
    def __init__(self, name, api_end_inclusive=True, api_left_delta=0, api_expose_left=True):
        self.name = name
        self.api_end_inclusive = api_end_inclusive
        self.api_left_delta = api_left_delta
        self.api_expose_left = api_expose_left  # False: GetLeftOffset devolve None (in-point não observado)
        self.timelines = []
        self.current_timeline = None
        self.appended_infos = []
        self.render_settings = None
        self.settings = {}
        self.media_pool = FakeMediaPool(self)

    def GetName(self):
        return self.name

    def GetMediaPool(self):
        return self.media_pool

    def SetSetting(self, key, value):
        self.settings[key] = value
        return True

    def GetTimelineCount(self):
        return len(self.timelines)

    def GetTimelineByIndex(self, index):
        if 1 <= index <= len(self.timelines):
            return self.timelines[index - 1]
        return None

    def SetCurrentTimeline(self, timeline):
        self.current_timeline = timeline
        return True

    def GetCurrentTimeline(self):
        return self.current_timeline

    def SetCurrentRenderFormatAndCodec(self, fmt, codec):
        return True

    def SetRenderSettings(self, settings):
        self.render_settings = dict(settings)
        return True

    def AddRenderJob(self):
        return "job-1"

    def StartRendering(self, job_ids):
        target = self.render_settings["TargetDir"]
        with open(os.path.join(target, self.render_settings["CustomName"] + ".mp4"), "wb") as handle:
            handle.write(b"mp4 falso")
        return True

    def IsRenderingInProgress(self):
        return False

    def GetRenderJobStatus(self, job_id):
        return {"JobStatus": "Complete"}

    def StopRendering(self):
        return True


class FakeProjectManager(object):
    project_options = {}

    def __init__(self, auto_switch=True):
        self.projects = {}
        self.current = None
        self.auto_switch = auto_switch
        self.loads = []
        self.exports = []
        self.saves = 0

    def add_project(self, project, make_current=True):
        self.projects[project.name] = project
        if make_current:
            self.current = project

    def CreateProject(self, name):
        if name in self.projects:
            return None
        project = FakeProject(name, **self.project_options)
        self.projects[name] = project
        if self.auto_switch:
            self.current = project
        return project

    def LoadProject(self, name):
        self.loads.append(name)
        project = self.projects.get(name)
        if project is not None:
            self.current = project
        return project

    def GetCurrentProject(self):
        return self.current

    def SaveProject(self):
        self.saves += 1
        return True

    def ExportProject(self, name, path):
        self.exports.append((name, path))
        if name not in self.projects:
            return False
        with open(path, "wb") as handle:
            handle.write(b"drp falso")
        return True


class FakeResolve(object):
    def __init__(self, manager):
        self.manager = manager

    def GetProjectManager(self):
        return self.manager

    def GetProductName(self):
        return "DaVinci Resolve Studio"

    def GetVersionString(self):
        return "20.2.0.0"


class ResolveFakeTestCase(unittest.TestCase):
    def setUp(self):
        self.root = tempfile.mkdtemp(prefix="tc_bridge_root_")
        self.allowed = os.path.join(self.root, "3-Trabalho", "editor")
        self.plan_dir = os.path.join(self.allowed, "plano")
        os.makedirs(self.plan_dir)
        os.makedirs(os.path.join(self.root, "1-Bruto"))
        self.media_path = os.path.join(self.root, "1-Bruto", "a.mp4")
        with open(self.media_path, "wb") as handle:
            handle.write(b"midia falsa")

        FakeProjectManager.project_options = {}
        self.manager = FakeProjectManager()
        module = types.ModuleType("DaVinciResolveScript")
        module.scriptapp = lambda name: FakeResolve(self.manager) if name == "Resolve" else None
        self._saved_module = sys.modules.get("DaVinciResolveScript")
        sys.modules["DaVinciResolveScript"] = module
        self._saved_env = {key: os.environ.get(key) for key in ("RESOLVE_SCRIPT_API", "RESOLVE_SCRIPT_LIB")}
        os.environ["RESOLVE_SCRIPT_API"] = self.root          # sem pasta Modules: nada é adicionado ao sys.path
        os.environ["RESOLVE_SCRIPT_LIB"] = os.path.join(self.root, "missing.dll")

    def tearDown(self):
        if self._saved_module is None:
            sys.modules.pop("DaVinciResolveScript", None)
        else:
            sys.modules["DaVinciResolveScript"] = self._saved_module
        for key, value in self._saved_env.items():
            if value is None:
                os.environ.pop(key, None)
            else:
                os.environ[key] = value
        FakeProjectManager.project_options = {}
        shutil.rmtree(self.root, ignore_errors=True)

    def manifest(self):
        data = make_manifest(editorProjectName=NEW_PROJECT)
        data["media"][0]["absolutePath"] = self.media_path
        return data

    def write_manifest(self, manifest, name="r0001.assembly.json"):
        path = os.path.join(self.plan_dir, name)
        with open(path, "w", encoding="utf-8") as handle:
            json.dump(manifest, handle)
        return path

    def call(self, command, **fields):
        body = {"schemaVersion": "resolve-bridge/1", "command": command}
        body.update(fields)
        return rb.process_request(json.dumps(body))

    def apply_request(self, manifest=None, **fields):
        path = self.write_manifest(manifest if manifest is not None else self.manifest())
        body = {"manifestPath": path, "allowedOutputRoot": self.allowed, "endFrameInclusive": True}
        body.update(fields)
        return self.call("apply_assembly", **body)

    def make_current_project(self, name=NEW_PROJECT, items=None, make_current=True):
        project = FakeProject(name)
        timeline = FakeTimeline("TravelCut r0001")
        project.timelines.append(timeline)
        project.current_timeline = timeline
        for track, start, duration, left in items or []:
            timeline.tracks.setdefault(track, []).append(
                FakeItem(path=self.media_path, start=start, duration=duration, left=left, uid="u"))
        self.manager.add_project(project, make_current=make_current)
        return project


class ProbeMethodsTests(ResolveFakeTestCase):
    def test_probe_reports_every_probed_method_including_render_and_save(self):
        self.make_current_project()

        resp = self.call("probe")

        self.assertTrue(resp["ok"], resp.get("errorMessage"))
        self.assertEqual(set(resp["methods"].keys()), set(rb.PROBED_METHODS))
        for name in ("SaveProject", "SetCurrentTimeline", "GetMediaPool", "SetRenderSettings", "AddRenderJob",
                     "StartRendering", "IsRenderingInProgress", "SetCurrentRenderFormatAndCodec", "ExportProject"):
            self.assertIn(name, rb.PROBED_METHODS)
            self.assertIs(resp["methods"][name], True, name)


class ApplyFlowTests(ResolveFakeTestCase):
    def test_inclusive_end_and_in_point_match(self):
        resp = self.apply_request()

        self.assertTrue(resp["ok"], resp.get("errorMessage"))
        self.assertEqual(resp["status"], "READY_REAL")
        self.assertEqual(self.manager.current.appended_infos[0],
                         {"startFrame": 30, "endFrame": 149, "recordFrame": 86400, "trackIndex": 1})
        mapping = resp["clipMappings"][0]
        self.assertTrue(mapping["matches"])
        self.assertEqual(mapping["leftOffsetFrames"], 30)
        self.assertEqual(mapping["observedRecordFrame"], 0)
        self.assertEqual(mapping["observedDurationFrames"], 120)

    def test_exclusive_end_convention_sends_150_and_matches(self):
        FakeProjectManager.project_options = {"api_end_inclusive": False}
        resp = self.apply_request(endFrameInclusive=False)

        self.assertEqual(resp["status"], "READY_REAL", resp.get("errorMessage"))
        self.assertEqual(self.manager.current.appended_infos[0]["endFrame"], 150)
        self.assertTrue(resp["clipMappings"][0]["matches"])

    def test_in_point_divergence_is_partial_and_not_matched(self):
        FakeProjectManager.project_options = {"api_left_delta": 1}
        resp = self.apply_request()

        self.assertTrue(resp["ok"])
        self.assertEqual(resp["status"], "PARTIAL_REAL")
        self.assertFalse(resp["clipMappings"][0]["matches"])
        self.assertEqual(resp["clipMappings"][0]["leftOffsetFrames"], 31)

    def test_in_point_not_observed_is_partial_with_warning_and_no_conferidos(self):
        # A3: posição e duração batem, leftOffset não exposto -> matches None, PARTIAL_REAL, IN_POINT_NOT_VERIFIED.
        FakeProjectManager.project_options = {"api_expose_left": False}
        resp = self.apply_request()

        self.assertTrue(resp["ok"], resp.get("errorMessage"))
        self.assertEqual(resp["status"], "PARTIAL_REAL")
        self.assertIsNone(resp["clipMappings"][0]["matches"])
        self.assertIsNone(resp["clipMappings"][0]["leftOffsetFrames"])
        self.assertIn("IN_POINT_NOT_VERIFIED", [w["code"] for w in resp["warnings"]])
        self.assertNotIn("TIMELINE_DIVERGENCE", [w["code"] for w in resp["warnings"]])
        query_step = [s for s in resp["steps"] if s["operation"] == "query_timeline"][0]
        self.assertEqual(query_step["status"], "ok")
        self.assertNotIn("conferidos", query_step["message"])
        self.assertIn("não observado", query_step["message"])

    def test_existing_project_name_is_refused_and_nothing_is_loaded(self):
        self.manager.CreateProject(NEW_PROJECT)
        loads_before = list(self.manager.loads)

        resp = self.apply_request()

        self.assertEqual(resp["status"], "ERROR")
        self.assertEqual(resp["errorCode"], "PROJECT_EXISTS")
        self.assertEqual(self.manager.loads, loads_before)

    def test_new_project_is_loaded_when_resolve_did_not_switch(self):
        self.manager.auto_switch = False

        resp = self.apply_request()

        self.assertEqual(resp["status"], "READY_REAL", resp.get("errorMessage"))
        self.assertIn(NEW_PROJECT, self.manager.loads)
        self.assertEqual(resp["steps"][0]["operation"], "create_project")
        self.assertIn("carregado", resp["steps"][0]["message"])

    def test_snapshot_outside_editor_folder_is_permission_denied(self):
        resp = self.apply_request(snapshotPath=os.path.join(self.root, "fora.drp"))

        self.assertEqual(resp["errorCode"], "PERMISSION_DENIED")
        self.assertEqual(self.manager.projects, {})

    def test_snapshot_inside_editor_folder_is_exported(self):
        snapshots = os.path.join(self.plan_dir, "snapshots")
        os.makedirs(snapshots)
        snapshot = os.path.join(snapshots, NEW_PROJECT + ".drp")

        resp = self.apply_request(snapshotPath=snapshot)

        self.assertEqual(resp["status"], "READY_REAL", resp.get("errorMessage"))
        self.assertTrue(os.path.isfile(snapshot))
        self.assertEqual(resp["snapshotAbsolutePath"], snapshot)

    def test_allowed_root_must_end_with_editor_folder(self):
        resp = self.apply_request(allowedOutputRoot=self.root)

        self.assertEqual(resp["errorCode"], "PERMISSION_DENIED")
        self.assertEqual(self.manager.projects, {})

    def test_allowed_root_is_required_for_apply(self):
        path = self.write_manifest(self.manifest())

        resp = self.call("apply_assembly", manifestPath=path)

        self.assertEqual(resp["errorCode"], "INVALID_REQUEST")
        self.assertEqual(self.manager.projects, {})

    def test_media_outside_source_folders_is_rejected_per_clip(self):
        outside_dir = os.path.join(self.root, "fora")
        os.makedirs(outside_dir)
        outside = os.path.join(outside_dir, "a.mp4")
        with open(outside, "wb") as handle:
            handle.write(b"x")
        manifest = self.manifest()
        manifest["media"][0]["absolutePath"] = outside

        resp = self.apply_request(manifest)

        self.assertIn("PERMISSION_DENIED", [item["code"] for item in resp["rejected"]])
        self.assertEqual(resp["status"], "ERROR")
        self.assertEqual(self.manager.current.appended_infos, [])

    def test_missing_media_inside_source_folder_is_media_not_found_per_clip(self):
        manifest = self.manifest()
        manifest["media"][0]["absolutePath"] = os.path.join(self.root, "1-Bruto", "nao-existe.mp4")

        resp = self.apply_request(manifest)

        self.assertIn("MEDIA_NOT_FOUND", [item["code"] for item in resp["rejected"]])
        self.assertEqual(resp["errorCode"], "APPLY_FAILED")


class QueryExportRenderTests(ResolveFakeTestCase):
    def test_query_reads_current_travelcut_project_with_in_point(self):
        self.make_current_project(items=[(1, 86400, 120, 30)])

        resp = self.call("query_timeline", projectName=NEW_PROJECT, timelineName="TravelCut r0001")

        self.assertTrue(resp["ok"], resp.get("errorMessage"))
        self.assertEqual(resp["timelineStartFrame"], 86400)
        item = resp["items"][0]
        self.assertEqual((item["recordFrame"], item["durationFrames"], item["leftOffsetFrames"]), (0, 120, 30))
        self.assertEqual(self.manager.loads, [])

    def test_query_refuses_when_another_project_is_current_without_switching(self):
        self.make_current_project(make_current=False)
        self.manager.add_project(FakeProject("Pessoal_2026"), make_current=True)

        resp = self.call("query_timeline", projectName=NEW_PROJECT, timelineName="TravelCut r0001")

        self.assertEqual(resp["errorCode"], "PROJECT_NOT_CURRENT")
        self.assertEqual(self.manager.loads, [])
        self.assertEqual(self.manager.current.name, "Pessoal_2026")

    def test_query_refuses_when_no_project_is_open(self):
        self.make_current_project(make_current=False)

        resp = self.call("query_timeline", projectName=NEW_PROJECT, timelineName="TravelCut r0001")

        self.assertEqual(resp["errorCode"], "PROJECT_NOT_CURRENT")
        self.assertEqual(self.manager.loads, [])

    def test_export_refuses_path_outside_editor_folder(self):
        self.make_current_project()
        outside = os.path.join(self.root, "fora.drp")

        resp = self.call("export_project", projectName=NEW_PROJECT, outputPath=outside, allowedOutputRoot=self.allowed)

        self.assertEqual(resp["errorCode"], "PERMISSION_DENIED")
        self.assertEqual(self.manager.exports, [])
        self.assertFalse(os.path.exists(outside))

    def test_export_refuses_sibling_folder_with_same_prefix(self):
        self.make_current_project()
        sneaky = self.allowed + "2" + os.sep + "x.drp"

        resp = self.call("export_project", projectName=NEW_PROJECT, outputPath=sneaky, allowedOutputRoot=self.allowed)

        self.assertEqual(resp["errorCode"], "PERMISSION_DENIED")
        self.assertEqual(self.manager.exports, [])

    def test_export_inside_editor_folder_writes_drp(self):
        self.make_current_project()
        target = os.path.join(self.plan_dir, "export.drp")

        resp = self.call("export_project", projectName=NEW_PROJECT, outputPath=target, allowedOutputRoot=self.allowed)

        self.assertTrue(resp["ok"], resp.get("errorMessage"))
        self.assertTrue(os.path.isfile(target))

    def test_export_refuses_when_project_is_not_current(self):
        self.make_current_project(make_current=False)
        self.manager.add_project(FakeProject("Pessoal_2026"), make_current=True)
        target = os.path.join(self.plan_dir, "export.drp")

        resp = self.call("export_project", projectName=NEW_PROJECT, outputPath=target, allowedOutputRoot=self.allowed)

        self.assertEqual(resp["errorCode"], "PROJECT_NOT_CURRENT")
        self.assertEqual(self.manager.exports, [])

    def test_render_refuses_output_outside_editor_folder(self):
        self.make_current_project()
        out_dir = os.path.join(self.root, "render")

        resp = self.call("render_preview", projectName=NEW_PROJECT, timelineName="TravelCut r0001",
                         outputDir=out_dir, allowedOutputRoot=self.allowed, timeoutSec=60)

        self.assertEqual(resp["errorCode"], "PERMISSION_DENIED")
        self.assertFalse(os.path.exists(out_dir))

    def test_render_inside_editor_folder_returns_new_output(self):
        self.make_current_project()
        out_dir = os.path.join(self.plan_dir, "previews", NEW_PROJECT)

        resp = self.call("render_preview", projectName=NEW_PROJECT, timelineName="TravelCut r0001",
                         outputDir=out_dir, allowedOutputRoot=self.allowed, timeoutSec=60)

        self.assertTrue(resp["ok"], resp.get("errorMessage"))
        self.assertTrue(resp["outputAbsolutePath"].lower().endswith(".mp4"))
        self.assertTrue(os.path.isfile(resp["outputAbsolutePath"]))
        self.assertEqual(self.manager.loads, [])
        # N5: CustomName único por job e a saída encontrada é a deste job.
        custom = self.manager.current.render_settings["CustomName"]
        self.assertRegex(custom, r"^TravelCut_preview_\d{8}T\d{6}$")
        self.assertTrue(os.path.basename(resp["outputAbsolutePath"]).startswith(custom))


if __name__ == "__main__":
    unittest.main()
