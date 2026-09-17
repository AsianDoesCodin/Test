using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

ProjectContext.Configure(args);

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();
await builder.Build().RunAsync();

internal static class ProjectContext
{
    public static string? Root { get; private set; }
    public static bool AllowWrite { get; private set; }

    public static void Configure(string[] args)
    {
        Root = Environment.GetEnvironmentVariable("YDE_PROJECT");
        AllowWrite = Environment.GetEnvironmentVariable("YDE_MCP_ALLOW_WRITE") == "1";
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--project" && i + 1 < args.Length) Root = args[++i];
            else if (args[i] == "--allow-write") AllowWrite = true;
        }
        if (!string.IsNullOrWhiteSpace(Root)) Root = Path.GetFullPath(Root);
    }

    public static string ResolveRoot(string? supplied)
    {
        var root = string.IsNullOrWhiteSpace(supplied) ? Root : supplied;
        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException("No CustomNPCs project is configured. Pass --project <customnpcs folder> or provide project_path.");
        root = Path.GetFullPath(root);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        if (!Directory.Exists(Path.Combine(root, "dialogs")))
            throw new InvalidOperationException($"{root} has no dialogs folder and does not look like a CustomNPCs project.");
        return root;
    }

    public static void RequireWrite()
    {
        if (!AllowWrite)
            throw new InvalidOperationException("Bridge is read-only. Launch with --allow-write or YDE_MCP_ALLOW_WRITE=1 to enable mutations.");
    }
}

[McpServerToolType]
public static class YdeTools
{
    [McpServerTool, Description("Returns project counts, categories, highest IDs, and validation results.")]
    public static string ProjectSummary(string? project_path = null)
    {
        var root = ProjectContext.ResolveRoot(project_path);
        return CnpcProject.Json(CnpcProject.Summary(root));
    }

    [McpServerTool, Description("Lists dialogs with category, ID, title, start quest and reply targets.")]
    public static string ListDialogs(string? project_path = null, string? category = null)
    {
        var root = ProjectContext.ResolveRoot(project_path);
        return CnpcProject.Json(CnpcProject.DialogFiles(root, category).Select(CnpcProject.DialogSummary).ToArray());
    }

    [McpServerTool, Description("Returns a full dialog record with text, replies, availability gates, raw native text, and YDEC visual position when available.")]
    public static string GetDialog(int dialog_id, string? project_path = null)
    {
        var root = ProjectContext.ResolveRoot(project_path);
        var file = CnpcProject.FindNumericFile(Path.Combine(root, "dialogs"), dialog_id)
                   ?? throw new FileNotFoundException($"Dialog {dialog_id} was not found.");
        return CnpcProject.Json(CnpcProject.DialogDetail(root, file));
    }

    [McpServerTool, Description("Returns a category graph with full dialog content, reply edges, quest gates, and saved YDEC X/Y positions for AI visual context.")]
    public static string GetDialogGraph(string category, string? project_path = null)
    {
        var root = ProjectContext.ResolveRoot(project_path);
        return CnpcProject.Json(CnpcProject.DialogGraph(root, category));
    }

    [McpServerTool, Description("Lists quests with category, ID, title, type, completer NPC and objective summary.")]
    public static string ListQuests(string? project_path = null, string? category = null)
    {
        var root = ProjectContext.ResolveRoot(project_path);
        return CnpcProject.Json(CnpcProject.QuestFiles(root, category).Select(CnpcProject.QuestSummary).ToArray());
    }

    [McpServerTool, Description("Returns one quest with parsed common fields and the complete native record.")]
    public static string GetQuest(int quest_id, string? project_path = null)
    {
        var root = ProjectContext.ResolveRoot(project_path);
        var file = CnpcProject.FindNumericFile(Path.Combine(root, "quests"), quest_id)
                   ?? throw new FileNotFoundException($"Quest {quest_id} was not found.");
        return CnpcProject.Json(CnpcProject.QuestDetail(file));
    }

    [McpServerTool, Description("Checks dangling dialog links, missing quest/dialog availability references, talk-quest targets, duplicate IDs, and highest_index drift.")]
    public static string ValidateProject(string? project_path = null)
    {
        var root = ProjectContext.ResolveRoot(project_path);
        return CnpcProject.Json(CnpcProject.Validate(root));
    }

