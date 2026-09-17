from __future__ import annotations

import re
import shutil
import sys
from pathlib import Path

if len(sys.argv) != 2:
    raise SystemExit("usage: apply_patch.py <upstream-project-dir>")

root = Path(sys.argv[1]).resolve()
workspace = Path(__file__).resolve().parents[1]

if not (root / "project.godot").exists():
    raise SystemExit(f"Not a Godot project: {root}")

# Add lossless quest editing + Project Tools UI.
quest_dest = root / "src" / "Classes" / "File System" / "QuestDocument.gd"
quest_dest.parent.mkdir(parents=True, exist_ok=True)
shutil.copy2(workspace / "patch" / "QuestDocument.gd", quest_dest)

tools_dest = root / "src" / "UI" / "ProjectTools" / "ProjectTools.gd"
tools_dest.parent.mkdir(parents=True, exist_ok=True)
shutil.copy2(workspace / "patch" / "ProjectTools.gd", tools_dest)

# Godot 4.1 cannot infer these expressions because they depend on autoload values.
# Keep explicit String annotations in the patched copy so --check-only is deterministic.
tools_text = tools_dest.read_text(encoding="utf-8")
tools_text = tools_text.replace(
    'var ydec_path := CurrentEnvironment.current_directory + "/dialogs/" + category + "/" + category + ".ydec"',
    'var ydec_path: String = CurrentEnvironment.current_directory + "/dialogs/" + category + "/" + category + ".ydec"',
)
tools_text = tools_text.replace(
    'var out := "CATEGORY: " + category + "\\nYDEC: " + ydec_path + "\\n\\n"',
    'var out: String = "CATEGORY: " + category + "\\nYDEC: " + ydec_path + "\\n\\n"',
)
tools_dest.write_text(tools_text, encoding="utf-8")

# Upstream pins an SSH.NET version affected by 2026 security advisories.
# 2026.0.0 is the first fixed version and still exposes the SftpClient API used here.
csproj = root / "Yellow-s Dialog Editor.csproj"
csproj_text = csproj.read_text(encoding="utf-8")
csproj_text, changed = re.subn(
    r'(<PackageReference\s+Include="SSH\.NET"\s+Version=")([^"]+)("\s*/>)',
    r'\g<1>2026.0.0\g<3>',
    csproj_text,
    count=1,
)
if changed != 1 or 'Include="SSH.NET" Version="2026.0.0"' not in csproj_text:
    raise RuntimeError("Could not update the upstream SSH.NET PackageReference to 2026.0.0")
csproj.write_text(csproj_text, encoding="utf-8")

# The MCP bridge is built as its own .NET 8 sidecar. Do not copy its C# source
# beneath the Godot project: the upstream SDK-style csproj globs **/*.cs.
main_path = root / "src" / "UI" / "Editor" / "MainEditor.gd"
main = main_path.read_text(encoding="utf-8")
needle = '\t$DialogEditor.use_snap = GlobalDeclarations.snap_enabled\n'
if "_install_project_tools_button()" not in main:
    if needle not in main:
        raise RuntimeError("Could not find MainEditor _ready insertion point")
    main = main.replace(needle, needle + '\t_install_project_tools_button()\n', 1)

append = r'''

# Arvan AI/Quest extension. Created programmatically so the original scene remains compatible.
func _install_project_tools_button():
	var container = $TopPanel/TopPanelContainer
	if container.get_node_or_null("ProjectToolsButton"):
		return
	var separator := VSeparator.new()
	separator.name = "ProjectToolsSeparator"
	separator.custom_minimum_size = Vector2(18, 0)
	container.add_child(separator)
	var button := Button.new()
	button.name = "ProjectToolsButton"
	button.text = "QUESTS / AI"
	button.tooltip_text = "Quest Workbench, AI graph context and MCP bridge setup"
	button.pressed.connect(_open_project_tools)
	container.add_child(button)

func _open_project_tools():
	var existing = get_node_or_null("ProjectTools")
	if existing:
		existing.show()
		return
	var tools_script = load("res://src/UI/ProjectTools/ProjectTools.gd")
	var window = tools_script.new()
	window.name = "ProjectTools"
	add_child(window)
	window.popup_centered()
'''
if "func _open_project_tools():" not in main:
    main += append
main_path.write_text(main, encoding="utf-8")

# Brand the build without replacing upstream attribution/links.
landing = root / "src" / "UI" / "LandingScreen.tscn"
text = landing.read_text(encoding="utf-8")
text = text.replace('text = "[b]v10.5"', 'text = "[b]v10.5 + Arvan AI"')
text = text.replace('text = "2025-07-19"', 'text = "2026-09-17"')
landing.write_text(text, encoding="utf-8")

# Include scripts in export for easier diagnostics.
presets = root / "export_presets.cfg"
preset_text = presets.read_text(encoding="utf-8")
preset_text = preset_text.replace('dotnet/include_scripts_content=false', 'dotnet/include_scripts_content=true')
presets.write_text(preset_text, encoding="utf-8")

(root / "ARVAN_AI_README.md").write_text(r'''# Yellow's Dialog Editor — Arvan AI build

This package is based on Yellow768/Yellows-Dialog-Editor v10.5 and keeps the original editor workflow.

## Added in this build

- Quest Workbench inside the main editor (`QUESTS / AI` toolbar button).
- Lossless basic-field editing for native CustomNPCs quest records: unknown/native fields are preserved.
- Verified starter templates for the quest shapes currently used by Arvan: Talk/Dialog (Type 1), Kill (Type 2), and Item (Type 0).
- Advanced native quest editor for fields not yet modeled by the UI.
- AI Graph Context tab that exposes saved YDEC node positions and reply connections.
- Companion `YdeMcpBridge` stdio MCP server for dialogs + quests.
- Project validation for dangling dialog/quest references and highest-index drift.
- SSH.NET bumped to 2026.0.0 to avoid the security advisories affecting the upstream pinned dependency.

## MCP safety

The bridge is read-only by default. Write operations require the bridge to be launched with `--allow-write` or `YDE_MCP_ALLOW_WRITE=1`.

The bridge accepts either `--project <path-to-customnpcs>` or a `project_path` argument on individual tools.

## Native-format boundary

The Quest Workbench intentionally edits common scalar fields in-place instead of parsing and reserializing the complete native record. This avoids destroying unknown GBPort tags or typed values such as `0b` and `0L`. The advanced raw editor is intentionally explicit because saving it replaces the complete quest file.
''', encoding="utf-8")

print("Applied Arvan AI quest/MCP patch to", root)
