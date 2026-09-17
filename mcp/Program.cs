using System.ComponentModel;
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
            if (args[i] == "--project" && i + 1 < args.Length)
                Root = args[++i];
            else if (args[i] == "--allow-write")
                AllowWrite = true;
        }

        if (!string.IsNullOrWhiteSpace(Root))
            Root = Path.GetFullPath(Root);
    }

    public static string ResolveRoot(string? supplied)
    {
        var root = string.IsNullOrWhiteSpace(supplied) ? Root : supplied;
        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException("No CustomNPCs project path is configured. Pass --project <customnpcs folder> or provide project_path to the tool.");

        root = Path.GetFullPath(root);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(root);
        if (!Directory.Exists(Path.Combine(root, "dialogs")))
            throw new InvalidOperationException($"{root} does not contain a dialogs folder and does not look like a CustomNPCs project.");
        return root;
    }

    public static void RequireWrite()
    {
        if (!AllowWrite)
            throw new InvalidOperationException("This MCP bridge is read-only. Launch it with --allow-write or YDE_MCP_ALLOW_WRITE=1 to enable mutations.");
    }
}

[McpServerToolType]
public static class YdeTools
{
    [McpServerTool, Description("Returns a compact CustomNPCs project summary including dialog and quest categories, counts, IDs, and validation warnings.")]
    public static string ProjectSummary(string? project_path = null)
    {
        var root = ProjectContext.ResolveRoot(project_path);
        return CnpcProject.ToJson(CnpcProject.BuildSummary(root));
    }

    [McpServerTool, Description("Lists dialog records with category, ID, title, start-quest and reply targets.")]
    public static string ListDialogs(string? project_path = null, string? category = null)
    {
        var root = ProjectContext.ResolveRoot(project_path);
        return CnpcProject.ToJson(CnpcProject.Dialogs(root, category).Select(CnpcProject.DialogSummary));
    }

    [McpServerTool, Description("Returns one dialog including full text, replies, availability references, raw native text and YDEC visual position when available.")]
    public static string GetDialog(int dialog_id, string? project_path = null)
    {
        var root = ProjectContext.ResolveRoot(project_path);
        var file = CnpcProject.FindNumericFile(Path.Combine(root, "dialogs"), dialog_id)
            ?? throw new FileNotFoundException($"Dialog {dialog_id} was not found.");
        return CnpcProject.ToJson(CnpcProject.DialogDetail(root, file));
    }

    [McpServerTool, Description("Returns the dialog graph for a category: nodes, full titles/text, reply edges, quest gates, and saved YDEC X/Y positions for visual context.")]
    public static string GetDialogGraph(string category, string? project_path = null)
    {
        var root = ProjectContext.ResolveRoot(project_path);
        return CnpcProject.ToJson(CnpcProject.DialogGraph(root, category));
    }

    [McpServerTool, Description("Lists quest records with category, ID, title, type, completer NPC and objective summary.")]
    public static string ListQuests(string? project_path = null, string? category = null)
    {
        var root = ProjectContext.ResolveRoot(project_path);
        return CnpcProject.ToJson(CnpcProject.Quests(root, category).Select(CnpcProject.QuestSummary));
    }

    [McpServerTool, Description("Returns one quest including full native content and parsed basic fields/objective summary.")]
    public static string GetQuest(int quest_id, string? project_path = null)
    {
        var root = ProjectContext.ResolveRoot(project_path);
        var file = CnpcProject.FindNumericFile(Path.Combine(root, "quests"), quest_id)
            ?? throw new FileNotFoundException($"Quest {quest_id} was not found.");
        return CnpcProject.ToJson(CnpcProject.QuestDetail(file));
    }

    [McpServerTool, Description("Checks dangling dialog links, missing quest references, duplicate IDs, talk-quest dialog references, and highest_index consistency.")]
    public static string ValidateProject(string? project_path = null)
    {
        var root = ProjectContext.ResolveRoot(project_path);
        return CnpcProject.ToJson(CnpcProject.Validate(root));
    }