    [McpServerTool, Description("Creates a dialog category. Requires explicit MCP write mode.")]
    public static string CreateDialogCategory(string name, string? project_path = null)
    {
        ProjectContext.RequireWrite();
        var root = ProjectContext.ResolveRoot(project_path);
        var path = Path.Combine(root, "dialogs", CnpcProject.SafeName(name));
        Directory.CreateDirectory(path);
        return CnpcProject.Json(new { created = path });
    }

    [McpServerTool, Description("Creates a quest category. Requires explicit MCP write mode.")]
    public static string CreateQuestCategory(string name, string? project_path = null)
    {
        ProjectContext.RequireWrite();
        var root = ProjectContext.ResolveRoot(project_path);
        var path = Path.Combine(root, "quests", CnpcProject.SafeName(name));
        Directory.CreateDirectory(path);
        return CnpcProject.Json(new { created = path });
    }

    [McpServerTool, Description("Creates a GBPort-style dialog and advances dialogs/highest_index.json. Requires explicit MCP write mode.")]
    public static string CreateDialog(string category, string title, string text, int start_quest = -1, string? project_path = null)
    {
        ProjectContext.RequireWrite();
        var root = ProjectContext.ResolveRoot(project_path);
        var id = CnpcProject.NextId(Path.Combine(root, "dialogs"));
        var dir = Path.Combine(root, "dialogs", CnpcProject.SafeName(category));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"{id}.json");
        File.WriteAllText(file, CnpcProject.NewDialogTemplate(id, title, text, start_quest));
        File.WriteAllText(Path.Combine(root, "dialogs", "highest_index.json"), id.ToString());
        return CnpcProject.Json(CnpcProject.DialogDetail(root, file));
    }

    [McpServerTool, Description("Adds a response option to a dialog. target_dialog_id=-1 creates a terminal reply. Requires explicit MCP write mode.")]
    public static string AddDialogReply(int dialog_id, string reply_title, int target_dialog_id, string? project_path = null)
    {
        ProjectContext.RequireWrite();
        var root = ProjectContext.ResolveRoot(project_path);
        var file = CnpcProject.FindNumericFile(Path.Combine(root, "dialogs"), dialog_id)
                   ?? throw new FileNotFoundException($"Dialog {dialog_id} was not found.");
        File.WriteAllText(file, CnpcProject.AddReply(File.ReadAllText(file), reply_title, target_dialog_id));
        return CnpcProject.Json(CnpcProject.DialogDetail(root, file));
    }

    [McpServerTool, Description("Updates dialog title/text in place without rebuilding unknown native fields. Requires explicit MCP write mode.")]
    public static string UpdateDialog(int dialog_id, string? title = null, string? text = null, string? project_path = null)
    {
        ProjectContext.RequireWrite();
        var root = ProjectContext.ResolveRoot(project_path);
        var file = CnpcProject.FindNumericFile(Path.Combine(root, "dialogs"), dialog_id)
                   ?? throw new FileNotFoundException($"Dialog {dialog_id} was not found.");
        var native = File.ReadAllText(file);
        if (title is not null) native = CnpcProject.ReplaceStringField(native, "DialogTitle", title);
        if (text is not null) native = CnpcProject.ReplaceStringField(native, "DialogText", text);
        File.WriteAllText(file, native);
        return CnpcProject.Json(CnpcProject.DialogDetail(root, file));
    }

    [McpServerTool, Description("Creates a quest from a verified native template: talk, kill, or item. Requires explicit MCP write mode.")]
    public static string CreateQuest(string category, string quest_type, string title, string text,
        string completer_npc = "", string objective_name = "", int objective_count = 1,
        int dialog_id = -1, string? project_path = null)
    {
        ProjectContext.RequireWrite();
        var root = ProjectContext.ResolveRoot(project_path);
        var id = CnpcProject.NextId(Path.Combine(root, "quests"));
        var dir = Path.Combine(root, "quests", CnpcProject.SafeName(category));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"{id}.json");
        File.WriteAllText(file, CnpcProject.NewQuestTemplate(quest_type, title, text, completer_npc, objective_name, objective_count, dialog_id));
        return CnpcProject.Json(CnpcProject.QuestDetail(file));
    }

    [McpServerTool, Description("Updates common quest scalar fields in place while preserving all other native fields. Requires explicit MCP write mode.")]
    public static string UpdateQuest(int quest_id, string? title = null, string? text = null,
        string? complete_text = null, string? completer_npc = null, int? next_quest_id = null,
        int? reward_exp = null, string? project_path = null)
    {
        ProjectContext.RequireWrite();
        var root = ProjectContext.ResolveRoot(project_path);
        var file = CnpcProject.FindNumericFile(Path.Combine(root, "quests"), quest_id)
                   ?? throw new FileNotFoundException($"Quest {quest_id} was not found.");
        var native = File.ReadAllText(file);
        if (title is not null) native = CnpcProject.ReplaceStringField(native, "Title", title);
        if (text is not null) native = CnpcProject.ReplaceStringField(native, "Text", text);
        if (complete_text is not null) native = CnpcProject.ReplaceStringField(native, "CompleteText", complete_text);
        if (completer_npc is not null) native = CnpcProject.ReplaceStringField(native, "CompleterNpc", completer_npc);
        if (next_quest_id.HasValue) native = CnpcProject.ReplaceIntField(native, "NextQuestId", next_quest_id.Value);
        if (reward_exp.HasValue) native = CnpcProject.ReplaceIntField(native, "RewardExp", reward_exp.Value);
        File.WriteAllText(file, native);
        return CnpcProject.Json(CnpcProject.QuestDetail(file));
    }
}

