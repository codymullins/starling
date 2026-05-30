using System.Text.Json;
using System.Text.Json.Nodes;
using Starling.Mcp;

namespace Starling.Gui.Mcp;

/// <summary>
/// MCP tool group that drives the visible Starling browser window. Owns the
/// tool descriptors (the static JSON below is the single source of truth) and
/// dispatches tools/call onto <see cref="IBrowserControlDispatcher"/>.
/// </summary>
public sealed class BrowserTools : IMcpToolGroup
{
    private readonly IBrowserControlDispatcher _browser;

    public BrowserTools(IBrowserControlDispatcher browser)
    {
        _browser = browser ?? throw new ArgumentNullException(nameof(browser));
    }

    public string GetToolDescriptorsJson() => ToolDescriptorsJson;

    public bool HasTool(string name) => name switch
    {
        "browser_navigate"
            or "browser_back"
            or "browser_forward"
            or "browser_refresh"
            or "browser_screenshot"
            or "browser_inspect"
            or "browser_click"
            or "browser_move"
            or "browser_type"
            or "browser_resize" => true,
        _ => false,
    };

    public async Task<McpToolResult> InvokeAsync(string name, JsonElement arguments, CancellationToken ct)
    {
        BrowserControlResult result = name switch
        {
            "browser_navigate" => await _browser.NavigateAsync(
                McpArgumentReader.ReadString(arguments, "url"), ct).ConfigureAwait(false),
            "browser_back" => await _browser.BackAsync(ct).ConfigureAwait(false),
            "browser_forward" => await _browser.ForwardAsync(ct).ConfigureAwait(false),
            "browser_refresh" => await _browser.ReloadAsync(ct).ConfigureAwait(false),
            "browser_screenshot" => await _browser.ScreenshotAsync(
                McpArgumentReader.ReadString(arguments, "path"), ct).ConfigureAwait(false),
            "browser_inspect" => await _browser.InspectAsync(
                McpArgumentReader.ReadBool(arguments, "includeHtml"),
                McpArgumentReader.ReadOptionalString(arguments, "logPath"),
                ct).ConfigureAwait(false),
            "browser_click" => await _browser.ClickAsync(
                McpArgumentReader.ReadDouble(arguments, "x"),
                McpArgumentReader.ReadDouble(arguments, "y"),
                ct).ConfigureAwait(false),
            "browser_move" => await _browser.MoveMouseAsync(
                McpArgumentReader.ReadDouble(arguments, "x"),
                McpArgumentReader.ReadDouble(arguments, "y"),
                ct).ConfigureAwait(false),
            "browser_type" => await _browser.TypeTextAsync(
                McpArgumentReader.ReadString(arguments, "text"),
                McpArgumentReader.ReadBool(arguments, "submit"),
                ct).ConfigureAwait(false),
            "browser_resize" => await _browser.ResizeAsync(
                McpArgumentReader.ReadDouble(arguments, "width"),
                McpArgumentReader.ReadDouble(arguments, "height"),
                ct).ConfigureAwait(false),
            _ => throw new ArgumentException($"Unknown browser tool: {name}", nameof(name)),
        };

        return new McpToolResult(ResultObject(result), IsError: !result.Ok);
    }

    private static JsonObject ResultObject(BrowserControlResult result) => new()
    {
        ["ok"] = result.Ok,
        ["url"] = result.Url,
        ["title"] = result.Title,
        ["canGoBack"] = result.CanGoBack,
        ["canGoForward"] = result.CanGoForward,
        ["isBusy"] = result.IsBusy,
        ["error"] = result.Error,
        ["detail"] = result.Detail,
    };