    [McpServerTool, Description("Creates a dialog category. Requires bridge write mode.")]
    public static string CreateDialogCategory(string name, string? project_path = null)
    {
        ProjectContext.RequireWrite();
        var root = ProjectContext.ResolveRoot(project_path);
        var safe = CnpcProject.SafeName(name);
        var path = Path.Combine(root, "dialogs", safe);
        Directory.CreateDirectory(path);
        return CnpcProject.ToJson(new { created = path });
    }

    [McpServerTool, Description("Creates a quest category. Requires bridge write mode.")]
    public static string CreateQuestCategory(string name, string? project_path = null)
    {
        ProjectContext.RequireWrite();
        var root = ProjectContext.ResolveRoot(project_path);
        var safe = CnpcProject.SafeName(name);
        var path = Path.Combine(root, "quests", safe);
        Directory.CreateDirectory(path);
        return CnpcProject.ToJson(new { created = path });
    }

    [McpServerTool, Description("Creates a new native GBPort-style dialog and advances dialogs/highest_index.json. Requires bridge write mode.")]
    public static string CreateDialog(string category, string title, string text, int start_quest = -1, string? project_path = null)
    {
        ProjectContext.RequireWrite();
        var root = ProjectContext.ResolveRoot(project_path);
        var id = CnpcProject.NextNumericId(Path.Combine(root, "dialogs"));
        var dir = Path.Combine(root, "dialogs", CnpcProject.SafeName(category));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{id}.json");
        File.WriteAllText(path, CnpcProject.NewDialogTemplate(id, title, text, start_quest));
        File.WriteAllText(Path.Combine(root, "dialogs", "highest_index.json"), id.ToString());
        return CnpcProject.ToJson(CnpcProject.DialogDetail(root, path));
    }

    [McpServerTool, Description("Adds a reply option to a dialog. target_dialog_id=-1 creates a terminal reply. Requires bridge write mode.")]
    public static string AddDialogReply(int dialog_id, string reply_title, int target_dialog_id, string? project_path = null)
    {
        ProjectContext.RequireWrite();
        var root = ProjectContext.ResolveRoot(project_path);
        var file = CnpcProject.FindNumericFile(Path.Combine(root, "dialogs"), dialog_id)
            ?? throw new FileNotFoundException($"Dialog {dialog_id} was not found.");
        var text = File.ReadAllText(file);
        text = CnpcProject.AddReply(text, reply_title, target_dialog_id);
        File.WriteAllText(file, text);
        return CnpcProject.ToJson(CnpcProject.DialogDetail(root, file));
    }

    [McpServerTool, Description("Updates a dialog title and/or text without rebuilding unknown native fields. Null values leave fields unchanged. Requires bridge write mode.")]
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
        return CnpcProject.ToJson(CnpcProject.DialogDetail(root, file));
    }

    [McpServerTool, Description("Creates a quest using one of the verified GBPort templates: talk, kill, or item. Objective fields are interpreted by template type. Requires bridge write mode.")]
    public static string CreateQuest(
        string category,
        string quest_type,
        string title,
        string text,
        string completer_npc = "",
        string objective_name = "",
        int objective_count = 1,
        int dialog_id = -1,
        string? project_path = null)
    {
        ProjectContext.RequireWrite();
        var root = ProjectContext.ResolveRoot(project_path);
        Directory.CreateDirectory(Path.Combine(root, "quests"));
        var id = CnpcProject.NextNumericId(Path.Combine(root, "quests"));
        var dir = Path.Combine(root, "quests", CnpcProject.SafeName(category));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{id}.json");
        File.WriteAllText(path, CnpcProject.NewQuestTemplate(quest_type, title, text, completer_npc, objective_name, objective_count, dialog_id));
        return CnpcProject.ToJson(CnpcProject.QuestDetail(path));
    }

    [McpServerTool, Description("Safely updates common quest scalar fields while preserving the rest of the native record. Requires bridge write mode.")]
    public static string UpdateQuest(
        int quest_id,
        string? title = null,
        string? text = null,
        string? complete_text = null,
        string? completer_npc = null,
        int? next_quest_id = null,
        int? reward_exp = null,
        string? project_path = null)
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
        return CnpcProject.ToJson(CnpcProject.QuestDetail(file));
    }
}

