"""Exercise the Debug server over stdio. Pass a folder to run the read-only live test."""
import json
import os
from pathlib import Path
import subprocess
import sys
import unittest
import uuid
from xml.etree import ElementTree
from zipfile import ZipFile


SERVER = Path(__file__).resolve().parents[1] / "Server/bin/Debug/net48/PiaTiaMcpServer.exe"


class UsageProtocolTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.server = subprocess.Popen(
            [str(SERVER)], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
            encoding="utf-8", cwd=SERVER.parent,
        )
        cls.addClassCleanup(cls.stop_server)
        cls.sequence = 0
        cls.request("initialize", {
            "protocolVersion": "2024-11-05", "capabilities": {},
            "clientInfo": {"name": "UsageProtocolTests", "version": "1"},
        })
        cls.send({"method": "notifications/initialized"})

    @classmethod
    def stop_server(cls):
        cls.server.stdin.close()
        try:
            cls.server.wait(timeout=5)
        except subprocess.TimeoutExpired:
            cls.server.kill()
            cls.server.wait()
        cls.server.stdout.close()

    @classmethod
    def send(cls, message):
        cls.server.stdin.write(json.dumps({"jsonrpc": "2.0", **message}) + "\n")
        cls.server.stdin.flush()

    @classmethod
    def request(cls, method, params):
        cls.sequence += 1
        cls.send({"id": cls.sequence, "method": method, "params": params})
        for line in cls.server.stdout:
            response = json.loads(line)
            if response.get("id") == cls.sequence:
                if "error" in response:
                    raise AssertionError(response["error"])
                return response["result"]
        raise RuntimeError("Server exited before returning a response.")

    def call(self, name, arguments):
        response = self.request("tools/call", {"name": name, "arguments": arguments})
        self.assertFalse(response.get("isError"), response.get("content"))
        if "structuredContent" in response:
            return response["structuredContent"]
        return json.loads(response["content"][0]["text"])

    def test_registration_and_validation(self):
        tools = {tool["name"]: tool for tool in self.request("tools/list", {})["tools"]}
        self.assertIn("GetExport", tools)
        export = tools["ExportExcel"]
        self.assertEqual(set(export["inputSchema"]["required"]), {"title", "columns", "rows"})
        self.assertFalse(export["annotations"]["readOnlyHint"])
        self.assertFalse(export["annotations"]["idempotentHint"])
        for name in ("GetProjectStructure", "GetProjectKnowledge", "GetCrossReferences", "DescribeBlockLogic",
                     "GetPlcTags", "GetPlcBlockInterface", "GetPlcCallConnections", "GetPlcBlockSource", "PlanPlcFbdAppend",
                     "PlanPlcSclAppend"):
            self.assertTrue(tools[name]["annotations"]["readOnlyHint"], name)
        self.assertNotIn("CheckProjectLibraryUsage", tools)
        self.assertNotIn("CheckGlobalLibraryUsage", tools)
        self.assertTrue(tools["CheckLibraryUsage"]["annotations"]["readOnlyHint"])
        for args, expected in [
                ({"scope": "project", "folder": "///"}, "invalidFolder"),
                ({"scope": "other", "entry": "FB"}, "invalidScope"),
                ({"scope": "project", "folder": "A", "entry": "B"}, "specifyEntryOrFolder"),
                ({"scope": "global", "entry": "FB", "versionId": "3.15.0"}, "invalidVersionId")]:
            self.assertEqual(self.call("CheckLibraryUsage", args)["status"], expected)
        schema = tools["CheckLibraryUsage"]["inputSchema"]
        self.assertEqual(set(schema["required"]), {"scope"})
        self.assertEqual(self.call("CheckLibraryUsage", {"scope": "project", "library": "Library", "entry": "FB"})["status"], "invalidLibrary")
        self.assertEqual(self.call("CheckLibraryUsage", {"scope": "global", "entry": "/"})["status"], "invalidEntry")
        self.assertEqual(self.call("GetProjectStructure", {"maxDepth": 33})["status"], "invalidDepth")
        self.assertEqual(self.call("GetProjectStructure", {"folder": "Area"})["status"], "softwarePathRequired")
        self.assertEqual(self.call("GetProjectStructure", {"softwarePath": "PLC", "folder": "../Area"})["status"], "invalidFolder")
        block = {"softwarePath": "PLC", "blockPath": "Stations/FB_Test"}
        self.assertEqual(self.call("GetPlcBlockInterface", {**block, "memberPath": "Data..Step"})["status"], "invalidMemberPath")
        self.assertEqual(self.call("GetPlcCallConnections", {**block, "network": -1})["status"], "invalidNetwork")
        self.assertEqual(self.call("GetPlcCallConnections", {**block, "limit": 101})["status"], "invalidPage")
        self.assertEqual(self.call("GetPlcBlockSource", {**block, "offset": -1})["status"], "invalidPage")
        plan_schema = tools["PlanPlcFbdAppend"]["inputSchema"]["properties"]["changes"]["items"]
        self.assertEqual(set(plan_schema["required"]), {"blockPath", "networks"})
        self.assertFalse(plan_schema["additionalProperties"])
        self.assertIn("networkTitles", plan_schema["properties"])
        local_schema = plan_schema["properties"]["statics"]["items"]
        self.assertEqual(set(local_schema["required"]), {"name", "dataType"})
        self.assertIn("comment", local_schema["properties"])
        self.assertTrue(tools["ApplyPlcFbdAppend"]["annotations"]["destructiveHint"])
        self.assertFalse(tools["ApplyPlcFbdAppend"]["annotations"]["readOnlyHint"])
        self.assertEqual(set(tools["ApplyPlcFbdAppend"]["inputSchema"]["required"]), {"planId", "approved"})
        self.assertEqual(self.call("ApplyPlcFbdAppend", {"planId": "invalid", "approved": False})["status"], "approvalRequired")
        self.assertEqual(self.call("ApplyPlcFbdAppend", {"planId": "invalid", "approved": True})["status"], "planNotFound")
        self.assertTrue(tools["ApplyPlcSclAppend"]["annotations"]["destructiveHint"])
        self.assertEqual(self.call("ApplyPlcSclAppend", {"planId": "invalid", "approved": False})["status"], "approvalRequired")
        self.assertEqual(self.call("ApplyPlcSclAppend", {"planId": "invalid", "approved": True})["status"], "planNotFound")
        for changes in ([], [{"blockPath": block["blockPath"], "networks": ["END_FUNCTION_BLOCK"]}],
                        [{"blockPath": block["blockPath"], "networks": ["RUNG #In Coil(#Out) END_RUNG"],
                          "temps": [{"name": "Out; END_VAR", "dataType": "Bool"}]}]):
            result = self.call("PlanPlcFbdAppend", {"softwarePath": block["softwarePath"], "changes": changes})
            self.assertEqual(result["status"], "invalidChange")

    def test_fbd_documentation_is_optional_and_validated(self):
        basic = {"blockPath": "Station/FB_Station", "networks": ["RUNG #In Coil(#Out) END_RUNG"]}
        documented = {**basic, "networkTitles": ["Capture sensor data"],
                      "temps": [{"name": "Payload", "dataType": "WString", "comment": "Sensor payload"}]}
        for change in (basic, documented):
            self.assertEqual(self.call("PlanPlcFbdAppend", {"softwarePath": "", "changes": [change]})["status"],
                             "invalidSoftwarePath")
        for change in ({**basic, "networkTitles": []},
                       {**basic, "networkTitles": [""]},
                       {**documented, "temps": [{"name": "Payload", "dataType": "WString", "comment": "line 1\nline 2"}]}):
            self.assertEqual(self.call("PlanPlcFbdAppend", {"softwarePath": "", "changes": [change]})["status"],
                             "invalidChange")

    def test_scl_append_accepts_source_statements_and_rejects_wrapped_or_fbd_source(self):
        change = {"blockPath": "Station/FB_Station",
                  "network": "// Send only after a value change.\nIF #Current <> #Previous THEN #Previous := #Current; END_IF;",
                  "networkTitle": "Observe step changes",
                  "statics": [{"name": "Previous", "dataType": "Int", "comment": "Last observed step number"}]}
        self.assertEqual(self.call("PlanPlcSclAppend", {"softwarePath": "", "change": change})["status"],
                         "invalidSoftwarePath")
        for source in ("NETWORK #Value := 1; END_NETWORK", "RUNG #In Coil(#Out) END_RUNG", "#Value := Call(#Input;"):
            self.assertEqual(self.call("PlanPlcSclAppend", {"softwarePath": "", "change": {**change, "network": source}})["status"],
                             "invalidChange")

    def test_compile_plc_software_requires_a_bounded_software_name(self):
        tools = {tool["name"]: tool for tool in self.request("tools/list", {})["tools"]}
        compile_tool = tools["CompilePlcSoftware"]
        self.assertEqual(compile_tool["inputSchema"]["required"], ["softwarePath"])
        self.assertFalse(compile_tool["annotations"]["readOnlyHint"])
        for software_path in (" ", "x" * 513, "PLC\nName"):
            self.assertEqual(self.call("CompilePlcSoftware", {"softwarePath": software_path})["status"],
                             "invalidSoftwarePath")

    def test_graph_tools_require_bounded_pages_and_exact_approval(self):
        tools = {tool["name"]: tool for tool in self.request("tools/list", {})["tools"]}
        self.assertTrue(tools["GetPlcGraph"]["annotations"]["readOnlyHint"])
        self.assertTrue(tools["PlanPlcGraphStepFlag"]["annotations"]["readOnlyHint"])
        self.assertTrue(tools["ApplyPlcGraphStepFlag"]["annotations"]["destructiveHint"])
        self.assertTrue(tools["PlanPlcGraphStepFlagActions"]["annotations"]["readOnlyHint"])
        self.assertTrue(tools["ApplyPlcGraphStepFlagActions"]["annotations"]["destructiveHint"])
        self.assertNotIn("title", tools["PlanPlcGraphStepFlagActions"]["inputSchema"]["properties"])
        action_schema = tools["PlanPlcGraphStepFlagActions"]["inputSchema"]
        for parameter in ("dataBlockPath", "dataMemberParent"):
            self.assertIn(parameter, action_schema["properties"])
            self.assertNotIn(parameter, action_schema["required"])
        plan_schema = tools["PlanPlcGraphStepFlag"]["inputSchema"]
        self.assertIn("branchSteps", plan_schema["properties"])
        self.assertNotIn("branchSteps", plan_schema["required"])
        target = {"softwarePath": "PLC", "blockPath": "Station/Sequence"}
        self.assertEqual(self.call("GetPlcGraph", {**target, "limit": 51})["status"], "invalidPage")
        self.assertEqual(self.call("GetPlcGraph", {**target, "section": "flow", "number": 1})["status"], "invalidSection")
        change = {**target, "sequence": 1, "firstStep": 1, "lastStep": 2, "flag": "Bad.Flag",
                  "title": "Expose step range", "comment": "True while the selected steps are active."}
        self.assertEqual(self.call("PlanPlcGraphStepFlag", change)["status"], "invalidChange")
        self.assertEqual(self.call("PlanPlcGraphStepFlag", {**change, "flag": "TestActive", "branchSteps": []})["status"],
                         "invalidChange")
        action_change = {k: v for k, v in change.items() if k != "title"}
        self.assertEqual(self.call("PlanPlcGraphStepFlagActions", {**action_change, "flag": "TestActive",
                         "dataMemberParent": "Logistics"})["status"], "invalidDataBlockTarget")
        self.assertEqual(self.call("ApplyPlcGraphStepFlag", {"planId": "invalid", "approved": False})["status"], "approvalRequired")
        self.assertEqual(self.call("ApplyPlcGraphStepFlag", {"planId": "invalid", "approved": True})["status"], "planNotFound")
        self.assertEqual(self.call("ApplyPlcGraphStepFlagActions", {"planId": "invalid", "approved": False})["status"],
                         "approvalRequired")
        self.assertEqual(self.call("ApplyPlcGraphStepFlagActions", {"planId": "invalid", "approved": True})["status"],
                         "planNotFound")

    def test_bundled_project_knowledge(self):
        custom = Path(__file__).resolve().parents[1]
        documents = {
            "libraryUsage": "capabilities/CheckLibraryUsage.md",
            "projectConventions": "knowledge/TiaPortalProjectConventions.md",
            "iasLibrary": "knowledge/PiaIndustrialAppSuiteLibrary.md",
            "excelExport": "capabilities/ExportExcel.md",
            "temporaryPlcBlocks": "knowledge/TemporaryPlcBlockRules.md",
        }
        for document, path in documents.items():
            with self.subTest(document=document):
                result = self.call("GetProjectKnowledge", {"document": document})
                self.assertEqual(result["status"], "report")
                self.assertEqual(result["markdown"].splitlines(),
                                 (custom / path).read_text(encoding="utf-8-sig").splitlines())
        self.assertEqual(self.call("GetProjectKnowledge", {"document": "../../AGENTS.md"})["status"], "unknownDocument")

    def test_excel_export_rejects_invalid_table(self):
        args = {"title": "Export validation", "columns": ["First", "Second"]}
        self.assertEqual(self.call("ExportExcel", {**args, "rows": [["one"]]})["status"], "invalidRowLength")
        self.assertEqual(self.call("ExportExcel", {**args, "rows": [[{}, None]]})["status"], "invalidCell")
        self.assertEqual(self.call("ExportExcel", {**args, "rows": [["bad\u0001", None]]})["status"], "invalidCell")
        duplicate = self.call("ExportExcel", {**args, "columns": ["Same", " same "], "rows": []})
        self.assertEqual(duplicate["status"], "invalidColumns")

    def test_excel_export_creates_one_plain_sheet(self):
        marker = "ExportTest-" + uuid.uuid4().hex
        headers = ["Station", "Called", "Value", "In block"]
        rows = [["M0120", True, 12.5, "=1+1"], ["Ä & < >", False, None, "_x0041_"]]
        args = {"title": "../" + marker, "columns": headers, "rows": rows}

        def export():
            report = self.call("ExportExcel", args)
            self.assertEqual(report["status"], "exported", report)
            path = Path(report["path"])
            self.assertTrue(path.is_absolute())
            self.assertEqual(path.parent.parts[-2:], ("PIA-MCP", "Exports"))
            self.assertRegex(path.name, rf"^{marker}(?:-\d+)?_\d{{4}}-\d{{2}}-\d{{2}}_\d{{2}}-\d{{2}}\.xlsx$")
            self.addCleanup(path.unlink, missing_ok=True)
            self.assertEqual((report["rowCount"], report["columnCount"]), (2, 4))
            return path

        path = export()
        self.assertNotEqual(path, export())
        ns = {"s": "http://schemas.openxmlformats.org/spreadsheetml/2006/main"}
        with ZipFile(path) as archive:
            workbook = ElementTree.fromstring(archive.read("xl/workbook.xml"))
            self.assertEqual(len(workbook.findall("s:sheets/s:sheet", ns)), 1)
            sheet = ElementTree.fromstring(archive.read("xl/worksheets/sheet1.xml"))

        exported_rows = sheet.findall("s:sheetData/s:row", ns)
        self.assertEqual([row.get("r") for row in exported_rows], ["1", "2", "3"])
        self.assertEqual([cell.text for cell in exported_rows[0].findall("s:c/s:is/s:t", ns)], headers)
        cells = {cell.get("r"): cell for cell in sheet.findall(".//s:c", ns)}
        self.assertEqual(cells["D2"].findtext("s:is/s:t", namespaces=ns), "=1+1")
        self.assertIsNone(cells["D2"].find("s:f", ns))
        self.assertEqual(cells["C2"].get("t"), "n")
        self.assertEqual(cells["C2"].findtext("s:v", namespaces=ns), "12.5")
        self.assertEqual(cells["B3"].get("t"), "b")
        self.assertEqual(cells["B3"].findtext("s:v", namespaces=ns), "0")
        self.assertNotIn("C3", cells)
        self.assertEqual(cells["A3"].findtext("s:is/s:t", namespaces=ns), "Ä & < >")
        self.assertEqual(cells["D3"].findtext("s:is/s:t", namespaces=ns), "_x005F_x0041_")

    def test_live_project_structure(self):
        plc = os.getenv("PIA_TEST_STRUCTURE_PLC")
        folder = os.getenv("PIA_TEST_STRUCTURE_FOLDER")
        if not plc or not folder:
            self.skipTest("Set PIA_TEST_STRUCTURE_PLC and PIA_TEST_STRUCTURE_FOLDER to a program folder with children.")
        args = {"softwarePath": plc, "folder": folder}
        shallow = self.call("GetProjectStructure", {**args, "maxDepth": 0})
        self.assertEqual(shallow["status"], "report", shallow)
        self.assertTrue(shallow["complete"], shallow["issues"])
        program = shallow["programs"][0]
        self.assertTrue(program["depthLimited"])
        self.assertEqual(len(program["folders"]), 1)
        self.assertEqual(program["folders"][0]["path"], folder)
        self.assertNotIn("blocks", program["folders"][0])
        expanded = self.call("GetProjectStructure", {**args, "maxDepth": 1, "includeBlocks": True})
        self.assertEqual(expanded["status"], "report", expanded)
        self.assertTrue(expanded["complete"], expanded["issues"])
        folders = expanded["programs"][0]["folders"]
        self.assertGreater(len(folders), 1)
        self.assertTrue(all(row["path"].startswith(folder + "/") for row in folders[1:]))
        self.assertTrue(any(row["blocks"] for row in folders[1:]))

    def test_live_global_type_identity(self):
        library = os.getenv("PIA_TEST_GLOBAL_LIBRARY")
        entry = os.getenv("PIA_TEST_GLOBAL_TYPE")
        if not library or not entry:
            self.skipTest("Set PIA_TEST_GLOBAL_LIBRARY and PIA_TEST_GLOBAL_TYPE to a known instantiated type.")
        result = self.call("CheckLibraryUsage", {"scope": "global", "library": library, "entry": entry, "includeReferences": False})
        self.assertEqual(result.get("status"), "report", result)
        self.assertTrue(result["complete"], result["issues"])
        self.assertEqual(result["entries"], 1)
        result = result["results"][0]
        self.assertEqual(result["matchBasis"], "typeAndVersionId")
        self.assertTrue(result["projectInstanceFound"])
        valid_ids = {v["versionId"] for v in result["matchedVersions"]}
        for instance in result["instances"]:
            self.assertIn(instance["versionId"], valid_ids)
            self.assertIsNone(instance.get("references"))

    def test_live_report(self):
        if not LIVE_FOLDER:
            self.skipTest("Pass a folder to inspect an open, saved V21 project.")
        report = self.call("CheckLibraryUsage", {"scope": "project", "folder": LIVE_FOLDER})
        if report.get("meta", {}).get("truncated"):
            data, meta = report["message"], report["meta"]
            while not meta["eof"]:
                offset = meta["nextOffset"]
                page = self.call("GetExport", {
                    "exportId": meta["exportId"], "offset": offset, "length": 12000,
                })
                meta = page["meta"]
                self.assertEqual(meta["offset"], offset)
                self.assertTrue(meta["eof"] or meta["nextOffset"] > offset)
                data += page["message"]
            report = json.loads(data)
        self.assertEqual(report.get("status"), "report", report.get("status"))
        self.assertEqual(report["scope"], "project")
        self.assertEqual(report["entries"], len(report["results"]))
        self.assertTrue(all(entry["entry"].startswith(report["folder"] + "/") for entry in report["results"]))
        self.assertEqual(report["requiresDecision"], any(entry["requiresDecision"] for entry in report["results"]))
        if report["complete"]:
            self.assertFalse(report["issues"])
            self.assertTrue(all(entry["complete"] for entry in report["results"]))
        print(f"Read {report['entries']} entries. Complete: {report['complete']}")

    def test_live_explicit_project_version(self):
        entry = os.getenv("PIA_TEST_PROJECT_TYPE")
        if not entry:
            self.skipTest("Set PIA_TEST_PROJECT_TYPE to a known instantiated project library type.")
        args = {"scope": "project", "entry": entry, "includeReferences": False}
        report = self.call("CheckLibraryUsage", args)
        self.assertEqual(report.get("status"), "report", report)
        self.assertTrue(report["complete"], report["issues"])
        original = report["results"][0]
        self.assertTrue(original["projectInstanceFound"])
        version_id = original["instances"][0]["versionId"]
        selected = self.call("CheckLibraryUsage", {**args, "versionId": version_id})
        self.assertEqual(selected.get("status"), "report", selected)
        self.assertTrue(selected["complete"], selected["issues"])
        self.assertEqual({instance["versionId"] for instance in selected["results"][0]["instances"]}, {version_id})
        missing = self.call("CheckLibraryUsage", {**args, "versionId": "ffffffff-ffff-ffff-ffff-ffffffffffff"})
        self.assertEqual(missing["status"], "versionNotFound")

    def test_live_master_copy_matches_by_name(self):
        entry = os.getenv("PIA_TEST_MASTER_COPY")
        if not entry:
            self.skipTest("Set PIA_TEST_MASTER_COPY to a project master copy with a known name match.")
        report = self.call("CheckLibraryUsage", {"scope": "project", "entry": entry, "includeReferences": False})
        self.assertEqual(report.get("status"), "report", report)
        self.assertTrue(report["complete"], report["issues"])
        result = report["results"][0]
        self.assertEqual(result["matchBasis"], "nameOnly")
        self.assertTrue(result["projectInstanceFound"])
        self.assertIsNone(result.get("typeId"))
        self.assertEqual(result["matchedVersions"], [])
        self.assertTrue(any(instance["projectObject"].split("/")[-1] == entry.split("/")[-1]
                            for instance in result["instances"]))

if __name__ == "__main__":
    LIVE_FOLDER = sys.argv.pop(1) if len(sys.argv) > 1 and not sys.argv[1].startswith("-") else None
    unittest.main()
