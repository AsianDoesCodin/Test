extends Window

const QuestDocumentClass = preload("res://src/Classes/File System/QuestDocument.gd")

var category_list: ItemList
var quest_list: ItemList
var new_category_name: LineEdit
var id_value: Label
var title_edit: LineEdit
var text_edit: TextEdit
var complete_edit: TextEdit
var completer_edit: LineEdit
var next_id_edit: SpinBox
var reward_edit: SpinBox
var type_value: Label
var objective_value: Label
var raw_edit: TextEdit
var status_label: Label
var graph_preview: TextEdit
var mcp_config_preview: TextEdit
var selected_category := ""
var current_doc: QuestDocument

func _ready():
	title = "Project Tools — Quests & AI"
	size = Vector2i(1280, 820)
	min_size = Vector2i(1000, 650)
	close_requested.connect(queue_free)
	_build_ui()
	_refresh_categories()

func _build_ui():
	var tabs := TabContainer.new()
	tabs.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(tabs)

	var quests_tab := VBoxContainer.new()
	quests_tab.name = "Quest Workbench"
	tabs.add_child(quests_tab)
	_build_quest_tab(quests_tab)

	var graph_tab := VBoxContainer.new()
	graph_tab.name = "AI Graph Context"
	tabs.add_child(graph_tab)
	_build_graph_tab(graph_tab)

	var mcp_tab := VBoxContainer.new()
	mcp_tab.name = "MCP Bridge"
	tabs.add_child(mcp_tab)
	_build_mcp_tab(mcp_tab)

func _build_quest_tab(parent: VBoxContainer):
	var toolbar := HBoxContainer.new()
	parent.add_child(toolbar)

	new_category_name = LineEdit.new()
	new_category_name.placeholder_text = "New quest category"
	new_category_name.custom_minimum_size = Vector2(210, 0)
	toolbar.add_child(new_category_name)
	var add_category := Button.new()
	add_category.text = "Add Category"
	add_category.pressed.connect(_add_category)
	toolbar.add_child(add_category)
	toolbar.add_child(VSeparator.new())
	for kind in ["Talk", "Kill", "Item"]:
		var b := Button.new()
		b.text = "New " + kind + " Quest"
		b.pressed.connect(_new_quest.bind(kind.to_lower()))
		toolbar.add_child(b)
	var refresh := Button.new()
	refresh.text = "Refresh"
	refresh.pressed.connect(_refresh_categories)
	toolbar.add_child(refresh)

	var split := HSplitContainer.new()
	split.size_flags_vertical = Control.SIZE_EXPAND_FILL
	parent.add_child(split)

	var left := VBoxContainer.new()
	left.custom_minimum_size = Vector2(300, 500)
	split.add_child(left)
	var category_label := Label.new()
	category_label.text = "Quest Categories"
	left.add_child(category_label)
	category_list = ItemList.new()
	category_list.custom_minimum_size = Vector2(280, 180)
	category_list.item_selected.connect(_category_selected)
	left.add_child(category_list)
	var quests_label := Label.new()
	quests_label.text = "Quests"
	left.add_child(quests_label)
	quest_list = ItemList.new()
	quest_list.size_flags_vertical = Control.SIZE_EXPAND_FILL
	quest_list.item_selected.connect(_quest_selected)
	left.add_child(quest_list)

	var right_scroll := ScrollContainer.new()
	right_scroll.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	right_scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	split.add_child(right_scroll)
	var form := VBoxContainer.new()
	form.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	right_scroll.add_child(form)

	id_value = _row_label(form, "Quest ID", "—")
	type_value = _row_label(form, "Native Type", "—")
	objective_value = _row_label(form, "Objective", "—")
	title_edit = _line_field(form, "Title")
	completer_edit = _line_field(form, "Completer NPC")
	text_edit = _text_field(form, "Quest Text", 105)
	complete_edit = _text_field(form, "Completion Text", 85)
	next_id_edit = _number_field(form, "Next Quest ID", -1, 999999, -1)
	reward_edit = _number_field(form, "Reward EXP", 0, 99999999, 0)

	var basic_buttons := HBoxContainer.new()
	form.add_child(basic_buttons)
	var save_basic := Button.new()
	save_basic.text = "Save Basic Fields (lossless)"
	save_basic.pressed.connect(_save_basic)
	basic_buttons.add_child(save_basic)
	var duplicate := Button.new()
	duplicate.text = "Duplicate Quest"
	duplicate.pressed.connect(_duplicate_current)
	basic_buttons.add_child(duplicate)

	var raw_label := Label.new()
	raw_label.text = "Advanced Native Record — editing this area writes the complete file"
	form.add_child(raw_label)
	raw_edit = TextEdit.new()
	raw_edit.custom_minimum_size = Vector2(700, 320)
	raw_edit.wrap_mode = TextEdit.LINE_WRAPPING_NONE
	form.add_child(raw_edit)
	var raw_buttons := HBoxContainer.new()
	form.add_child(raw_buttons)
	var save_raw := Button.new()
	save_raw.text = "Save Raw Native File"
	save_raw.pressed.connect(_save_raw)
	raw_buttons.add_child(save_raw)
	var reload_raw := Button.new()
	reload_raw.text = "Reload From Disk"
	reload_raw.pressed.connect(_reload_current)
	raw_buttons.add_child(reload_raw)
	status_label = Label.new()
	status_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	form.add_child(status_label)

