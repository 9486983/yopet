# AgentHooksPlugin

更新时间：2026-11-04  
分支：`feature/opencode-agent-hook`

## 当前状态

- yopet 端的 opencode 权限/提问弹窗已经可以显示。
- 用户在 yopet 中点击后，回复会写入：
  `~/.config/opencode/yopet-hooks-replies.jsonl`
- 但 opencode 侧仍会停留在等待选择的状态，回复链路尚未端到端打通。
- 最近一次修复已加入正确的 v1 SDK 调用和多重 HTTP 回退，但尚未在重启后的环境中完成验证。

## 实现结构

1. yopet 启动时，`OpencodeHookProvider` 安装/更新：
   `~/.config/opencode/plugin/yopet-hooks.js`（当前 v3）
2. opencode 插件把事件追加到：
   `~/.config/opencode/yopet-hooks.jsonl`
3. `AgentHooksPlugin` 监听日志，识别：
   - `permission_asked`
   - `question_asked`
4. yopet 使用 `PromptDialog` 显示权限选项、单选/多选问题和自定义输入。
5. 用户选择后，C# 写入：
   `~/.config/opencode/yopet-hooks-replies.jsonl`
6. opencode 插件轮询回复文件，尝试调用 opencode API 完成确认。

## 涉及文件

- `plugins/AgentHooksPlugin/OpencodeHookProvider.cs`
  - 安装 opencode 插件脚本
  - 解析权限/提问事件
  - 写回复文件
  - 记录 `permission_reply_debug` / `question_reply_debug`
- `plugins/AgentHooksPlugin/JsonlHookProviderBase.cs`
  - 新增 `HandleInteractiveEvent` 扩展点
- `yopet.Sdk/UI/PromptDialogConfig.cs`
  - 通用选项/输入弹窗配置
- `yopet.UI/Views/PromptDialog.cs`
  - 基于 `ArcDialogCard` 的权限/提问弹窗
- `yopet.Sdk/Interfaces/IPluginHost.cs`
  - `ShowPromptDialog`
- `yopet.Services/PluginHostImpl.cs`
  - 弹窗回调接线
- `yopet/App.axaml.cs`
  - UI 线程弹窗接线

## 已确认的现场日志

yopet 日志中可以看到：

```text
[OpencodeHook] permission prompt yopet per_...
```

回复文件中能看到：

```json
{"id":"per_...","kind":"permission","action":"reply","reply":"once"}
```

但 opencode 日志中仍出现权限请求等待，随后 TUI 侧仍需要人工处理。说明 yopet 的回复文件已经写出，真正的问题是 opencode 插件没有把回复落到正确的 permission 状态上。

## 当前最可能的原因

1. opencode 插件拿到的 `client` 是 v1 SDK：
   - 有 `client.postSessionIdPermissionsPermissionId(...)`
   - 没有 `client.permission.reply(...)`
   之前的代码优先调用了不存在的 `client.permission.reply`，失败后被静默吞掉。
2. TUI 模式下 `serverUrl` 可能不是可写的 TCP listener，直接 HTTP 回退可能无法命中正确的实例。
3. opencode 较新版本存在 v1/v2 permission 状态存储分离问题，插件通过 SDK 回复可能返回成功但没有真正 resolve。
4. 回答 question 时同样需要优先使用 `/api/session/{sessionID}/question/{requestID}/reply`，旧 `/question/{requestID}/reply` 不一定存在。

## 最近一次未验证修复

`OpencodeHookProvider.cs` 中的 v3 脚本现在按以下顺序尝试权限回复：

1. `client.postSessionIdPermissionsPermissionId({ path, body: { response } })`
2. HTTP `/session/{sessionID}/permissions/{permissionID}`，body `{ response }`
3. HTTP `/api/session/{sessionID}/permission/{requestID}/reply`，body `{ reply }`
4. HTTP `/permission/{requestID}/reply`，body `{ reply }`
5. `client.permission.reply(...)`（如果当前 client 是 v2）

每一步之后等待 `permission.replied` 事件确认；只有收到事件才认为成功。

question 回复按以下顺序尝试：

1. `/api/session/{sessionID}/question/{requestID}/reply`
2. `/question/{requestID}/reply`
3. `client.question.reply(...)`（如果存在）

question 拒绝同理尝试 v2/v1/客户端。

## 调试入口

重启 yopet 后，opencode 插件会写入初始化诊断：

```text
yopet_plugin_init
```

内容包含：

- `serverUrl`
- `directory`
- `client_v1_permission` 是否存在
- `client_v2_permission` 是否存在
- `client_v2_question` 是否存在

每次回复尝试后会写入：

```text
permission_reply_debug
question_reply_debug
```

内容包含每一步的尝试结果，例如：

```json
{"id":"per_...","reply":"once","ok":false,"attempts":[["client.v1","no-event"],["http.v1","HTTP 404"]]}
```

## 明天继续排查的顺序

1. 重启 opencode，确保加载最新 `yopet-hooks.js`（yopet 启动会覆盖安装）。
2. 触发一次 `.env` 读取权限。
3. 依次检查：
   - `~/.config/opencode/yopet-hooks.jsonl` 中的 `yopet_plugin_init`
   - `permission_reply_debug` 的 `attempts`
   - `~/.local/share/opencode/log/opencode.log` 中 `asking id=...` 之后是否出现 `replied`
4. 如果 `client.v1` 返回 `no-event`，但 opencode 日志显示已 replied，则说明 UI store 没同步，需要改用 `permission.ask` hook 或 TUI 控制通道。
5. 如果所有 attempt 都报 `missing` / `HTTP 404`，下一步应优先抓取真实 `serverUrl` 和实际可用的 API 路径。
6. 如果 `client_v1_permission` 为 `undefined`，需要改用 v2 SDK 或直接构造 v2 HTTP 请求。
7. question 功能还要确认当前 agent 是否真的启用了 question 工具。

## 备注

- yopet 会覆盖安装 `~/.config/opencode/plugin/yopet-hooks.js`，所以修改 `OpencodeHookProvider.cs` 后需要重启 yopet，再重启 opencode。
- 当前分支尚未合并到 `master`。
