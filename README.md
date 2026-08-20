# dsh-Uninstall

DSH / DeepSeek Harness 桌面端独立卸载器。单个 exe 即可运行，支持官方版、第三方集合版/集成版、简洁版/极简版以及其他未知变体的通用卸载。

## 特点

- **多桌面端兼容**：按注册表卸载项（HKLM/HKCU、32/64 位视图通用扫描）、常见安装位置、运行中进程、已知变体目录与快捷方式自动检测，不再依赖单一卸载 GUID / 安装路径。
- **变体识别**：窗口最上方显示当前识别的桌面端类型：
  - `官方 deepseek-ai/deepseek-harness`
  - `第三方 <仓库路径>`（例如 myYangyunfan/dsh_desktop、dataelement/dsh-desktop、AmazingBoyCrazy/dsh_desktop、Easyhoov/deepseek-harness-desktop-windows、steven-kid/deepseek-harness-desktop、majiayu000/dsh-desk、gxcsoccer/dsh-studio、FlashingChen/dsh-desktop-hub、Lxiayu/DshCockpit、zouyuxuan122/Deepseek-Harness-EAC 等）
  - `未知`（无法识别时使用通用卸载逻辑）
- **识别到具体仓库后按仓库收窄清理目标**：进程名、快捷方式名、程序目录名等自动切换为该仓库对应名称；未识别时使用全量通用列表，避免漏删。
- **可选保留**：默认删除全部用户数据，可在弹窗中按类别勾选保留：
  - 预设（按名称保留，显示实际预设名称）
  - 插件（按 package.json 识别，列表可滚动，勾选插件自动保留 `.dsh-runtime`）
  - skills（按 `.dsh\skills` 识别，按名称勾选保留）
  - 聊天数据（`.dsh\sessions`）
  - 应用设置（`settings.yaml`）
  - 模型配置与凭据（`.credentials.yaml` + `settings.yaml` 模型部分，共用文件自动合并）
  - 其他 `.dsh` 数据
  - `.dsh-runtime`
- **静默卸载**：`/S` 支持不弹窗执行，并可用命令行参数指定保留项。
- **日志**：默认在 exe 同目录生成 `Log.log`；若该目录会被卸载删除，自动保留副本到上一级目录（不可写时到桌面）。可用 `/Log=<路径>` 指定。
- **单文件发布**：最终 `Uninstall_DSH_Desktop.exe` 只依赖 Windows 自带的 .NET Framework 4.x，不调用额外脚本/DLL。

## 使用

双击 `Uninstall_DSH_Desktop.exe` 弹出确认窗口，选择卸载模式与可选保留项，点击「卸载」并再次确认后执行。

静默示例：

```powershell
Uninstall_DSH_Desktop.exe /S
Uninstall_DSH_Desktop.exe /S /KeepPresets=agent-sc /KeepChatData /KeepAppSettings /KeepModelConfig /KeepPlugins=@dsh-external/dsh-vision
Uninstall_DSH_Desktop.exe /S /KeepSkills=animate,prototype /DetectRunning
Uninstall_DSH_Desktop.exe /DryRun
```

## 命令行参数

| 参数 | 说明 |
| --- | --- |
| `/S` | 静默模式，不弹窗 |
| `/KeepPresets` | 保留全部 `.agent-presets` 预设 |
| `/KeepPresets=名称1,名称2` | 仅保留指定预设目录 |
| `/KeepPlugins` | 保留全部检测到的插件（自动附带保留 `.dsh-runtime`） |
| `/KeepPlugins=包名1,包名2` | 仅保留指定插件包 |
| `/KeepSkills` | 保留全部 skills（`.dsh\skills`） |
| `/KeepSkills=名称1,名称2` | 仅保留指定 skills |
| `/KeepRuntime` | 保留 `.dsh-runtime` |
| `/KeepVision` | 兼容旧参数：只保留识图插件 `@dsh-external/dsh-vision` |
| `/KeepAppSettings` | 保留应用设置 `settings.yaml` |
| `/KeepModelConfig` | 保留模型配置与凭据（`.credentials.yaml` + `settings.yaml` 模型部分） |
| `/KeepOtherUserData` | 保留预设/聊天/插件/skills/设置之外的其他 `.dsh` 数据，别名 `/KeepOtherData` |
| `/KeepChatData` | 保留聊天数据 `.dsh\sessions`，别名 `/KeepChat` |
| `/KeepAll` | 保留全部可选项目 |
| `/DetectRunning` | 识别当前正在运行的 DSH 并卸载其目录，别名 `/DetectDSH` |
| `/Default` | 默认卸载模式（注册表/常见安装位置检测） |
| `/InstallDir=C:\path\to\app` | 手动指定要卸载的安装目录（仅接受安全路径） |
| `/Log=<完整文件路径>` | 指定日志文件路径（默认见上方日志说明） |
| `/DryRun` | 只列出将删除的目标与保留项，不做实际删除，别名 `/Preview` |
| `/help` | 显示命令行选项说明 |

## 构建

需要 Windows + .NET Framework 4.x 自带编译器 `csc.exe`，运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-uninstaller.ps1
```

产物输出到：

```
build\Uninstall_DSH_Desktop.exe
```

## 目录结构

```
dsh-Uninstall/
├── DSH_Desktop_Uninstaller.cs                 # 入口 + 命令行解析 + 卸载流水线 + 日志
├── DSH_Desktop_Uninstaller.Core.cs            # 变体目录 / 名称匹配 / 保留选项 / 纯函数
├── DSH_Desktop_Uninstaller.Detection.cs       # 注册表 / 进程 / 安装目录 / 变体识别
├── DSH_Desktop_Uninstaller.Cleanup.cs         # 进程结束 / 文件与注册表清理 / PATH 清理
├── DSH_Desktop_Uninstaller.Retention.cs       # 预设 / 插件 / skills 检测与保留逻辑
├── DSH_Desktop_Uninstaller.Gui.cs             # 确认弹窗 / 保留项复选列表 / 进度窗口
├── DSH_Desktop_卸载说明.txt                     # 详细使用/卸载说明
├── embed-icon-in-exe.ps1                      # 构建时把图标写入 exe
├── make-uninstaller-icon.ps1                  # 从 PNG 生成多尺寸 ICO（仅开发用）
├── Uninstall_DSH_Desktop.exe                  # 预编译的单文件卸载器
├── Uninstall_DSH_Desktop_icon.ico             # 卸载器图标
├── Uninstall_DSH_Desktop_icon_preview.png
├── build-uninstaller.ps1                      # 一键构建脚本（自动编译根目录全部 .cs）
├── README.md
└── .gitignore
```

## 注意

- 卸载会删除 DSH / DeepSeek Harness 桌面端产生的用户数据及会话记录，请提前备份需要的内容。
- 运行时只依赖 Windows 自带 .NET Framework 4.x，不需要额外安装或附带 DLL。