func _build_graph_tab(parent: VBoxContainer):
	var info := Label.new()
	info.text = "This is the graph context exposed to AI. It includes the editor's saved YDEC positions plus dialogue/response connections when available."
	info.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	parent.add_child(info)
	var buttons := HBoxContainer.new()
	parent.add_child(buttons)
	var refresh := Button.new()
	refresh.text = "Refresh Current Dialog Category"
	refresh.pressed.connect(_refresh_graph_preview)
	buttons.add_child(refresh)
	var copy := Button.new()
	copy.text = "Copy Context"
	copy.pressed.connect(_copy_graph)
	buttons.add_child(copy)
	graph_preview = TextEdit.new()
	graph_preview.editable = false
	graph_preview.wrap_mode = TextEdit.LINE_WRAPPING_NONE
	graph_preview.size_flags_vertical = Control.SIZE_EXPAND_FILL
	parent.add_child(graph_preview)
	_refresh_graph_preview()

func _build_mcp_tab(parent: VBoxContainer):
	var text := Label.new()
	text.text = "The installer includes a local stdio MCP bridge. It is read-only unless --allow-write is explicitly added. Point your MCP client at the installed YdeMcpBridge.exe and the active CustomNPCs folder."
	text.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	parent.add_child(text)
	var buttons := HBoxContainer.new()
	parent.add_child(buttons)
	var readonly := Button.new()
	readonly.text = "Copy Read-Only Config"
	readonly.pressed.connect(_copy_mcp_config.bind(false))
	buttons.add_child(readonly)
	var writable := Button.new()
	writable.text = "Copy Write-Enabled Config"
	writable.pressed.connect(_copy_mcp_config.bind(true))
	buttons.add_child(writable)
	mcp_config_preview = TextEdit.new()
	mcp_config_preview.editable = false
	mcp_config_preview.size_flags_vertical = Control.SIZE_EXPAND_FILL
	parent.add_child(mcp_config_preview)
	_update_mcp_preview(false)

func _row_label(parent: Control, label_text: String, value_text: String) -> Label:
	var row := HBoxContainer.new()
	parent.add_child(row)
	var label := Label.new()
	label.text = label_text
	label.custom_minimum_size = Vector2(160, 0)
	row.add_child(label)
	var value := Label.new()
	value.text = value_text
	value.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(value)
	return value

