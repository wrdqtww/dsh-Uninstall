# dsh-Uninstall

DSH / DeepSeek Harness 桌面端独立卸载器。单个 exe 即可运行，支持官方版、第三方集合版/集成版、极简版/简洁版以及其他未知变体的通用卸载。

## 特点

- **多桌面端兼容**：不再只认一个注册表 GUID / 安装目录，自动扫描 HKLM/HKCU、32/64 位注册表视图、常见安装位置、运行中进程和已知变体目录。
- **变体识别**：窗口最上方显示当前识别的桌面端类型：
  - `官方 deepseek-ai/deepseek-harness`
  - `第三方 <仓库路径>`
  - `未知 null`
- **通用卸载兜底**：找不到已知变体时，按注册表卸载项、常见安装路径、进程名、快捷方式名自动清理。
- **可选保留**：默认删除全部用户数据；可在弹窗中按类别保留：
  - 预设（按实际显示名称勾选）
  - 插件（按 package.json 识别，列表可滚动）
  - 聊天数据（`.dsh\sessions`）
  - 应用设置（`settings.yaml`）
  - 模型配置与凭据（`.credentials.yaml` + `settings.yaml` 模型部分，共用文件自动合并）
  - 其他 `.dsh` 数据
  - `.dsh-runtime`
- **静默卸载**：`/S` 支持不弹窗执行，并可用命令行参数指定保留项。
- **日志**：运行后在当前目录生成 `Log.log`。
- **单文件发布**：最终产物只有一个 `Uninstall_DSH_Desktop.exe`，仅依赖 Windows 自带的 .NET Framework 4.x，不调用任何外部脚本/辅助 exe。

## 使用

双击 `Uninstall_DSH_Desktop.exe` 打开卸载确认窗口，勾选需要保留的内容后点击“卸载”。

静默示例：

```bat
Uninstall_DSH_Desktop.exe /S
Uninstall_DSH_Desktop.exe /S /KeepPresets=agent-sc /KeepChatData /KeepAppSettings /KeepModelConfig
Uninstall_DSH_Desktop.exe /S /KeepPlugins=@dsh-external/dsh-vision /DetectRunning
```

### 命令行参数

| 参数 | 说明 |
| --- | --- |
| `/S` | 静默模式，不弹窗 |
| `/KeepPresets` | 保留全部 `.agent-presets` 预设 |
| `/KeepPresets=名称1,名称2` | 仅保留指定预设 |
| `/KeepPlugins` | 保留全部检测到的插件（自动附带保留 `.dsh-runtime`） |
| `/KeepPlugins=包名1,包名2` | 仅保留指定插件包 |
| `/KeepRuntime` | 保留 `.dsh-runtime` |
| `/KeepVision` | 兼容旧参数：只保留识图插件 `@dsh-external/dsh-vision` |
| `/KeepAppSettings` | 保留应用设置 `settings.yaml` |
| `/KeepModelConfig` | 保留模型配置与凭据（`.credentials.yaml` + `settings.yaml` 模型部分） |
| `/KeepOtherUserData` | 保留预设/聊天/插件/设置之外的其他 `.dsh` 数据，别名 `/KeepOtherData` |
| `/KeepChatData` | 保留聊天数据 `.dsh\sessions`，别名 `/KeepChat` |
| `/KeepAll` | 保留全部可选项目 |
| `/DetectRunning` | 识别当前正在运行的 DSH 并卸载其目录，别名 `/DetectDSH` |
| `/Default` | 默认卸载模式（注册表/常见安装位置检测） |

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
├── DSH_Desktop_Uninstaller.cs      # 主程序源码
├── DshRetentionContract.cs         # 保留项扩展契约
├── DSH_Desktop_卸载说明.txt          # 详细使用/卸载说明
├── embed-icon-in-exe.ps1           # 构建时把图标写入 exe
├── make-uninstaller-icon.ps1       # 从 PNG 生成多尺寸 ICO（仅开发用）
├── Uninstall_DSH_Desktop.exe       # 预编译的单文件卸载器
├── Uninstall_DSH_Desktop_icon.ico  # 卸载器图标
├── Uninstall_DSH_Desktop_icon_preview.png
├── build-uninstaller.ps1           # 一键构建脚本
├── README.md
└── .gitignore
```

## 注意

- 卸载会删除 DSH / DeepSeek Harness 桌面端产生的用户数据及会话记录，请提前备份需要的内容。
- 运行时只依赖 Windows 自带 .NET Framework 4.x，不需要额外安装或附带 DLL。