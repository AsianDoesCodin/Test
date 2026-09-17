from __future__ import annotations

import re
import sys
from pathlib import Path

if len(sys.argv) != 2:
    raise SystemExit("usage: generate_godot_class_cache.py <godot-project>")

root = Path(sys.argv[1]).resolve()
entries: list[tuple[str, str, str]] = []

for file in sorted(root.rglob("*.gd")):
    if ".godot" in file.parts:
        continue
    text = file.read_text(encoding="utf-8-sig", errors="replace")
    name_match = re.search(r"(?m)^\s*class_name\s+([A-Za-z_][A-Za-z0-9_]*)", text)
    if not name_match:
        continue
    base_match = re.search(r"(?m)^\s*extends\s+([A-Za-z_][A-Za-z0-9_]*)\s*$", text)
    if not base_match:
        raise SystemExit(f"Cannot seed global class cache: {file} has class_name but no simple extends Type line")
    rel = file.relative_to(root).as_posix()
    entries.append((name_match.group(1), base_match.group(1), rel))

cache_dir = root / ".godot"
cache_dir.mkdir(parents=True, exist_ok=True)
cache_file = cache_dir / "global_script_class_cache.cfg"

parts: list[str] = []
for class_name, base, rel in entries:
    parts.append(
        '{\n'
        f'"base": &"{base}",\n'
        f'"class": &"{class_name}",\n'
        '"icon": "",\n'
        '"language": &"GDScript",\n'
        f'"path": "res://{rel}"\n'
        '}'
    )

cache_file.write_text("list=Array[Dictionary]([" + ",\n".join(parts) + "])\n", encoding="utf-8")
print(f"Seeded {len(entries)} global GDScript classes in {cache_file}")
for class_name, base, rel in entries:
    print(f"  {class_name} : {base} @ res://{rel}")