func _line_field(parent: Control, label_text: String) -> LineEdit:
	var row := HBoxContainer.new()
	parent.add_child(row)
	var label := Label.new()
	label.text = label_text
	label.custom_minimum_size = Vector2(160, 0)
	row.add_child(label)
	var edit := LineEdit.new()
	edit.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(edit)
	return edit

func _text_field(parent: Control, label_text: String, height: float) -> TextEdit:
	var label := Label.new()
	label.text = label_text
	parent.add_child(label)
	var edit := TextEdit.new()
	edit.custom_minimum_size = Vector2(600, height)
	parent.add_child(edit)
	return edit

func _number_field(parent: Control, label_text: String, minimum: float, maximum: float, initial: float) -> SpinBox:
	var row := HBoxContainer.new()
	parent.add_child(row)
	var label := Label.new()
	label.text = label_text
	label.custom_minimum_size = Vector2(160, 0)
	row.add_child(label)
	var spin := SpinBox.new()
	spin.min_value = minimum
	spin.max_value = maximum
	spin.value = initial
	spin.allow_greater = true
	row.add_child(spin)
	return spin

func _quest_root() -> String:
	return CurrentEnvironment.current_directory + "/quests"

func _refresh_categories():
	DirAccess.make_dir_recursive_absolute(_quest_root())
	category_list.clear()
	quest_list.clear()
	var dirs := Array(DirAccess.get_directories_at(_quest_root()))
	dirs.sort()
	for dir in dirs:
		category_list.add_item(dir)
		category_list.set_item_metadata(category_list.item_count - 1, dir)
	if !selected_category.is_empty() && dirs.has(selected_category):
		var idx := dirs.find(selected_category)
		category_list.select(idx)
		_load_quests(selected_category)
	elif !dirs.is_empty():
		category_list.select(0)
		_category_selected(0)
	else:
		_clear_form()

func _category_selected(index: int):
	selected_category = str(category_list.get_item_metadata(index))
	_load_quests(selected_category)

func _load_quests(category_name: String):
	quest_list.clear()
	var path := _quest_root() + "/" + category_name
	var ids: Array[int] = []
	for file in DirAccess.get_files_at(path):
		if file.get_extension().to_lower() == "json" && file.get_basename().is_valid_int():
			ids.append(int(file.get_basename()))
	ids.sort()
	for id in ids:
		var doc := QuestDocumentClass.load_file(path + "/" + str(id) + ".json", category_name)
		quest_list.add_item("Q" + str(id) + " — " + doc.get_string("Title", "Untitled"))
		quest_list.set_item_metadata(quest_list.item_count - 1, id)
	if quest_list.item_count > 0:
		quest_list.select(0)
		_quest_selected(0)
	else:
		_clear_form()

func _quest_selected(index: int):
	if selected_category.is_empty():
		return
	var id := int(quest_list.get_item_metadata(index))
	var path := _quest_root() + "/" + selected_category + "/" + str(id) + ".json"
	current_doc = QuestDocumentClass.load_file(path, selected_category)
	_populate_form()

func _populate_form():
	if current_doc == null:
		_clear_form()
		return
	id_value.text = str(current_doc.quest_id)
	var type_id := current_doc.get_int("Type", -1)
	type_value.text = _type_name(type_id) + " (" + str(type_id) + ")"
	objective_value.text = current_doc.objective_summary()
	title_edit.text = current_doc.get_string("Title")
	text_edit.text = current_doc.get_string("Text")
	complete_edit.text = current_doc.get_string("CompleteText")
	completer_edit.text = current_doc.get_string("CompleterNpc")
	next_id_edit.value = current_doc.get_int("NextQuestId", -1)
	reward_edit.value = current_doc.get_int("RewardExp", 0)
	raw_edit.text = current_doc.raw_text
	status_label.text = "Loaded " + current_doc.file_path

func _clear_form():
	current_doc = null
	if id_value:
		id_value.text = "—"
		type_value.text = "—"
		objective_value.text = "—"
		title_edit.text = ""
		text_edit.text = ""
		complete_edit.text = ""
		completer_edit.text = ""
		next_id_edit.value = -1
		reward_edit.value = 0
		raw_edit.text = ""

