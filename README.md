# Codex Usage Bar

A lightweight Windows overlay for Codex token usage, context windows, and rate limits.

灵感来自 DeepSeek Harness 的用量状态栏。

适用于 Windows 10 1809 或更高版本、Windows 11 的 x64 版 Codex 桌面客户端。用量胶囊贴在模型选择器和上下文圆环左侧，随背景切换浅色和深色。点击轮次或速度打开「会话统计」，点击 Token 或缓存打开「Token 用量」。胶囊对应区块会高亮。面板默认使用短淡入滑动动画。再次点击同一侧、点击面板外或按 Esc 可收起。窗口变窄时会缩减指标，空间不足时会隐藏。

## 启动

1. 从 [GitHub Releases](https://github.com/xiaoxins8/codex-usage-bar/releases/latest) 下载 Windows x64 EXE 安装包。退出正在运行的旧版后双击安装。向导可自选安装目录，并可选建桌面快捷方式。安装后从开始菜单打开设置窗口。设置窗口使用 Microsoft Edge WebView2 Runtime，界面基于 React + Fluent UI，支持简体中文、繁體中文和 English，跟随 Windows 浅色/深色。
2. 应用设置后切回 Codex，打开一个本地会话。双击 Windows 托盘中的「Codex 用量栏」可再次打开设置。退出可使用托盘右键菜单。

升级时先从托盘退出旧版，再启动新版。同一时间只运行一个用量栏进程。

## 位置和启动设置

- 设置窗口（控制中心）使用自绘标题栏，分「概览 / 外观与位置 / 账号额度 / 通用」四页，左下角显示监听状态和当前会话名。
  - 概览：大号累计 Token 读数，右侧为输出速度和缓存命中，下方刻度尺显示额度用量。再往下是输入区预览和三栏明细（累计 Token、缓存命中、输出速度）。
  - 有会话数据时用实时数据，否则用示例数据并标明「示例数据」。
- 输入区预览按 Codex 输入框的样式和比例绘制，模型名和推理档位取自当前会话。外观与位置页可直接拖动预览中的胶囊，或选中后用方向键微调；也可以选择靠右、居中、靠左，再用滑块或数值框调整水平与垂直偏移。改动先在预览中生效，点「应用设置」或按 Ctrl+S 保存。设置窗口关闭后，用量栏留在托盘。
- 勾选「登录 Windows 时启动」并应用后，用量栏会在当前 Windows 用户登录时后台启动。启用前把 EXE 放在固定位置。移动或升级后，在新版设置里再应用一次即可更新启动路径。
- 详情面板默认淡入滑动。Windows 关闭菜单动画时，面板会直接展开。

## 额度和刷新频率

- 「五小时额度」和「每周额度」默认为关闭，可独立开启。开启后主栏显示剩余百分比，点击额度区查看重置时间和来源。
- 本机会话数据约每 300 毫秒检查一次日志增量。模型的用量到达日志时显示对应值。首条上报前显示「等待用量」。
- 额度开启后使用 Codex App Server 的 `account/rateLimits/read`，默认每 60 秒查询一次。当前已识别会话若写入较新的 Codex 额度快照，也会随日志刷新。查询额度沿用本机 Codex 登录，使用只读请求。缺失的额度显示「—」。
- 用量和窗口定位分别刷新。同一 Codex 窗口临时失去定位时保留最近可用位置，移动同尺寸窗口时沿用锚点。切到其他应用、最小化或没有足够空间时隐藏。

### 会话识别

用量栏通过 Windows 辅助功能读取 Codex 窗口，按顺序确定当前会话，再读取该会话的本机日志：

1. 页面路由里带会话 ID 时直接使用。
2. 页面标题：Codex 把当前会话名写入网页标题（文档名），与本机会话库 `state_*.sqlite` 的名称比对；未命名的会话用其标题比对。不受侧边栏展开或收起影响。
3. 侧边栏选中项与顶部标题栏，作为补充。顶部标题在侧边栏收起时也会读取。
4. 同名会话：Codex 常把问候类短对话自动命名为相同标题（如多个「回应问候」）。依次比对可见的首条消息、排除已归档会话，最后选最近写入的会话。
5. 新会话索引尚未更新时，在内存中比较窗口可见短文本和最近会话首条消息的指纹。

确定会话后读取它的日志。会话恢复后，Codex 把后续内容写入新的分段文件 `rollout-…-<会话ID>_<分段ID>.jsonl`，用量栏会自动切到最新分段。闲置较久的会话会被 Codex 压缩为 `.jsonl.zst`；打开这类会话时，先显示会话库记录的累计 Token，发送新消息后恢复轮次、速度和缓存等完整指标。

如果仍长时间显示「识别会话…」，用 Codex 的会话菜单复制链接，再从用量栏托盘菜单选择「固定会话…」并粘贴。固定后显示「固定」，选择「跟随当前会话」可以恢复自动识别。

## 指标

| 显示 | 计算方式 |
| --- | --- |
| 轮 | 该会话日志记录的提问轮数 |
| 步 | 最近一轮中累计用量发生变化的模型上报次数 |
| ≈ tok/s | 最近一轮输出 Token 增量 ÷ 轮次耗时。进行中使用上次用量上报前的耗时，包含等待和工具运行时间 |
| tok | 当前会话最后一次上报的累计 Token |
| 缓存命中 | 累计缓存输入 Token ÷ 累计输入 Token |
| 上下文已用 | 最新一次模型上报的 `last_token_usage.total_tokens` ÷ 同一次上报的 `model_context_window`，上限 100% |

会话统计读取日志已上报的轮次总耗时和本轮首 Token 时间。工具耗时只汇总有完整计时的工具项，并行项的耗时分别计入。缺失字段显示「—」。Token 明细显示累计总量、缓存命中率、未缓存输入、缓存读取和输出。

点击胶囊的 Token 或缓存区，上半部分显示最新一次上报的上下文已用量、窗口容量和进度条。分色对应未缓存输入、已缓存输入和本次输出，同一份上报的数据能核对时才分色。Codex 的公开用量协议没有系统提示词、工具定义和消息的独立 Token 数。[Codex 用量协议](https://github.com/openai/codex/blob/main/codex-rs/app-server-protocol/schema/json/v2/ThreadTokenUsageUpdatedNotification.json)

用量随本机日志更新。可读取的数据来自 `CODEX_HOME` 或当前用户的 `.codex`。程序只读会话索引和统计事件，所有计算都在本机完成。用量栏依赖桌面客户端暴露的 Windows 辅助功能标签，适配简体中文、繁体中文和英文的常见模型按钮。远程会话需要本机已有对应的日志才可显示用量。Codex App Server 的账户额度与本机会话用量分别统计。[Codex App Server 文档](https://learn.chatgpt.com/docs/app-server)

## 加入 Codex 本地插件目录

要通过 Codex 对话开启或关闭，请先在 `plugins/codex-usage-bar` 中构建 `CodexUsageBar.exe`，再在仓库根目录运行 `codex plugin marketplace add .`。重启桌面客户端后打开插件目录，在「本地 Codex 用量栏」来源安装「Codex 用量栏」。安装后可以在对话中要求开启或关闭用量栏。

桌面插件的公开接口没有模型按钮旁的常驻插槽，浮层通过 Windows 辅助功能定位。支持内嵌交互 UI 的路径是 MCP 会话侧面板。如果需要自由控制输入区的布局，可以通过 Codex App Server 做自定义客户端。[OpenAI 插件扩展文档](https://developers.openai.com/plugins/build/extensions) · [Codex 平台文档](https://developers.openai.com/blog/codex-as-a-platform)

## 从源码编译

在 `plugins\codex-usage-bar` 中使用 Windows PowerShell 运行 `.\build.ps1`。源码位于 `src`（C#）、`ui-src`（设置页）和 `ui`（页面模板与构建产物），编译使用 Windows 自带的 .NET Framework C# 编译器和 SQLite 库。

- 设置页源码在 `ui-src/src`：React 18、Fluent UI React v9（组件库）、i18next + react-i18next（语言库，`ui-src/src/locales` 下为简体、繁體、English 三套文案）。依赖版本在 `ui-src/package.json` 中固定。
- 直接重编发布包运行 `.\build.ps1 -UseBundledUi`。此方式使用随包提供的设置页，无需 Node.js、npm 依赖或联网。
- 修改设置页后运行 `.\build.ps1`。装有 Node.js 时，脚本会从锁文件安装缺失的依赖，使用 esbuild 生成 `ui\settings.js` 和 `ui\THIRD_PARTY_NOTICES.txt`，再将网页资源嵌入 EXE。
- 在 `ui-src` 运行 `npm run preview`，然后打开 `http://localhost:5179`。浏览器预览使用示例数据。运行 `npm run watch` 编辑界面时，刷新浏览器即可查看最新构建。
- WebView2 SDK 二进制和许可证位于 `ui\vendor`，随程序打包，运行设置页时从本机加载。打包发布时不需要带上 `ui-src\node_modules`。

### 构建安装包

构建应用后，使用 NSIS 编译 `plugins/codex-usage-bar/installer/CodexUsageBar.nsi`。命令从仓库根目录运行。

```powershell
makensis.exe /INPUTCHARSET UTF8 .\plugins\codex-usage-bar\installer\CodexUsageBar.nsi
```
