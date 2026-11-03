using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Avalonia.Threading;
using yopet.Sdk;

namespace AgentHooksPlugin;

public class OpencodeHookProvider : JsonlHookProviderBase
{
    public override string Id => "opencode";
    public override string Name => "Opencode";
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
    private static string ReplyFile => Path.Combine(ConfigRoot, "yopet-hooks-replies.jsonl");

    protected override string LogFilePath => Path.Combine(ConfigRoot, "yopet-hooks.jsonl");

    private readonly ConcurrentDictionary<string, string> _pending = new(StringComparer.Ordinal);
    private readonly object _replyLock = new();

    private sealed record OpencodeQuestion(
        string Header,
        string Text,
        IReadOnlyList<OpencodeOption> Options,
        bool Multiple,
        bool Custom);

    private sealed record OpencodeOption(string Label, string Description);

    protected override void OnStarted(IPluginHost host)
    {
        try
        {
            Directory.CreateDirectory(PluginDir);

            if (File.Exists(PluginFile))
            {
                var existing = File.ReadAllText(PluginFile, Encoding.UTF8);
                if (existing == PluginScriptContent)
                    return;
            }

            File.WriteAllText(PluginFile, PluginScriptContent, new UTF8Encoding(false));
            Logger?.Info("[OpencodeHook] 已安装 opencode 插件: yopet-hooks.js");
        }
        catch (Exception ex)
        {
            Logger?.Warn($"[OpencodeHook] 安装 opencode 插件失败: {ex.Message}");
        }
    }

    public override void Stop()
    {
        foreach (var pair in _pending)
            WriteReply(pair.Key, pair.Value, "reject");

        _pending.Clear();
        base.Stop();
    }

    protected override bool HandleInteractiveEvent(
        IPluginHost host,
        string eventId,
        string project,
        string extra)
    {
        if (eventId is not ("permission_asked" or "question_asked"))
            return false;

        if (!_isRunning)
            return true;

        switch (eventId)
        {
            case "permission_asked":
                _ = HandlePermissionAsync(host, project, extra);
                break;
            case "question_asked":
                _ = HandleQuestionAsync(host, project, extra);
                break;
        }

        return true;
    }

    private async Task HandlePermissionAsync(IPluginHost host, string project, string extra)
    {
        string? requestId = null;

        try
        {
            using var document = JsonDocument.Parse(extra);
            var root = document.RootElement;
            requestId = GetString(root, "id");
            if (string.IsNullOrWhiteSpace(requestId))
                return;

            _pending[requestId] = "permission";

            var allowOnce = T("OpencodeAllowOnce");
            var allowAlways = T("OpencodeAllowAlways");
            var reject = T("OpencodeReject");

            var prompt = new PromptDialogConfig
            {
                Title = T("OpencodePermissionTitle"),
                Description = BuildPermissionDetail(root),
                ConfirmLabel = T("OpencodeConfirm"),
                CancelLabel = reject,
                ValidationMessage = T("OpencodeRequired"),
                Options =
                [
                    new PromptOption
                    {
                        Label = allowOnce,
                        Description = T("OpencodeAllowOnceDesc"),
                        Kind = PromptOptionKind.Primary,
                    },
                    new PromptOption
                    {
                        Label = allowAlways,
                        Description = T("OpencodeAllowAlwaysDesc"),
                    },
                    new PromptOption
                    {
                        Label = reject,
                        Description = T("OpencodeRejectDesc"),
                        Kind = PromptOptionKind.Danger,
                    },
                ],
            };

            Logger?.Info($"[OpencodeHook] permission prompt {project} {requestId}");
            var selected = await ShowOnUiThreadAsync(host, prompt);
            if (!_isRunning)
                return;

            var reply = selected is { Count: > 0 } && selected[0] == allowAlways
                ? "always"
                : selected is { Count: > 0 } && selected[0] == allowOnce
                    ? "once"
                    : "reject";

            WriteReply(requestId, "permission", reply == "reject" ? "reject" : "reply", reply);
        }
        catch (Exception ex)
        {
            Logger?.Warn($"[OpencodeHook] permission prompt failed: {ex.Message}");
            if (requestId != null)
                WriteReply(requestId, "permission", "reject");
        }
        finally
        {
            if (requestId != null)
                _pending.TryRemove(requestId, out _);
        }
    }