func _type_name(type_id: int) -> String:
	match type_id:
		0: return "Item"
		1: return "Talk / Dialog"
		2: return "Kill"
		_: return "Unknown Native Type"

func _save_basic():
	if current_doc == null:
		return
	current_doc.set_string("Title", title_edit.text)
	current_doc.set_string("Text", text_edit.text)
	current_doc.set_string("CompleteText", complete_edit.text)
	current_doc.set_string("CompleterNpc", completer_edit.text)
	current_doc.set_int("NextQuestId", int(next_id_edit.value))
	current_doc.set_int("RewardExp", int(reward_edit.value))
	var err := current_doc.save()
	status_label.text = "Saved without rebuilding unknown native fields." if err == OK else "Save failed: " + error_string(err)
	raw_edit.text = current_doc.raw_text
	_load_quests(selected_category)

func _save_raw():
	if current_doc == null:
		return
	current_doc.raw_text = raw_edit.text
	var err := current_doc.save()
	status_label.text = "Advanced native file saved." if err == OK else "Raw save failed: " + error_string(err)
	_reload_current()

func _reload_current():
	if current_doc == null:
		return
	current_doc = QuestDocumentClass.load_file(current_doc.file_path, current_doc.category)
	_populate_form()

func _add_category():
	var name := new_category_name.text.strip_edges()
	if name.is_empty() || "/" in name || "\\" in name || name == "." || name == "..":
		return
	DirAccess.make_dir_recursive_absolute(_quest_root() + "/" + name)
	selected_category = name
	new_category_name.text = ""
	_refresh_categories()

func _next_quest_id() -> int:
	var highest := 0
	for category in DirAccess.get_directories_at(_quest_root()):
		for file in DirAccess.get_files_at(_quest_root() + "/" + category):
			if file.get_extension().to_lower() == "json" && file.get_basename().is_valid_int():
				highest = maxi(highest, int(file.get_basename()))
	return highest + 1

func _new_quest(kind: String):
	if selected_category.is_empty():
		selected_category = "New Quests"
		DirAccess.make_dir_recursive_absolute(_quest_root() + "/" + selected_category)
	var id := _next_quest_id()
	var path := _quest_root() + "/" + selected_category + "/" + str(id) + ".json"
	var file := FileAccess.open(path, FileAccess.WRITE)
	if !file:
		return
	file.store_string(_quest_template(kind))
	file.close()
	_refresh_categories()
	status_label.text = "Created Q" + str(id) + " from the verified " + kind + " template."
	for i in quest_list.item_count:
		if int(quest_list.get_item_metadata(i)) == id:
			quest_list.select(i)
			_quest_selected(i)
			break

func _duplicate_current():
	if current_doc == null:
		return
	var id := _next_quest_id()
	var path := _quest_root() + "/" + selected_category + "/" + str(id) + ".json"
	var new_text := current_doc.raw_text
	var copy_doc := QuestDocumentClass.new()
	copy_doc.raw_text = new_text
	copy_doc.file_path = path
	copy_doc.category = selected_category
	copy_doc.quest_id = id
	copy_doc.set_string("Title", current_doc.get_string("Title", "Quest") + " (Copy)")
	copy_doc.save()
	_refresh_categories()

