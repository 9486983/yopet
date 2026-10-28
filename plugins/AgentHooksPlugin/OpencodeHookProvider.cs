using System.Text;
using Lang.Avalonia;
using yopet.Sdk;

namespace AgentHooksPlugin;

/// <summary>
/// opencode Hook Provider —— 自动安装 ~/.config/opencode/plugin/yopet-hooks.js，
/// 监听该插件写入的 ~/.config/opencode/yopet-hooks.jsonl。
/// 使用 FileSystemWatcher 即时响应，无需轮询。
/// </summary>
public class OpencodeHookProvider : JsonlHookProviderBase
{
    /// <summary>取当前语言的 Agent Hooks 词条</summary>
    private static string T(string key) =>
        I18nManager.Instance.GetResource($"Localization.AgentHooksPlugin.{key}");

    public override string Id => "opencode";
    public override string Name => "opencode";
    public override string Emoji => "🌀";
    public override string Description => T("OpencodeDesc");
    public override string ConfigPrefix => "opencode_";

    private static string ConfigRoot =>
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg
            ? Path.Combine(xdg, "opencode")
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config", "opencode");

    private static string PluginDir => Path.Combine(ConfigRoot, "plugin");
    private static string PluginFile => Path.Combine(PluginDir, "yopet-hooks.js");

    protected override string LogFilePath => Path.Combine(ConfigRoot, "yopet-hooks.jsonl");

    /// <summary>写入 opencode 插件，使其将事件追加到 yopet-hooks.jsonl</summary>
    protected override void OnStarted(IPluginHost host)
    {
        try
        {
            Directory.CreateDirectory(PluginDir);

            if (File.Exists(PluginFile))
            {
                var existing = File.ReadAllText(PluginFile, Encoding.UTF8);
                if (existing == PluginScriptContent) return;
            }

            File.WriteAllText(PluginFile, PluginScriptContent, new UTF8Encoding(false));
            host.Log("[OpencodeHook] 已安装 opencode 插件: yopet-hooks.js");
        }
        catch (Exception ex)
        {
            host.Log($"[OpencodeHook] 安装 opencode 插件失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 嵌入的 opencode 插件脚本。
    /// 订阅会话、空闲与工具事件，追加到 yopet-hooks.jsonl：
    /// timestamp \t event \t project \t extra
    /// </summary>
    private const string PluginScriptContent = """
        // yopet-opencode-hook v2
        // Installed by the yopet AgentHooksPlugin. Writes agent events to yopet-hooks.jsonl.
        import { appendFile, mkdir } from "node:fs/promises"
        import { homedir } from "node:os"
        import { join } from "node:path"

        const configRoot = process.env.XDG_CONFIG_HOME
          ? join(process.env.XDG_CONFIG_HOME, "opencode")
          : join(homedir(), ".config", "opencode")
        const logFile = join(configRoot, "yopet-hooks.jsonl")

        const clean = (value) => String(value ?? "").replace(/[\t\r\n]+/g, " ").trim()

        async function emit(event, project, extra = "") {
          try {
            await mkdir(configRoot, { recursive: true })
            const line = [Date.now(), event, clean(project), clean(extra)].join("\t") + "\n"
            await appendFile(logFile, line, "utf8")
          } catch {}
        }

        async function lastAssistantText(client, sessionID) {
          if (!sessionID) return ""
          try {
            const result = await client.session.messages({ path: { id: sessionID } })
            const messages = result?.data ?? result
            if (!Array.isArray(messages)) return ""
            for (let i = messages.length - 1; i >= 0; i--) {
              const message = messages[i]
              if (message?.info?.role !== "assistant") continue
              const text = (message.parts ?? [])
                .filter((part) => part?.type === "text" && !part.synthetic && !part.ignored)
                .map((part) => part.text ?? "")
                .join(" ")
                .trim()
              if (text) return text
            }
          } catch {}
          return ""
        }

        export const YopetHooks = async ({ client, directory }) => {
          const project = clean(directory ?? "").split(/[\\/]/).filter(Boolean).pop() ?? ""
          const childSessions = new Set()

          return {
            event: async ({ event }) => {
              switch (event?.type) {
                case "session.created": {
                  const info = event.properties?.info
                  if (!info?.id) break
                  if (info.parentID) childSessions.add(info.id)
                  else await emit("session_start", project)
                  break
                }
                case "session.idle": {
                  const sessionID = event.properties?.sessionID
                  if (!sessionID || childSessions.has(sessionID)) break
                  await emit("stop", project, await lastAssistantText(client, sessionID))
                  break
                }
                case "session.status": {
                  const sessionID = event.properties?.sessionID
                  const status = event.properties?.status?.type
                  if (!sessionID || childSessions.has(sessionID)) break
                  if (status === "busy" || status === "retry") await emit("working", project)
                  break
                }
                case "permission.updated":
                case "permission.asked": {
                  const permission = event.properties
                  if (!permission?.sessionID || childSessions.has(permission.sessionID)) break
                  await emit("notification", project, permission.title ?? "")
                  break
                }
              }
            },

            "tool.execute.after": async (input, output) => {
              const tool = input?.tool ?? ""
              const args = input?.args ?? {}
              if (tool === "bash") {
                await emit("bash", project, args.command ?? output?.title ?? "")
              } else if (["write", "edit", "patch", "multiedit"].includes(tool)) {
                await emit("file", project, args.filePath ?? args.path ?? args.file ?? output?.title ?? "")
              }
            },
          }
        }
        """;
}