internal static class CnpcProject
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string ToJson(object? value) => JsonSerializer.Serialize(value, JsonOptions);

    public static string SafeName(string value)
    {
        value = value.Trim();
        if (string.IsNullOrWhiteSpace(value) || value is "." or ".." || value.Contains('/') || value.Contains('\\'))
            throw new ArgumentException("Category name is invalid.");
        foreach (var c in Path.GetInvalidFileNameChars())
            if (value.Contains(c)) throw new ArgumentException("Category name contains an invalid filename character.");
        return value;
    }

    public static IEnumerable<string> NumericJsonFiles(string root)
    {
        if (!Directory.Exists(root)) yield break;
        foreach (var file in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
            if (int.TryParse(Path.GetFileNameWithoutExtension(file), out _))
                yield return file;
    }

    public static string? FindNumericFile(string root, int id) =>
        NumericJsonFiles(root).FirstOrDefault(f => Path.GetFileNameWithoutExtension(f) == id.ToString());

    public static int NextNumericId(string root)
    {
        var ids = NumericJsonFiles(root)
            .Select(f => int.TryParse(Path.GetFileNameWithoutExtension(f), out var id) ? id : -1)
            .Where(x => x >= 0)
            .ToArray();
        return ids.Length == 0 ? 1 : ids.Max() + 1;
    }

    public static IEnumerable<string> Dialogs(string root, string? category = null)
    {
        var baseDir = Path.Combine(root, "dialogs");
        if (!string.IsNullOrWhiteSpace(category))
            baseDir = Path.Combine(baseDir, SafeName(category));
        return NumericJsonFiles(baseDir).OrderBy(f => ParseFileId(f));
    }

    public static IEnumerable<string> Quests(string root, string? category = null)
    {
        var baseDir = Path.Combine(root, "quests");
        if (!string.IsNullOrWhiteSpace(category))
            baseDir = Path.Combine(baseDir, SafeName(category));
        return NumericJsonFiles(baseDir).OrderBy(f => ParseFileId(f));
    }

    public static int ParseFileId(string file) => int.Parse(Path.GetFileNameWithoutExtension(file));

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
            category = CategoryOf(FindAncestor(file, "dialogs"), file),
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
        var layout = ReadYdecLayout(root, category).TryGetValue(id, out var pos) ? pos : null;
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
            visual_position = layout,
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
            category = CategoryOf(FindAncestor(file, "quests"), file),
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
            category = CategoryOf(FindAncestor(file, "quests"), file),
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
            var m = Regex.Match(native, @"\"Integer\"\s*:\s*(?<id>-?\d+)");
            return new { kind = "talk", dialog_id = m.Success ? int.Parse(m.Groups["id"].Value) : -1 };
        }
        if (type == 2)
        {
            var m = Regex.Match(native, @"\"Value\"\s*:\s*(?<count>\d+).*?\"Slot\"\s*:\s*\"(?<name>(?:\\.|[^\"])*)\"", RegexOptions.Singleline);
            return new { kind = "kill", count = m.Success ? int.Parse(m.Groups["count"].Value) : 0, entity = m.Success ? JsonString(m.Groups["name"].Value) : "" };
        }
        if (type == 0)
        {
            var id = Regex.Match(native, @"\"id\"\s*:\s*\"(?<id>[^\"]+)\"");
            var count = Regex.Match(native, @"\"Count\"\s*:\s*(?<count>\d+)b?");
            return new { kind = "item", item = id.Success ? id.Groups["id"].Value : "", count = count.Success ? int.Parse(count.Groups["count"].Value) : 0 };
        }
        return new { kind = "unknown", type };
    }

    public static object DialogGraph(string root, string category)
    {
        var files = Dialogs(root, category).ToArray();
        var layout = ReadYdecLayout(root, category);
        var nodes = files.Select(file =>
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
                if (!e.TryGetProperty("node_type", out var type) || type.GetString() != "Dialog Node") continue;
                if (!e.TryGetProperty("dialog_id", out var idValue)) continue;
                var id = idValue.GetInt32();
                var x = e.TryGetProperty("position_offset.x", out var xv) ? xv.GetDouble() : 0;
                var y = e.TryGetProperty("position_offset.y", out var yv) ? yv.GetDouble() : 0;
                result[id] = new { x, y, source = "ydec" };
            }
            catch { }
        }
        return result;
    }

    public static object BuildSummary(string root)
    {
        var dialogs = Dialogs(root).ToArray();
        var quests = Quests(root).ToArray();
        var dialogRoot = Path.Combine(root, "dialogs");
        var questRoot = Path.Combine(root, "quests");
        return new
        {
            project_root = root,
            write_enabled = ProjectContext.AllowWrite,
            dialog_count = dialogs.Length,
            quest_count = quests.Length,
            highest_dialog_id = dialogs.Length == 0 ? 0 : dialogs.Max(ParseFileId),
            highest_quest_id = quests.Length == 0 ? 0 : quests.Max(ParseFileId),
            dialog_categories = Directories(dialogRoot),
            quest_categories = Directories(questRoot),
            validation = Validate(root)
        };
    }

    public static object Validate(string root)
    {
        var dialogFiles = Dialogs(root).ToArray();
        var questFiles = Quests(root).ToArray();
        var dialogIds = dialogFiles.Select(ParseFileId).ToHashSet();
        var questIds = questFiles.Select(ParseFileId).ToHashSet();
        var errors = new List<string>();
        var warnings = new List<string>();

        foreach (var dup in dialogFiles.GroupBy(ParseFileId).Where(g => g.Count() > 1))
            errors.Add($"Dialog ID {dup.Key} exists in multiple categories: {string.Join(", ", dup)}");
        foreach (var dup in questFiles.GroupBy(ParseFileId).Where(g => g.Count() > 1))
            errors.Add($"Quest ID {dup.Key} exists in multiple categories: {string.Join(", ", dup)}");

        foreach (var file in dialogFiles)
        {
            var native = File.ReadAllText(file);
            var id = ParseFileId(file);
            foreach (var reply in ParseReplies(native))
            {
                var target = (int)reply.GetType().GetProperty("target_dialog_id")!.GetValue(reply)!;
                if (target >= 0 && !dialogIds.Contains(target)) errors.Add($"D{id} links to missing D{target}.");
            }

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
            if (GetInt(native, "Type", -1) == 1)
            {
                foreach (Match m in Regex.Matches(native, @"\"Integer\"\s*:\s*(?<id>-?\d+)"))
                {
                    var dialog = int.Parse(m.Groups["id"].Value);
                    if (dialog >= 0 && !dialogIds.Contains(dialog)) errors.Add($"Q{id} talk objective references missing D{dialog}.");
                }
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

    public static string[] Directories(string root)
    {
        if (!Directory.Exists(root)) return Array.Empty<string>();
        return Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(x => x is not null)
            .Cast<string>()
            .OrderBy(x => x)
            .ToArray();
    }

    public static string FindAncestor(string file, string folderName)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(file)!);
        while (dir.Parent is not null)
        {
            if (dir.Name.Equals(folderName, StringComparison.OrdinalIgnoreCase)) return dir.FullName;
            dir = dir.Parent;
        }
        return dir.FullName;
    }

    public static string GetString(string native, string key, string fallback = "")
    {
        var m = Regex.Match(native, $"\\\"{Regex.Escape(key)}\\\"\\s*:\\s*\\\"(?<v>(?:\\\\.|[^\\\"])*)\\\"");
        return m.Success ? JsonString(m.Groups["v"].Value) : fallback;
    }

    public static string JsonString(string escaped)
    {
        try { return JsonSerializer.Deserialize<string>($"\"{escaped}\"") ?? ""; }
        catch { return escaped.Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\\\", "\\"); }
    }

    public static int GetInt(string native, string key, int fallback = 0)
    {
        var m = Regex.Match(native, $"\\\"{Regex.Escape(key)}\\\"\\s*:\\s*(?<v>-?\\d+)(?:[bBsSlLfFdD])?");
        return m.Success && int.TryParse(m.Groups["v"].Value, out var value) ? value : fallback;
    }

    public static string ReplaceStringField(string native, string key, string value)
    {
        var rx = new Regex($"\\\"{Regex.Escape(key)}\\\"\\s*:\\s*\\\"(?:\\\\.|[^\\\"])*\\\"");
        var m = rx.Match(native);
        if (!m.Success) return native;
        var replacement = $"\"{key}\": {JsonSerializer.Serialize(value)}";
        return native[..m.Index] + replacement + native[(m.Index + m.Length)..];
    }

    public static string ReplaceIntField(string native, string key, int value)
    {
        var rx = new Regex($"\\\"{Regex.Escape(key)}\\\"\\s*:\\s*-?\\d+(?:[bBsSlLfFdD])?");
        var m = rx.Match(native);
        if (!m.Success) return native;
        var old = m.Value;
        var suffixMatch = Regex.Match(old, @"[bBsSlLfFdD]$");
        var suffix = suffixMatch.Success ? suffixMatch.Value : "";
        var replacement = $"\"{key}\": {value}{suffix}";
        return native[..m.Index] + replacement + native[(m.Index + m.Length)..];
    }

    public static object[] ParseReplies(string native)
    {
        var block = ExtractArray(native, "Options");
        if (block is null) return Array.Empty<object>();
        var results = new List<object>();
        foreach (var obj in SplitTopLevelObjects(block))
        {
            var slot = GetInt(obj, "OptionSlot", results.Count);
            var target = GetInt(obj, "Dialog", -1);
            var title = GetString(obj, "Title");
            var type = GetInt(obj, "OptionType", target < 0 ? 0 : 1);
            results.Add(new { slot, title, target_dialog_id = target, option_type = type });
        }
        return results.ToArray();
    }

    public static string? ExtractArray(string native, string key)
    {
        var keyPos = native.IndexOf($"\"{key}\"", StringComparison.Ordinal);
        if (keyPos < 0) return null;
        var start = native.IndexOf('[', keyPos);
        if (start < 0) return null;
        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var i = start; i < native.Length; i++)
        {
            var c = native[i];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"') { inString = true; continue; }
            if (c == '[') depth++;
            else if (c == ']')
            {
                depth--;
                if (depth == 0) return native[(start + 1)..i];
            }
        }
        return null;
    }

    public static IEnumerable<string> SplitTopLevelObjects(string arrayBody)
    {
        var results = new List<string>();
        var depth = 0;
        var start = -1;
        var inString = false;
        var escaped = false;
        for (var i = 0; i < arrayBody.Length; i++)
        {
            var c = arrayBody[i];
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
                results.Add(arrayBody[start..(i + 1)]);
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
        if (open < 0) throw new InvalidOperationException("Dialog Options array is malformed.");
        var close = FindMatchingBracket(native, open, '[', ']');
        if (close < 0) throw new InvalidOperationException("Dialog Options array is malformed.");
        var body = native[(open + 1)..close];
        var slots = Regex.Matches(body, @"\"OptionSlot\"\s*:\s*(?<slot>\d+)").Select(m => int.Parse(m.Groups["slot"].Value)).ToArray();
        var slot = slots.Length == 0 ? 0 : slots.Max() + 1;
        var optionType = targetDialogId < 0 ? 0 : 1;
        var entry = $"{{\"OptionSlot\":{slot},\"Option\":{{\"DialogCommand\":\"\",\"Dialog\":{targetDialogId},\"Title\":{JsonSerializer.Serialize(replyTitle)},\"DialogColor\":16777215,\"OptionType\":{optionType}}}}}";
        var insertion = string.IsNullOrWhiteSpace(body) ? "\n    " + entry + "\n  " : body.TrimEnd() + ",\n    " + entry + "\n  ";
        return native[..(open + 1)] + insertion + native[close..];
    }

    private static int FindMatchingBracket(string text, int start, char openChar, char closeChar)
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
        string S(string x) => JsonSerializer.Serialize(x);
        return $$"""
{
  "DialogShowWheel": 0b,
  "AvailabilityQuestId": -1,
  "Options": [],
  "AvailabilityScoreboardType": 1,
  "DialogHideNPC": 0b,
  "AvailabilityFactionStance": 0,
  "AvailabilityScoreboard2Value": 0,
  "DialogId": {{id}},
  "AvailabilityQuest": 0,
  "AvailabilityDialog4": 0,
  "AvailabilityScoreboardObjective": "",
  "AvailabilityDialog3": 0,
  "AvailabilityQuest2": 0,
  "AvailabilityQuest3": 0,
  "AvailabilityScoreboard2Objective": "",
  "AvailabilityQuest4": 0,
  "ModRev": 18,
  "DecreaseFaction1Points": 0b,
  "DialogQuest": {{startQuest}},
  "AvailabilityDialog2": 0,
  "OptionFactions1": -1,
  "AvailabilityDayTime": 0,
  "OptionFactions2": -1,
  "AvailabilityFaction2Id": -1,
  "OptionFaction1Points": 0,
  "AvailabilityScoreboardValue": 0,
  "DialogDisableEsc": 1b,
  "AvailabilityFaction": 0,
  "DialogTitle": {{S(title)}},
  "AvailabilityDialog": 0,
  "AvailabilityScoreboard2Type": 1,
  "AvailabilityFaction2": 0,
  "AvailabilityFactionId": -1,
  "AvailabilityFaction2Stance": 0,
  "DialogCommand": "",
  "AvailabilityDialogId": -1,
  "OptionFaction2Points": 0,
  "DialogText": {{S(text)}},
  "AvailabilityQuest4Id": -1,
  "AvailabilityQuest3Id": -1,
  "AvailabilityQuest2Id": -1,
  "AvailabilityDialog2Id": -1,
  "AvailabilityDialog3Id": -1,
  "AvailabilityDialog4Id": -1,
  "AvailabilityMinPlayerLevel": 0,
  "DecreaseFaction2Points": 0b,
  "DialogMail": {
    "Sender": "",
    "BeenRead": 0b,
    "Message": {},
    "MailItems": [],
    "MailQuest": -1,
    "TimePast": 0L,
    "Time": 0L,
    "Subject": ""
  }
}
""";
    }

    public static string NewQuestTemplate(string questType, string title, string text, string completer, string objectiveName, int objectiveCount, int dialogId)
    {
        questType = questType.Trim().ToLowerInvariant();
        string S(string x) => JsonSerializer.Serialize(x);
        string objective;
        int type;
        int completion = 0;
        string extra = "";
        switch (questType)
        {
            case "talk":
            case "dialog":
                type = 1;
                completion = 1;
                objective = $$"\"QuestDialogs\": [{\"Integer\": {{dialogId}}, \"Slot\": 0}],";
                break;
            case "kill":
                type = 2;
                objectiveName = string.IsNullOrWhiteSpace(objectiveName) ? "Mob" : objectiveName;
                objective = $$"\"QuestDialogs\": [{\"Value\": {{Math.Max(1, objectiveCount)}}, \"Slot\": {{S(objectiveName)}}}],";
                break;
            case "item":
                type = 0;
                objectiveName = string.IsNullOrWhiteSpace(objectiveName) ? "minecraft:stone" : objectiveName;
                objective = $$"\"Items\": {\"NpcMiscInv\": [{\"Slot\": 0b, \"id\": {{S(objectiveName)}}, \"Count\": {{Math.Clamp(objectiveCount, 1, 64)}}b}]},";
                extra = "  \"LeaveItems\": 0b,\n";
                break;
            default:
                throw new ArgumentException("quest_type must be talk, kill, or item.");
        }

        return $$"""
{
  "CompleterNpc": {{S(completer)}},
  "NextQuestId": -1,
  "RandomReward": 0b,
  "QuestRepeat": 0,
  "QuestCompletion": {{completion}},
  "IgnoreNBT": 0b,
  "Title": {{S(title)}},
  "Text": {{S(text)}},
  "QuestFactionPoints": {
    "DecreaseFaction1Points": 0b,
    "OptionFaction2Points": 100,
    "OptionFactions1": -1,
    "OptionFactions2": -1,
    "OptionFaction1Points": 100,
    "DecreaseFaction2Points": 0b
  },
  "RewardExp": 0,
  "QuestCommand": "",
  {{objective}}
  "ModRev": 18,
  "Type": {{type}},
  "QuestMail": {
    "Sender": "",
    "BeenRead": 0b,
    "Message": {},
    "MailItems": [],
    "MailQuest": -1,
    "TimePast": 0L,
    "Time": 0L,
    "Subject": ""
  },
  "IgnoreDamage": 0b,
  "Rewards": {"NpcMiscInv": []},
{{extra}}  "CompleteText": "Quest complete."
}
""";
    }
}