internal static class CnpcProject
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public static string Json(object? value) => JsonSerializer.Serialize(value, JsonOptions);

    public static string SafeName(string value)
    {
        value = value.Trim();
        if (string.IsNullOrWhiteSpace(value) || value is "." or ".." || value.Contains('/') || value.Contains('\\'))
            throw new ArgumentException("Invalid category name.");
        if (Path.GetInvalidFileNameChars().Any(value.Contains))
            throw new ArgumentException("Category name contains an invalid filename character.");
        return value;
    }

    public static IEnumerable<string> NumericJsonFiles(string root)
    {
        if (!Directory.Exists(root)) yield break;
        foreach (var file in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
            if (int.TryParse(Path.GetFileNameWithoutExtension(file), out _)) yield return file;
    }

    public static string? FindNumericFile(string root, int id) =>
        NumericJsonFiles(root).FirstOrDefault(f => Path.GetFileNameWithoutExtension(f) == id.ToString());

    public static int ParseFileId(string file) => int.Parse(Path.GetFileNameWithoutExtension(file));

    public static int NextId(string root)
    {
        var ids = NumericJsonFiles(root).Select(ParseFileId).ToArray();
        return ids.Length == 0 ? 1 : ids.Max() + 1;
    }

    public static IEnumerable<string> DialogFiles(string root, string? category = null)
    {
        var dir = Path.Combine(root, "dialogs");
        if (!string.IsNullOrWhiteSpace(category)) dir = Path.Combine(dir, SafeName(category));
        return NumericJsonFiles(dir).OrderBy(ParseFileId);
    }

    public static IEnumerable<string> QuestFiles(string root, string? category = null)
    {
        var dir = Path.Combine(root, "quests");
        if (!string.IsNullOrWhiteSpace(category)) dir = Path.Combine(dir, SafeName(category));
        return NumericJsonFiles(dir).OrderBy(ParseFileId);
    }

    public static string CategoryOf(string typeRoot, string file)
    {
        var rel = Path.GetRelativePath(typeRoot, Path.GetDirectoryName(file)!);
        return rel == "." ? "" : rel.Replace('\\', '/');
    }

    public static object DialogSummary(string file)
    {
        var native = File.ReadAllText(file);
        return new
        {
            id = GetInt(native, "DialogId", ParseFileId(file)),
            title = GetString(native, "DialogTitle"),
            start_quest = GetInt(native, "DialogQuest", -1),
            replies = ParseReplies(native)
        };
    }

    public static object DialogDetail(string root, string file)
    {
        var native = File.ReadAllText(file);
        var id = GetInt(native, "DialogId", ParseFileId(file));
        var category = CategoryOf(Path.Combine(root, "dialogs"), file);
        var layout = ReadYdecLayout(root, category);
        layout.TryGetValue(id, out var pos);
        return new
        {
            id,
            category,
            title = GetString(native, "DialogTitle"),
            text = GetString(native, "DialogText"),
            start_quest = GetInt(native, "DialogQuest", -1),
            quest_availability = Enumerable.Range(0, 4).Select(i => Availability(native, "Quest", i)).ToArray(),
            dialog_availability = Enumerable.Range(0, 4).Select(i => Availability(native, "Dialog", i)).ToArray(),
            replies = ParseReplies(native),
            visual_position = pos,
            file,
            raw = native
        };
    }

    private static object Availability(string native, string kind, int index)
    {
        var suffix = index == 0 ? "" : (index + 1).ToString();
        return new
        {
            mode = GetInt(native, $"Availability{kind}{suffix}", 0),
            id = GetInt(native, $"Availability{kind}{suffix}Id", -1)
        };
    }

    public static object QuestSummary(string file)
    {
        var native = File.ReadAllText(file);
        return new
        {
            id = ParseFileId(file),
            title = GetString(native, "Title"),
            type = GetInt(native, "Type", -1),
            completer_npc = GetString(native, "CompleterNpc"),
            objective = QuestObjective(native)
        };
    }

    public static object QuestDetail(string file)
    {
        var native = File.ReadAllText(file);
        return new
        {
            id = ParseFileId(file),
            title = GetString(native, "Title"),
            text = GetString(native, "Text"),
            complete_text = GetString(native, "CompleteText"),
            type = GetInt(native, "Type", -1),
            completer_npc = GetString(native, "CompleterNpc"),
            next_quest_id = GetInt(native, "NextQuestId", -1),
            reward_exp = GetInt(native, "RewardExp", 0),
            objective = QuestObjective(native),
            file,
            raw = native
        };
    }

    public static object QuestObjective(string native)
    {
        var type = GetInt(native, "Type", -1);
        if (type == 1)
        {
            var m = Regex.Match(native, "\\\"Integer\\\"\\s*:\\s*(?<id>-?\\d+)");
            return new { kind = "talk", dialog_id = m.Success ? int.Parse(m.Groups["id"].Value) : -1 };
        }
        if (type == 2)
        {
            var m = Regex.Match(native,
                "\\\"Value\\\"\\s*:\\s*(?<count>\\d+).*?\\\"Slot\\\"\\s*:\\s*\\\"(?<name>(?:\\\\.|[^\\\"])*)\\\"",
                RegexOptions.Singleline);
            return new
            {
                kind = "kill",
                count = m.Success ? int.Parse(m.Groups["count"].Value) : 0,
                entity = m.Success ? DecodeJsonString(m.Groups["name"].Value) : ""
            };
        }
        if (type == 0)
        {
            var id = Regex.Match(native, "\\\"id\\\"\\s*:\\s*\\\"(?<id>[^\\\"]+)\\\"");
            var count = Regex.Match(native, "\\\"Count\\\"\\s*:\\s*(?<count>\\d+)b?");
            return new
            {
                kind = "item",
                item = id.Success ? id.Groups["id"].Value : "",
                count = count.Success ? int.Parse(count.Groups["count"].Value) : 0
            };
        }
        return new { kind = "unknown", type };
    }

    public static object DialogGraph(string root, string category)
    {
        var layout = ReadYdecLayout(root, category);
        var nodes = DialogFiles(root, category).Select(file =>
        {
            var native = File.ReadAllText(file);
            var id = GetInt(native, "DialogId", ParseFileId(file));
            layout.TryGetValue(id, out var pos);
            return new
            {
                id,
                title = GetString(native, "DialogTitle"),
                text = GetString(native, "DialogText"),
                start_quest = GetInt(native, "DialogQuest", -1),
                quest_availability = Enumerable.Range(0, 4).Select(i => Availability(native, "Quest", i)).ToArray(),
                position = pos,
                replies = ParseReplies(native)
            };
        }).ToArray();
        return new { category, nodes };
    }

    public static Dictionary<int, object> ReadYdecLayout(string root, string category)
    {
        var result = new Dictionary<int, object>();
        var categoryDir = Path.Combine(root, "dialogs", category.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(categoryDir)) return result;
        var ydec = Directory.EnumerateFiles(categoryDir, "*.ydec", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (ydec is null) return result;

        foreach (var line in File.ReadLines(ydec))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var e = doc.RootElement;
                if (!e.TryGetProperty("node_type", out var nodeType) || nodeType.GetString() != "Dialog Node") continue;
                if (!e.TryGetProperty("dialog_id", out var idValue)) continue;
                var id = idValue.GetInt32();
                var x = e.TryGetProperty("position_offset.x", out var xv) ? xv.GetDouble() : 0;
                var y = e.TryGetProperty("position_offset.y", out var yv) ? yv.GetDouble() : 0;
                result[id] = new { x, y, source = "ydec" };
            }
            catch { /* A malformed editor line should not block native context. */ }
        }
        return result;
    }

    public static object Summary(string root)
    {
        var dialogs = DialogFiles(root).ToArray();
        var quests = QuestFiles(root).ToArray();
        return new
        {
            project_root = root,
            write_enabled = ProjectContext.AllowWrite,
            dialog_count = dialogs.Length,
            quest_count = quests.Length,
            highest_dialog_id = dialogs.Length == 0 ? 0 : dialogs.Max(ParseFileId),
            highest_quest_id = quests.Length == 0 ? 0 : quests.Max(ParseFileId),
            dialog_categories = Directories(Path.Combine(root, "dialogs")),
            quest_categories = Directories(Path.Combine(root, "quests")),
            validation = Validate(root)
        };
    }

    public static object Validate(string root)
    {
        var dialogFiles = DialogFiles(root).ToArray();
        var questFiles = QuestFiles(root).ToArray();
        var dialogIds = dialogFiles.Select(ParseFileId).ToHashSet();
        var questIds = questFiles.Select(ParseFileId).ToHashSet();
        var errors = new List<string>();
        var warnings = new List<string>();

        foreach (var dup in dialogFiles.GroupBy(ParseFileId).Where(g => g.Count() > 1))
            errors.Add($"Dialog ID {dup.Key} exists more than once.");
        foreach (var dup in questFiles.GroupBy(ParseFileId).Where(g => g.Count() > 1))
            errors.Add($"Quest ID {dup.Key} exists more than once.");

        foreach (var file in dialogFiles)
        {
            var native = File.ReadAllText(file);
            var id = ParseFileId(file);
            foreach (var reply in ParseRepliesTyped(native))
                if (reply.TargetDialogId >= 0 && !dialogIds.Contains(reply.TargetDialogId))
                    errors.Add($"D{id} links to missing D{reply.TargetDialogId}.");

            var startQuest = GetInt(native, "DialogQuest", -1);
            if (startQuest >= 0 && !questIds.Contains(startQuest)) errors.Add($"D{id} starts missing Q{startQuest}.");

            for (var i = 0; i < 4; i++)
            {
                var suffix = i == 0 ? "" : (i + 1).ToString();
                var qMode = GetInt(native, $"AvailabilityQuest{suffix}", 0);
                var qId = GetInt(native, $"AvailabilityQuest{suffix}Id", -1);
                if (qMode != 0 && qId >= 0 && !questIds.Contains(qId)) errors.Add($"D{id} availability references missing Q{qId}.");
                var dMode = GetInt(native, $"AvailabilityDialog{suffix}", 0);
                var dId = GetInt(native, $"AvailabilityDialog{suffix}Id", -1);
                if (dMode != 0 && dId >= 0 && !dialogIds.Contains(dId)) errors.Add($"D{id} availability references missing D{dId}.");
            }
        }

        foreach (var file in questFiles)
        {
            var native = File.ReadAllText(file);
            var id = ParseFileId(file);
            if (GetInt(native, "Type", -1) != 1) continue;
            foreach (Match m in Regex.Matches(native, "\\\"Integer\\\"\\s*:\\s*(?<id>-?\\d+)"))
            {
                var dialogId = int.Parse(m.Groups["id"].Value);
                if (dialogId >= 0 && !dialogIds.Contains(dialogId)) errors.Add($"Q{id} talk objective references missing D{dialogId}.");
            }
        }

        var highestFile = Path.Combine(root, "dialogs", "highest_index.json");
        if (File.Exists(highestFile) && int.TryParse(File.ReadAllText(highestFile).Trim(), out var recorded))
        {
            var actual = dialogIds.Count == 0 ? 0 : dialogIds.Max();
            if (recorded < actual) warnings.Add($"dialogs/highest_index.json is {recorded}, below actual highest dialog ID {actual}.");
        }

        return new { valid = errors.Count == 0, errors, warnings };
    }

    private static string[] Directories(string root)
    {
        if (!Directory.Exists(root)) return Array.Empty<string>();
        return Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName).Where(x => x is not null).Cast<string>().OrderBy(x => x).ToArray();
    }

    public static string GetString(string native, string key, string fallback = "")
    {
        var pattern = "\\\"" + Regex.Escape(key) + "\\\"\\s*:\\s*\\\"(?<v>(?:\\\\.|[^\\\"])*)\\\"";
        var m = Regex.Match(native, pattern);
        return m.Success ? DecodeJsonString(m.Groups["v"].Value) : fallback;
    }

    public static int GetInt(string native, string key, int fallback = 0)
    {
        var pattern = "\\\"" + Regex.Escape(key) + "\\\"\\s*:\\s*(?<v>-?\\d+)(?:[bBsSlLfFdD])?";
        var m = Regex.Match(native, pattern);
        return m.Success && int.TryParse(m.Groups["v"].Value, out var value) ? value : fallback;
    }

    private static string DecodeJsonString(string escaped)
    {
        try { return JsonSerializer.Deserialize<string>("\"" + escaped + "\"") ?? ""; }
        catch { return escaped.Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\\\", "\\"); }
    }

    public static string ReplaceStringField(string native, string key, string value)
    {
        var pattern = "\\\"" + Regex.Escape(key) + "\\\"\\s*:\\s*\\\"(?:\\\\.|[^\\\"])*\\\"";
        var rx = new Regex(pattern);
        var m = rx.Match(native);
        if (!m.Success) return native;
        var replacement = "\"" + key + "\": " + JsonSerializer.Serialize(value);
        return native[..m.Index] + replacement + native[(m.Index + m.Length)..];
    }

    public static string ReplaceIntField(string native, string key, int value)
    {
        var pattern = "\\\"" + Regex.Escape(key) + "\\\"\\s*:\\s*-?\\d+(?:[bBsSlLfFdD])?";
        var rx = new Regex(pattern);
        var m = rx.Match(native);
        if (!m.Success) return native;
        var suffix = Regex.Match(m.Value, "[bBsSlLfFdD]$").Value;
        var replacement = "\"" + key + "\": " + value + suffix;
        return native[..m.Index] + replacement + native[(m.Index + m.Length)..];
    }

    private sealed record ReplyInfo(int Slot, string Title, int TargetDialogId, int OptionType);

    private static ReplyInfo[] ParseRepliesTyped(string native)
    {
        var block = ExtractArray(native, "Options");
        if (block is null) return Array.Empty<ReplyInfo>();
        var results = new List<ReplyInfo>();
        foreach (var obj in SplitTopLevelObjects(block))
        {
            results.Add(new ReplyInfo(
                GetInt(obj, "OptionSlot", results.Count),
                GetString(obj, "Title"),
                GetInt(obj, "Dialog", -1),
                GetInt(obj, "OptionType", 0)));
        }
        return results.ToArray();
    }

    public static object[] ParseReplies(string native) => ParseRepliesTyped(native)
        .Select(r => (object)new { slot = r.Slot, title = r.Title, target_dialog_id = r.TargetDialogId, option_type = r.OptionType })
        .ToArray();

    private static string? ExtractArray(string native, string key)
    {
        var keyPos = native.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
        if (keyPos < 0) return null;
        var start = native.IndexOf('[', keyPos);
        if (start < 0) return null;
        var close = FindMatching(native, start, '[', ']');
        return close < 0 ? null : native[(start + 1)..close];
    }

    private static IEnumerable<string> SplitTopLevelObjects(string body)
    {
        var results = new List<string>();
        var depth = 0;
        var start = -1;
        var inString = false;
        var escaped = false;
        for (var i = 0; i < body.Length; i++)
        {
            var c = body[i];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"') { inString = true; continue; }
            if (c == '{') { if (depth++ == 0) start = i; }
            else if (c == '}' && --depth == 0 && start >= 0)
            {
                results.Add(body[start..(i + 1)]);
                start = -1;
            }
        }
        return results;
    }

    public static string AddReply(string native, string replyTitle, int targetDialogId)
    {
        var keyPos = native.IndexOf("\"Options\"", StringComparison.Ordinal);
        if (keyPos < 0) throw new InvalidOperationException("Dialog has no Options array.");
        var open = native.IndexOf('[', keyPos);
        var close = open >= 0 ? FindMatching(native, open, '[', ']') : -1;
        if (open < 0 || close < 0) throw new InvalidOperationException("Dialog Options array is malformed.");
        var body = native[(open + 1)..close];
        var slotMatches = Regex.Matches(body, "\\\"OptionSlot\\\"\\s*:\\s*(?<slot>\\d+)").Cast<Match>().ToArray();
        var slots = slotMatches.Select(m => int.Parse(m.Groups["slot"].Value)).ToArray();
        var slot = slots.Length == 0 ? 0 : slots.Max() + 1;
        var optionType = targetDialogId < 0 ? 0 : 1;
        var entry = "{\"OptionSlot\":" + slot + ",\"Option\":{\"DialogCommand\":\"\",\"Dialog\":" + targetDialogId +
                    ",\"Title\":" + JsonSerializer.Serialize(replyTitle) + ",\"DialogColor\":16777215,\"OptionType\":" + optionType + "}}";
        var insertion = string.IsNullOrWhiteSpace(body) ? "\n    " + entry + "\n  " : body.TrimEnd() + ",\n    " + entry + "\n  ";
        return native[..(open + 1)] + insertion + native[close..];
    }

    private static int FindMatching(string text, int start, char openChar, char closeChar)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"') { inString = true; continue; }
            if (c == openChar) depth++;
            else if (c == closeChar && --depth == 0) return i;
        }
        return -1;
    }

    public static string NewDialogTemplate(int id, string title, string text, int startQuest)
    {
        var sb = new StringBuilder();
        void L(string line) => sb.AppendLine(line);
        L("{");
        L("  \"DialogShowWheel\": 0b,");
        L("  \"AvailabilityQuestId\": -1,");
        L("  \"Options\": [],");
        L("  \"AvailabilityScoreboardType\": 1,");
        L("  \"DialogHideNPC\": 0b,");
        L("  \"AvailabilityFactionStance\": 0,");
        L("  \"AvailabilityScoreboard2Value\": 0,");
        L($"  \"DialogId\": {id},");
        L("  \"AvailabilityQuest\": 0,");
        L("  \"AvailabilityDialog4\": 0,");
        L("  \"AvailabilityScoreboardObjective\": \"\",");
        L("  \"AvailabilityDialog3\": 0,");
        L("  \"AvailabilityQuest2\": 0,");
        L("  \"AvailabilityQuest3\": 0,");
        L("  \"AvailabilityScoreboard2Objective\": \"\",");
        L("  \"AvailabilityQuest4\": 0,");
        L("  \"ModRev\": 18,");
        L("  \"DecreaseFaction1Points\": 0b,");
        L($"  \"DialogQuest\": {startQuest},");
        L("  \"AvailabilityDialog2\": 0,");
        L("  \"OptionFactions1\": -1,");
        L("  \"AvailabilityDayTime\": 0,");
        L("  \"OptionFactions2\": -1,");
        L("  \"AvailabilityFaction2Id\": -1,");
        L("  \"OptionFaction1Points\": 0,");
        L("  \"AvailabilityScoreboardValue\": 0,");
        L("  \"DialogDisableEsc\": 1b,");
        L("  \"AvailabilityFaction\": 0,");
        L("  \"DialogTitle\": " + JsonSerializer.Serialize(title) + ",");
        L("  \"AvailabilityDialog\": 0,");
        L("  \"AvailabilityScoreboard2Type\": 1,");
        L("  \"AvailabilityFaction2\": 0,");
        L("  \"AvailabilityFactionId\": -1,");
        L("  \"AvailabilityFaction2Stance\": 0,");
        L("  \"DialogCommand\": \"\",");
        L("  \"AvailabilityDialogId\": -1,");
        L("  \"OptionFaction2Points\": 0,");
        L("  \"DialogText\": " + JsonSerializer.Serialize(text) + ",");
        L("  \"AvailabilityQuest4Id\": -1,");
        L("  \"AvailabilityQuest3Id\": -1,");
        L("  \"AvailabilityQuest2Id\": -1,");
        L("  \"AvailabilityDialog2Id\": -1,");
        L("  \"AvailabilityDialog3Id\": -1,");
        L("  \"AvailabilityDialog4Id\": -1,");
        L("  \"AvailabilityMinPlayerLevel\": 0,");
        L("  \"DecreaseFaction2Points\": 0b,");
        L("  \"DialogMail\": {\"Sender\":\"\",\"BeenRead\":0b,\"Message\":{},\"MailItems\":[],\"MailQuest\":-1,\"TimePast\":0L,\"Time\":0L,\"Subject\":\"\"}");
        L("}");
        return sb.ToString();
    }

    public static string NewQuestTemplate(string questType, string title, string text, string completer,
        string objectiveName, int objectiveCount, int dialogId)
    {
        questType = questType.Trim().ToLowerInvariant();
        var type = 0;
        var completion = 0;
        var objectiveLine = "";
        var extraLine = "";
        switch (questType)
        {
            case "talk":
            case "dialog":
                type = 1;
                completion = 1;
                objectiveLine = "  \"QuestDialogs\": [{\"Integer\": " + dialogId + ", \"Slot\": 0}],";
                break;
            case "kill":
                type = 2;
                objectiveName = string.IsNullOrWhiteSpace(objectiveName) ? "Mob" : objectiveName;
                objectiveLine = "  \"QuestDialogs\": [{\"Value\": " + Math.Max(1, objectiveCount) + ", \"Slot\": " + JsonSerializer.Serialize(objectiveName) + "}],";
                break;
            case "item":
                type = 0;
                objectiveName = string.IsNullOrWhiteSpace(objectiveName) ? "minecraft:stone" : objectiveName;
                objectiveLine = "  \"Items\": {\"NpcMiscInv\": [{\"Slot\": 0b, \"id\": " + JsonSerializer.Serialize(objectiveName) + ", \"Count\": " + Math.Clamp(objectiveCount, 1, 64) + "b}]},";
                extraLine = "  \"LeaveItems\": 0b,";
                break;
            default:
                throw new ArgumentException("quest_type must be talk, kill, or item.");
        }

        var sb = new StringBuilder();
        void L(string line) => sb.AppendLine(line);
        L("{");
        L("  \"CompleterNpc\": " + JsonSerializer.Serialize(completer) + ",");
        L("  \"NextQuestId\": -1,");
        L("  \"RandomReward\": 0b,");
        L("  \"QuestRepeat\": 0,");
        L($"  \"QuestCompletion\": {completion},");
        L("  \"IgnoreNBT\": 0b,");
        L("  \"Title\": " + JsonSerializer.Serialize(title) + ",");
        L("  \"Text\": " + JsonSerializer.Serialize(text) + ",");
        L("  \"QuestFactionPoints\": {\"DecreaseFaction1Points\":0b,\"OptionFaction2Points\":100,\"OptionFactions1\":-1,\"OptionFactions2\":-1,\"OptionFaction1Points\":100,\"DecreaseFaction2Points\":0b},");
        L("  \"RewardExp\": 0,");
        L("  \"QuestCommand\": \"\",");
        L(objectiveLine);
        L("  \"ModRev\": 18,");
        L($"  \"Type\": {type},");
        L("  \"QuestMail\": {\"Sender\":\"\",\"BeenRead\":0b,\"Message\":{},\"MailItems\":[],\"MailQuest\":-1,\"TimePast\":0L,\"Time\":0L,\"Subject\":\"\"},");
        L("  \"IgnoreDamage\": 0b,");
        L("  \"Rewards\": {\"NpcMiscInv\": []},");
        if (!string.IsNullOrEmpty(extraLine)) L(extraLine);
        L("  \"CompleteText\": \"Quest complete.\"");
        L("}");
        return sb.ToString();
    }
}