    // The tool catalogue is fully static; the descriptors live as a JSON
    // literal so the MCP server can splice them into tools/list and re-parse
    // a fresh tree per request (a JsonNode cannot be re-parented, so a
    // pre-built tree could not be reused).
    private const string ToolDescriptorsJson = """
        [
          {
            "name": "browser_navigate",
            "description": "Navigate the visible Starling browser window to a URL.",
            "inputSchema": {
              "type": "object",
              "properties": {
                "url": {
                  "type": "string",
                  "description": "The absolute URL to load, for example https://example.com."
                }
              },
              "required": ["url"]
            }
          },
          {
            "name": "browser_back",
            "description": "Navigate the visible Starling browser window back in history.",
            "inputSchema": { "type": "object", "properties": {} }
          },
          {
            "name": "browser_forward",
            "description": "Navigate the visible Starling browser window forward in history.",
            "inputSchema": { "type": "object", "properties": {} }
          },
          {
            "name": "browser_refresh",
            "description": "Reload the current page in the visible Starling browser window.",
            "inputSchema": { "type": "object", "properties": {} }
          },
          {
            "name": "browser_screenshot",
            "description": "Capture the current page in the visible Starling browser window to a PNG file (full scroll extent). The written path is returned in `detail`.",
            "inputSchema": {
              "type": "object",
              "properties": {
                "path": {
                  "type": "string",
                  "description": "Output PNG path. Relative paths resolve against the GUI's working directory. Defaults to starling-screenshot.png."
                }
              }
            }
          },
          {
            "name": "browser_inspect",
            "description": "Inspect the current page: URL, title, live-scripting state, and recent JS console warnings/errors, returned in `detail`. Optionally include the serialized outerHTML and/or dump a full telemetry+HTML report to a logfile.",
            "inputSchema": {
              "type": "object",
              "properties": {
                "includeHtml": {
                  "type": "boolean",
                  "description": "Include the page's serialized outerHTML in the response (truncated to 100 KB)."
                },
                "logPath": {
                  "type": "string",
                  "description": "If set, write a full report (all telemetry logs + complete outerHTML) to this file path."
                }
              }
            }
          },
          {
            "name": "browser_click",
            "description": "Left-click a point on the current page. Coordinates are page pixels from the document's top-left (same space browser_screenshot captures, full scroll extent). Clicking a text field focuses it (follow with browser_type); a link/button/checkbox is activated. The outcome is returned in `detail`.",
            "inputSchema": {
              "type": "object",
              "properties": {
                "x": { "type": "number", "description": "X coordinate in page pixels from the document's left edge." },
                "y": { "type": "number", "description": "Y coordinate in page pixels from the document's top edge." }
              },
              "required": ["x", "y"]
            }
          },
          {
            "name": "browser_move",
            "description": "Move the mouse to a point on the current page, updating hover/cursor state and dispatching DOM mouseover/mousemove/mouseout so JS hover handlers run. Coordinates are page pixels from the document's top-left (same space as browser_screenshot). What is under the cursor is returned in `detail`.",
            "inputSchema": {
              "type": "object",
              "properties": {
                "x": { "type": "number", "description": "X coordinate in page pixels from the document's left edge." },
                "y": { "type": "number", "description": "Y coordinate in page pixels from the document's top edge." }
              },
              "required": ["x", "y"]
            }
          },
          {
            "name": "browser_type",
            "description": "Type text into the currently focused text field (focus one first with browser_click). Fires a DOM input event so search-as-you-type and form handlers run. Set submit=true to press Enter afterward, submitting the owning form. The field's new value is returned in `detail`.",
            "inputSchema": {
              "type": "object",
              "properties": {
                "text": { "type": "string", "description": "The literal text to type. Control characters are ignored." },
                "submit": { "type": "boolean", "description": "Press Enter after typing to submit the owning form. Defaults to false." }
              },
              "required": ["text"]
            }
          },
          {
            "name": "browser_resize",
            "description": "Resize the visible Starling browser window. Width and height are device-independent pixels (DIPs). The window's min size is honored — smaller requests clamp up. If the window is maximized/fullscreen it is restored first. The page reflows to the new viewport; the applied size is returned in `detail`.",
            "inputSchema": {
              "type": "object",
              "properties": {
                "width": { "type": "number", "description": "Window width in DIPs. Clamped to the window's MinWidth." },
                "height": { "type": "number", "description": "Window height in DIPs. Clamped to the window's MinHeight." }
              },
              "required": ["width", "height"]
            }
          }
        ]
        """;
}