    private async Task HandleQuestionAsync(IPluginHost host, string project, string extra)
    {
        string? requestId = null;

        try
        {
            using var document = JsonDocument.Parse(extra);
            var root = document.RootElement;
            requestId = GetString(root, "id");
            if (string.IsNullOrWhiteSpace(requestId))
                return;

            var questions = ParseQuestions(root);
            if (questions.Count == 0)
            {
                WriteReply(requestId, "question", "reject");
                return;
            }

            _pending[requestId] = "question";
            var answers = new List<List<string>>();

            Logger?.Info($"[OpencodeHook] question prompt {project} {requestId} ({questions.Count})");

            foreach (var question in questions)
            {
                var prompt = new PromptDialogConfig
                {
                    Title = string.IsNullOrWhiteSpace(question.Header)
                        ? T("OpencodeQuestionTitle")
                        : question.Header,
                    Description = question.Text,
                    Options = question.Options
                        .Select(option => new PromptOption
                        {
                            Label = option.Label,
                            Description = option.Description,
                        })
                        .ToList(),
                    AllowMultiple = question.Multiple,
                    AllowCustom = question.Custom,
                    CustomLabel = T("OpencodeCustomLabel"),
                    CustomPlaceholder = T("OpencodeCustomPlaceholder"),
                    ConfirmLabel = T("OpencodeConfirm"),
                    CancelLabel = T("OpencodeReject"),
                    ValidationMessage = T("OpencodeRequired"),
                };

                var selected = await ShowOnUiThreadAsync(host, prompt);
                if (!_isRunning)
                    return;

                if (selected is not { Count: > 0 })
                {
                    WriteReply(requestId, "question", "reject");
                    return;
                }

                answers.Add(selected.ToList());
            }

            WriteReply(requestId, "question", "reply", answers: answers);
        }
        catch (Exception ex)
        {
            Logger?.Warn($"[OpencodeHook] question prompt failed: {ex.Message}");
            if (requestId != null)
                WriteReply(requestId, "question", "reject");
        }
        finally
        {
            if (requestId != null)
                _pending.TryRemove(requestId, out _);
        }
    }

