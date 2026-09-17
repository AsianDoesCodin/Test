class_name QuestDocument
extends RefCounted

var file_path: String = ""
var raw_text: String = ""
var quest_id: int = -1
var category: String = ""

static func load_file(path: String, category_name: String = "") -> QuestDocument:
	var doc := QuestDocument.new()
	doc.file_path = path
	doc.category = category_name
	doc.quest_id = int(path.get_file().get_basename()) if path.get_file().get_basename().is_valid_int() else -1
	var file := FileAccess.open(path, FileAccess.READ)
	if file:
		doc.raw_text = file.get_as_text()
		file.close()
	return doc

func save() -> int:
	if file_path.is_empty():
		return ERR_FILE_BAD_PATH
	var file := FileAccess.open(file_path, FileAccess.WRITE)
	if !file:
		return FileAccess.get_open_error()
	file.store_string(raw_text)
	file.close()
	return OK

func get_string(key: String, fallback: String = "") -> String:
	var literal := _get_literal(key)
	if literal.is_empty():
		return fallback
	var parsed = JSON.parse_string(literal)
	return parsed if parsed is String else fallback

func get_int(key: String, fallback: int = 0) -> int:
	var literal := _get_literal(key)
	if literal.is_empty():
		return fallback
	literal = literal.strip_edges().trim_suffix(",")
	for suffix in ["b", "B", "s", "S", "l", "L", "f", "F", "d", "D"]:
		literal = literal.trim_suffix(suffix)
	return int(literal) if literal.is_valid_int() else fallback

func set_string(key: String, value: String) -> bool:
	return _replace_scalar_line(key, JSON.stringify(value))

func set_int(key: String, value: int) -> bool:
	var current := _get_literal(key).strip_edges().trim_suffix(",")
	var suffix := ""
	if current.length() > 0 && current.right(1) in ["b", "B", "s", "S", "l", "L", "f", "F", "d", "D"]:
		suffix = current.right(1)
	return _replace_scalar_line(key, str(value) + suffix)

func _get_literal(key: String) -> String:
	for line in raw_text.split("\n"):
		if ('"' + key + '"') in line:
			var colon := line.find(":")
			if colon >= 0:
				return line.substr(colon + 1).strip_edges().trim_suffix(",")
	return ""

func _replace_scalar_line(key: String, literal: String) -> bool:
	var lines := raw_text.split("\n")
	for i in lines.size():
		var line: String = lines[i]
		if ('"' + key + '"') in line:
			var quote_pos := line.find('"')
			var indent := line.left(quote_pos) if quote_pos >= 0 else ""
			var comma := "," if line.strip_edges().ends_with(",") else ""
			lines[i] = indent + '"' + key + '": ' + literal + comma
			raw_text = "\n".join(lines)
			return true
	return false

func objective_summary() -> String:
	match get_int("Type", -1):
		0:
			return "Item objective (edit advanced native data for item/NBT details)"
		1:
			return "Talk/dialog objective"
		2:
			return "Kill objective"
		_:
			return "Unknown/native quest type"
