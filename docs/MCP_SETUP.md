# YDE Arvan AI — MCP setup

The installer places the MCP bridge under the YDE install directory at:

`mcp/YdeMcpBridge.exe`

The bridge uses **stdio** and is **read-only by default**.

## Read-only example

```json
{
  "mcpServers": {
    "yde-arvan": {
      "command": "C:\\Path\\To\\YDE Arvan AI\\mcp\\YdeMcpBridge.exe",
      "args": ["--project", "C:\\Path\\To\\world\\customnpcs"]
    }
  }
}
```

## Write-enabled example

Add `--allow-write` only when you want the AI client to be able to create or update dialog/quest files:

```json
{
  "mcpServers": {
    "yde-arvan": {
      "command": "C:\\Path\\To\\YDE Arvan AI\\mcp\\YdeMcpBridge.exe",
      "args": ["--project", "C:\\Path\\To\\world\\customnpcs", "--allow-write"]
    }
  }
}
```

The MCP graph tools read `.ydec` layout data when present, so AI clients can receive the same node X/Y organization used by the editor in addition to native dialog/quest records.

## Main tools

Read tools include `ProjectSummary`, `ListDialogs`, `GetDialog`, `GetDialogGraph`, `ListQuests`, `GetQuest`, and `ValidateProject`.

Write mode adds usable mutations through `CreateDialogCategory`, `CreateQuestCategory`, `CreateDialog`, `AddDialogReply`, `UpdateDialog`, `CreateQuest`, and `UpdateQuest`.

Quest creation intentionally supports the three native shapes verified for the current Arvan/GBPort workflow: **talk/dialog (Type 1), kill (Type 2), and item (Type 0)**. Unknown quest fields remain available through the editor's advanced native view rather than being guessed or discarded.