func _quest_template(kind: String) -> String:
	var objective := '  "QuestDialogs": [{"Integer": -1, "Slot": 0}],\n'
	var type_id := 1
	var completion := 1
	var extra := ""
	if kind == "kill":
		type_id = 2
		completion = 0
		objective = '  "QuestDialogs": [{"Value": 1, "Slot": "Mob"}],\n'
	elif kind == "item":
		type_id = 0
		completion = 0
		objective = '  "Items": {"NpcMiscInv": [{"Slot": 0b, "id": "minecraft:stone", "Count": 1b}]},\n'
		extra = '  "LeaveItems": 0b,\n'
	return "{\n" + \
		'  "CompleterNpc": "",\n' + \
		'  "NextQuestId": -1,\n' + \
		'  "RandomReward": 0b,\n' + \
		'  "QuestRepeat": 0,\n' + \
		'  "QuestCompletion": ' + str(completion) + ',\n' + \
		'  "IgnoreNBT": 0b,\n' + \
		'  "Title": "New ' + kind.capitalize() + ' Quest",\n' + \
		'  "Text": "Describe the objective.",\n' + \
		'  "QuestFactionPoints": {"DecreaseFaction1Points":0b,"OptionFaction2Points":100,"OptionFactions1":-1,"OptionFactions2":-1,"OptionFaction1Points":100,"DecreaseFaction2Points":0b},\n' + \
		'  "RewardExp": 0,\n' + \
		'  "QuestCommand": "",\n' + objective + \
		'  "ModRev": 18,\n' + \
		'  "Type": ' + str(type_id) + ',\n' + \
		'  "QuestMail": {"Sender":"","BeenRead":0b,"Message":{},"MailItems":[],"MailQuest":-1,"TimePast":0L,"Time":0L,"Subject":""},\n' + \
		'  "IgnoreDamage": 0b,\n' + \
		'  "Rewards": {"NpcMiscInv": []},\n' + extra + \
		'  "CompleteText": "Quest complete."\n' + \
		"}\n"

func _refresh_graph_preview():
	if graph_preview == null:
		return
	var category := str(CurrentEnvironment.current_category_name) if CurrentEnvironment.current_category_name != null else ""
	if category.is_empty():
		graph_preview.text = "Load a dialogue category first. The MCP bridge can still inspect all native files without a loaded category."
		return
	var ydec_path := CurrentEnvironment.current_directory + "/dialogs/" + category + "/" + category + ".ydec"
	if !FileAccess.file_exists(ydec_path):
		graph_preview.text = "No YDEC layout file found for " + category + ". Native dialogue data remains available, but visual node coordinates have not been saved."
		return
	var file := FileAccess.open(ydec_path, FileAccess.READ)
	var out := "CATEGORY: " + category + "\nYDEC: " + ydec_path + "\n\n"
	while file && !file.eof_reached():
		var line := file.get_line()
		if line.strip_edges().is_empty():
			continue
		var data = JSON.parse_string(line)
		if !(data is Dictionary) || data.get("node_type", "") != "Dialog Node":
			continue
		out += "D" + str(data.get("dialog_id", -1)) + "  " + str(data.get("dialog_title", "")) + "\n"
		out += "  position: (" + str(data.get("position_offset.x", 0)) + ", " + str(data.get("position_offset.y", 0)) + ")\n"
		out += "  text: " + str(data.get("text", "")).replace("\n", " ") + "\n"
		for response in data.get("response_options", []):
			if response is Dictionary:
				out += "    reply: " + str(response.get("response_title", "")) + " -> D" + str(response.get("to_dialog_id", -1)) + "\n"
		out += "\n"
	file.close()
	graph_preview.text = out

func _copy_graph():
	DisplayServer.clipboard_set(graph_preview.text)

func _mcp_config(write_enabled: bool) -> String:
	var args := ["--project", CurrentEnvironment.current_directory]
	if write_enabled:
		args.append("--allow-write")
	var config := {
		"mcpServers": {
			"yde-arvan": {
				"command": "<INSTALL_DIR>\\mcp\\YdeMcpBridge.exe",
				"args": args
			}
		}
	}
	return JSON.stringify(config, "  ")

func _update_mcp_preview(write_enabled: bool):
	if mcp_config_preview:
		mcp_config_preview.text = _mcp_config(write_enabled)

func _copy_mcp_config(write_enabled: bool):
	_update_mcp_preview(write_enabled)
	DisplayServer.clipboard_set(mcp_config_preview.text)
