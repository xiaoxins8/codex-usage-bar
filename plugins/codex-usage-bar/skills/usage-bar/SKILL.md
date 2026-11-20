---
name: usage-bar
description: 启动、设置或退出 Windows Codex 用量栏。用户想显示用量、五小时和周额度，或调整位置、自启、动画时使用。
---

# Codex 用量栏

- 用户要求开启时，从本技能目录向上两级找到插件根目录中的 `CodexUsageBar.exe`。在本机 Windows 用 PowerShell 的 `Start-Process -FilePath` 启动，传入可执行文件的完整路径和 `-ArgumentList '--background' -WindowStyle Hidden`。已有实例会保留。
- 用户要打开设置时，启动同一个可执行文件并传入 `-ArgumentList '--settings'`。设置窗口用于查看当前会话用量，调整位置、五小时与周额度显示、登录自启、动画和界面语言。也可以双击 Windows 托盘中的用量栏图标。
- 用户要求关闭时，启动同一个可执行文件，给 `Start-Process` 增加 `-ArgumentList '--stop'`。也可以从 Windows 系统托盘退出用量栏。
- 启动后，切回 Windows Codex 本地会话。窗口宽度足够时，用量会出现在模型选择器左侧。启动进程仅代表启动请求已发出。
- 点击胶囊的 Token 或缓存区，查看最近一次模型上报的上下文占用和会话累计用量。
- 显示「识别会话…」时，让程序继续匹配当前会话。若一直无法识别，可以从 Codex 复制当前会话链接，在用量栏托盘菜单选择「固定会话…」。选择「跟随当前会话」恢复自动跟随。「等待用量」表示已找到会话，正在等待本机日志上报。