    private static async Task<IReadOnlyList<string>?> ShowOnUiThreadAsync(
        IPluginHost host,
        PromptDialogConfig prompt)
    {
        IReadOnlyList<string>? result = null;
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            result = await host.ShowPromptDialog(prompt);
        });
        return result;
    }

    private void WriteReply(
        string requestId,
        string kind,
        string action,
        string? reply = null,
        IReadOnlyList<IReadOnlyList<string>>? answers = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["id"] = requestId,
            ["kind"] = kind,
            ["action"] = action,
        };

        if (!string.IsNullOrEmpty(reply))
            payload["reply"] = reply;
        if (answers != null)
            payload["answers"] = answers;

        var line = JsonSerializer.Serialize(payload);

        lock (_replyLock)
        {
            try
            {
                Directory.CreateDirectory(ConfigRoot);
                File.AppendAllText(ReplyFile, line + "\n", new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Logger?.Warn($"[OpencodeHook] write reply failed: {ex.Message}");
            }
        }
    }

    private static List<OpencodeQuestion> ParseQuestions(JsonElement root)
    {
        var questions = new List<OpencodeQuestion>();

        if (!root.TryGetProperty("questions", out var items) || items.ValueKind != JsonValueKind.Array)
            return questions;

        foreach (var item in items.EnumerateArray())
        {
            var options = new List<OpencodeOption>();
            if (item.TryGetProperty("options", out var optionItems) && optionItems.ValueKind == JsonValueKind.Array)
            {
                foreach (var option in optionItems.EnumerateArray())
                {
                    options.Add(new OpencodeOption(
                        GetString(option, "label"),
                        GetString(option, "description")));
                }
            }

            var multiple = item.TryGetProperty("multiple", out var multipleValue)
                && multipleValue.ValueKind == JsonValueKind.True;
            var custom = !item.TryGetProperty("custom", out var customValue)
                || customValue.ValueKind != JsonValueKind.False;

            questions.Add(new OpencodeQuestion(
                GetString(item, "header"),
                GetString(item, "question"),
                options,
                multiple,
                custom || options.Count == 0));
        }

        return questions;
    }

    private static string BuildPermissionDetail(JsonElement root)
    {
        var parts = new List<string>();
        var permission = GetString(root, "permission");
        if (!string.IsNullOrWhiteSpace(permission))
            parts.Add(permission);

        var patterns = GetStringArray(root, "patterns");
        if (patterns.Count > 0)
            parts.Add(string.Join("\n", patterns));

        var metadataKeys = new[] { "command", "filePath", "path", "url", "description", "title" };
        var metadataShown = false;

        if (root.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in metadataKeys)
            {
                if (!metadata.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String)
                    continue;

                var text = value.GetString();
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                parts.Add(text);
                metadataShown = true;
            }

            if (!metadataShown)
                parts.Add(metadata.GetRawText());
        }

        var detail = string.Join("\n", parts);
        return detail.Length > 1200 ? detail[..1200] + "…" : detail;
    }

    private static string GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static List<string> GetStringArray(JsonElement element, string name)
    {
        var values = new List<string>();
        if (!element.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
            return values;

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var value = item.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    values.Add(value);
            }
        }

        return values;
    }

    private const string PluginScriptContent = """
        // yopet-opencode-hook v3
        // Installed by the yopet AgentHooksPlugin. Writes agent events and reads yopet prompt replies.
        import { appendFile, mkdir, readFile } from "node:fs/promises"
        import { homedir } from "node:os"
        import { join } from "node:path"

        const configRoot = process.env.XDG_CONFIG_HOME
          ? join(process.env.XDG_CONFIG_HOME, "opencode")
          : join(homedir(), ".config", "opencode")
        const logFile = join(configRoot, "yopet-hooks.jsonl")
        const replyFile = join(configRoot, "yopet-hooks-replies.jsonl")

        const clean = (value) => String(value ?? "").replace(/[\t\r\n]+/g, " ").trim()

        async function emit(event, project, extra = "") {
          try {
            await mkdir(configRoot, { recursive: true })
            const line = [Date.now(), event, clean(project), clean(extra)].join("\t") + "\n"
            await appendFile(logFile, line, "utf8")
          } catch {}
        }

        async function emitJson(event, project, payload) {
          await emit(event, project, JSON.stringify(payload ?? {}))
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

        async function readReply(id) {
          try {
            const text = await readFile(replyFile, "utf8")
            for (const line of text.split("\n")) {
              if (!line.trim()) continue
              try {
                const value = JSON.parse(line)
                if (value?.id === id) return value
              } catch {}
            }
          } catch {}
          return null
        }

        async function waitForReply(id, timeoutMs) {
          const started = Date.now()
          while (Date.now() - started < timeoutMs) {
            const reply = await readReply(id)
            if (reply) return reply
            await new Promise((resolve) => setTimeout(resolve, 250))
          }
          return null
        }

        async function postJson(serverUrl, directory, path, body) {
          const headers = { "content-type": "application/json" }
          if (directory) headers["x-opencode-directory"] = encodeURIComponent(directory)
          const response = await fetch(new URL(path, serverUrl), {
            method: "POST",
            headers,
            body: JSON.stringify(body),
          })
          if (!response.ok) throw new Error("HTTP " + response.status)
          return response
        }

        const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms))

        function makeAttempt(attempts, resolved, id) {
          return async (name, fn) => {
            try {
              await fn()
              await delay(350)
              if (resolved.has(id)) {
                attempts.push([name, "ok"])
                return true
              }
              attempts.push([name, "no-event"])
            } catch (error) {
              attempts.push([name, String(error?.message ?? error)])
            }
            return false
          }
        }

        async function replyPermission(client, serverUrl, directory, request, resolved) {
          const reply = request?.reply === "always" ? "always" : request?.reply === "once" ? "once" : "reject"
          const attempts = []
          const tryStep = makeAttempt(attempts, resolved, request.id)

          if (await tryStep("client.v1", async () => {
            if (typeof client.postSessionIdPermissionsPermissionId !== "function") throw new Error("missing")
            await client.postSessionIdPermissionsPermissionId({
              path: { id: request.sessionID, permissionID: request.id },
              body: { response: reply },
            })
          })) return { ok: true, attempts }

          if (serverUrl && request.sessionID && await tryStep("http.v1", () =>
            postJson(serverUrl, directory, `/session/${encodeURIComponent(request.sessionID)}/permissions/${encodeURIComponent(request.id)}`, { response: reply })
          )) return { ok: true, attempts }

          if (serverUrl && request.sessionID && await tryStep("http.v2", () =>
            postJson(serverUrl, directory, `/api/session/${encodeURIComponent(request.sessionID)}/permission/${encodeURIComponent(request.id)}/reply`, { reply })
          )) return { ok: true, attempts }

          if (serverUrl && await tryStep("http.legacy", () =>
            postJson(serverUrl, directory, `/permission/${encodeURIComponent(request.id)}/reply`, { reply })
          )) return { ok: true, attempts }

          if (await tryStep("client.v2", async () => {
            if (typeof client.permission?.reply !== "function") throw new Error("missing")
            await client.permission.reply({ requestID: request.id, reply })
          })) return { ok: true, attempts }

          return { ok: false, attempts }
        }

        async function replyQuestion(client, serverUrl, directory, request, resolved) {
          const answers = request?.answers ?? []
          const attempts = []
          const tryStep = makeAttempt(attempts, resolved, request.id)

          if (serverUrl && request.sessionID && await tryStep("http.v2", () =>
            postJson(serverUrl, directory, `/api/session/${encodeURIComponent(request.sessionID)}/question/${encodeURIComponent(request.id)}/reply`, { answers })
          )) return { ok: true, attempts }

          if (serverUrl && await tryStep("http.v1", () =>
            postJson(serverUrl, directory, `/question/${encodeURIComponent(request.id)}/reply`, { answers })
          )) return { ok: true, attempts }

          if (await tryStep("client.v2", async () => {
            if (typeof client.question?.reply !== "function") throw new Error("missing")
            await client.question.reply({ requestID: request.id, answers })
          })) return { ok: true, attempts }

          return { ok: false, attempts }
        }

        async function rejectQuestion(client, serverUrl, directory, request, resolved) {
          const attempts = []
          const tryStep = makeAttempt(attempts, resolved, request.id)

          if (serverUrl && request.sessionID && await tryStep("http.v2", () =>
            postJson(serverUrl, directory, `/api/session/${encodeURIComponent(request.sessionID)}/question/${encodeURIComponent(request.id)}/reject`, {})
          )) return { ok: true, attempts }

          if (serverUrl && await tryStep("http.v1", () =>
            postJson(serverUrl, directory, `/question/${encodeURIComponent(request.id)}/reject`, {})
          )) return { ok: true, attempts }

          if (await tryStep("client.v2", async () => {
            if (typeof client.question?.reject !== "function") throw new Error("missing")
            await client.question.reject({ requestID: request.id })
          })) return { ok: true, attempts }

          return { ok: false, attempts }
        }

        export const YopetHooks = async ({ client, directory, serverUrl }) => {
          const project = clean(directory ?? "").split(/[\\/]/).filter(Boolean).pop() ?? ""
          void emitJson("yopet_plugin_init", project, {
            serverUrl: String(serverUrl ?? ""),
            directory: clean(directory ?? ""),
            client_v1_permission: typeof client.postSessionIdPermissionsPermissionId,
            client_v2_permission: typeof client.permission?.reply,
            client_v2_question: typeof client.question?.reply,
          })
          const childSessions = new Set()
          const pending = new Set()
          const resolvedPermissions = new Set()
          const resolvedQuestions = new Set()

          async function handlePermission(permission) {
            if (!permission?.id || pending.has("p:" + permission.id)) return
            pending.add("p:" + permission.id)
            try {
              const reply = await waitForReply(permission.id, 30 * 60 * 1000)
              if (!reply) return
              const value = reply.action === "reject" || reply.reply === "reject"
                ? "reject"
                : reply.reply
              const result = await replyPermission(client, serverUrl, directory, { ...permission, reply: value }, resolvedPermissions)
              await emitJson("permission_reply_debug", project, { id: permission.id, reply: value, ...result })
            } finally {
              pending.delete("p:" + permission.id)
            }
          }

          async function handleQuestion(question) {
            if (!question?.id || pending.has("q:" + question.id)) return
            pending.add("q:" + question.id)
            try {
              const reply = await waitForReply(question.id, 30 * 60 * 1000)
              if (!reply) return
              const result = reply.action === "reject"
                ? await rejectQuestion(client, serverUrl, directory, question, resolvedQuestions)
                : await replyQuestion(client, serverUrl, directory, { ...question, answers: reply.answers ?? [] }, resolvedQuestions)
              await emitJson("question_reply_debug", project, { id: question.id, action: reply.action, ...result })
            } finally {
              pending.delete("q:" + question.id)
            }
          }

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
                case "permission.asked":
                case "permission.updated": {
                  const permission = event.properties
                  if (!permission?.id || childSessions.has(permission.sessionID)) break
                  await emitJson("permission_asked", project, permission)
                  void handlePermission(permission)
                  break
                }
                case "permission.v2.asked": {
                  const permission = event.properties
                  if (!permission?.id || childSessions.has(permission.sessionID)) break
                  const normalized = {
                    ...permission,
                    permission: permission.action,
                    patterns: permission.resources ?? [],
                    always: permission.save ?? [],
                  }
                  await emitJson("permission_asked", project, normalized)
                  void handlePermission(normalized)
                  break
                }
                case "question.asked": {
                  const question = event.properties
                  if (!question?.id || childSessions.has(question.sessionID)) break
                  await emitJson("question_asked", project, question)
                  void handleQuestion(question)
                  break
                }
                case "question.v2.asked": {
                  const question = event.properties
                  if (!question?.id || childSessions.has(question.sessionID)) break
                  await emitJson("question_asked", project, question)
                  void handleQuestion(question)
                  break
                }
                case "permission.replied":
                case "permission.v2.replied": {
                  const requestID = event.properties?.requestID
                  if (requestID) resolvedPermissions.add(requestID)
                  await emit("permission_replied", project)
                  break
                }
                case "question.replied":
                case "question.v2.replied": {
                  const requestID = event.properties?.requestID
                  if (requestID) resolvedQuestions.add(requestID)
                  await emit("question_replied", project)
                  break
                }
                case "question.rejected":
                case "question.v2.rejected": {
                  const requestID = event.properties?.requestID
                  if (requestID) resolvedQuestions.add(requestID)
                  await emit("question_rejected", project)
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
