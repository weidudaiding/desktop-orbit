# Desktop Orbit · 桌面环形快捷菜单 技术方案

> 版本 v0.1（设计稿） · 平台：Windows 10 1809+ / Windows 11（验证机为 11 25H2，build 26200）
> 目标：桌面空白处长按唤出环形菜单，浏览/打开/拖出/搜索桌面与自定义目录中的文件，轻量常驻。

---

## 1. 目标与非目标

### 1.1 目标

| 编号 | 需求 | 验收口径 |
|---|---|---|
| G1 | 不依赖桌面图标显示状态读取桌面目录 | 关闭「显示桌面图标」后菜单仍完整列出文件 |
| G2 | 映射任意文件夹为扇区入口 | 配置多个根目录，一级环按配置排列 |
| G3 | 桌面空白处长按唤出环形菜单 | 按住 350ms 且位移 <6px，在光标所在屏弹出 |
| G4 | 扇区展开二级菜单显示文件与子文件夹 | 二级环可继续下钻，面包屑可回退 |
| G5 | 快速打开 / 拖拽 / 搜索 / 最近使用 | 拖入资源管理器、微信、浏览器上传框可用 |
| G6 | 样式可配置 | 扇区数、半径、主题、图标、动画、触发方式均在 JSON 内 |
| G7 | 轻量常驻、多屏、异分辨率 | 空闲 CPU <0.1%，RSS <90MB，Per-Monitor V2 |

### 1.2 非目标（本期不做）

- 不做文件管理器替代品（无批量重命名、无预览编辑）。
- 不注入 Explorer、不写文件系统驱动、不做代码注入。
- 不做跨平台。macOS 的 Finder 桌面机制完全不同，需另立项。
- 不做云同步 / 账号体系（未来版本若加，必须显式 opt-in）。

---

## 2. 结论与三个关键取舍

整体可行。真正需要先定策略的只有三件事，其余都是工程量。

### 2.1 决策一：长按检测

| 方案 | 实现 | 优点 | 代价 |
|---|---|---|---|
| **A. 轮询（推荐默认）** | `GetAsyncKeyState(VK_LBUTTON)` + `GetCursorPos` + `WindowFromPoint`，20Hz 空闲 / 60Hz 按住 | 无全局钩子、无注入、杀软友好、Explorer 崩溃不受影响 | 触发后那一次 mouse-up 仍落到桌面（仅取消选中，无害） |
| B. 低级鼠标钩子 | `SetWindowsHookEx(WH_MOUSE_LL)`，触发时返回 1 吞掉 mouse-up | 可完全吞掉点击 | 全局钩子易被 EDR 关注；与其他壳软件冲突概率高；钩子超时会被系统摘除；有输入延迟 |
| C. Shell 右键菜单扩展 | 注册 `IContextMenu` 到桌面背景菜单 | 100% 官方机制，零误触 | 需要右键而非长按；DLL 需安装/注册；不是一个「长按」交互 |

**结论**：默认 A，B 作为「严格模式」开关（默认关闭），C 放到 M5 作为可选增强。

### 2.2 决策二：隐藏「个别」桌面图标

Explorer **没有**按项隐藏桌面图标的公开 API。可落地的三条路：

1. **收纳目录（推荐）**：目标文件夹移到 `%USERPROFILE%\DesktopVault`（可配置），菜单里作为扇区入口。文件不在桌面目录了，但体验等价，且可逆、零风险。
2. **Hidden + System 属性**：靠系统默认的「隐藏受保护的操作系统文件」生效。用户一开「显示隐藏文件」就会露出来，属半可靠。
3. **整层隐藏**：`SendMessage(hSHELLDLL_DefView, WM_COMMAND, 0x7402, 0)` 等价于点「查看 → 显示桌面图标」开关。可用于「一键清空桌面」，但作用于全部图标。

**结论**：产品化做 1（收纳目录 + 拖入收纳），2 作为高级选项并明确风险提示，3 作为一个托盘快捷动作。

### 2.3 决策三：是否需要跨进程读 Explorer 内存做「空白色」判定

- 精确判定需要 `LVM_HITTEST` 的跨进程缓冲区（`VirtualAllocEx` + `WriteProcessMemory` + `ReadProcessMemory` on explorer.exe）。
- 该操作合法（同用户、同会话），但部分 EDR/杀软会告警。
- **结论**：做成能力探测。启动时试一次，成功则启用精确判定；失败自动降级为「按住时长 + 位移」判定（误触代价：在图标上长按也会弹菜单，可接受）。

---

## 3. 总体架构

单进程、双线程核心，全部无管理员权限。

```
                    ┌─────────────────────────── DesktopOrbit.App ───────────────────────────┐
                    │  Tray / Single-Instance / Autostart / Logging / Hotkey host             │
                    └───────────────┬──────────────────────────────┬──────────────────────────┘
                                    │                              │
                     ┌──────────────▼────────────┐    ┌────────────▼─────────────┐
                     │  TriggerService           │    │  OrbitWindow (WPF)       │
                     │  · GlobalHotkey (RegisterHotKey)                  │  · 扇区几何/命中/动画/二级环 │
                     │  · LongPressDetector (poll)│    │  · SearchPanel (IME)     │
                     │  · Optional LL hook        │    │  · DragSource (OLE)      │
                     │  · FullscreenGuard         │    └────────────▲─────────────┘
                     └──────────────┬────────────┘                 │
                                    │ Show/Hide                     │ items
                     ┌──────────────▼──────────────────────────────────────────────┐
                     │  Core: LayoutEngine · MenuStateMachine · ConfigStore(热重载)   │
                     └──────────────┬──────────────────────────────────────────────┘
                                    │
        ┌───────────────────────────▼───────────────────────────────────────────────┐
        │  ShellService                                                              │
        │  · DesktopLocator (Progman/WorkerW/SHELLDLL_DefView/SysListView32)         │
        │  · FolderSource (Desktop/PublicDesktop/自定义根)                             │
        │  · IconProvider (IShellItemImageFactory + 磁盘缓存)                          │
        │  · ShortcutResolver (IShellLink)                                           │
        │  · RecentProvider (Recent\*.lnk + 自有 frecency)                            │
        │  · SearchService (Windows Search OLE DB / 本地索引 / Everything SDK)         │
        └────────────────────────────────────────────────────────────────────────────┘
```

线程模型：

- **UI 线程**（WPF Dispatcher）：窗口、动画、命中、拖拽起手。
- **Probe 线程**：20/60Hz 轮询、`GetAsyncKeyState`、`WindowFromPoint`；只做状态机，不碰 UI 对象（通过 `Dispatcher.BeginInvoke` 交付事件）。
- **IO**：`Task` + `Channel`，图标/缩略图异步加载，先占位后替换；`FileSystemWatcher` 合并 300ms 防抖。

---

## 4. 技术选型

| 维度 | 选择 | 理由 |
|---|---|---|
| 语言/运行时 | C# / .NET 8（LTS） | 开发效率与 Win32 互操作都够用；`LibraryImport` 源生成 P/Invoke |
| UI | WPF（`AllowsTransparency` 分层窗口） | 逐像素透明、几何/动画/文本栈成熟；WinUI 3 对任意形状透明窗口支持差且部署重 |
| 托盘 | `H.NotifyIcon` 或 WinForms `NotifyIcon` | 避免自绘 Shell_NotifyIcon 的坑 |
| 配置 | `System.Text.Json` + JSON Schema 校验 + `FileSystemWatcher` 热重载 | 无依赖、可手改 |
| 本地库 | SQLite（`Microsoft.Data.Sqlite`）存使用频率与图标缓存索引 | 单文件、零服务 |
| 搜索（首选） | Windows Search OLE DB（`Search.CollatorDSO`） | 复用系统索引，零自建成本 |
| 搜索（兜底） | 目录枚举 + 内存/FTS5 索引 | 系统索引关闭或未覆盖时可用 |
| 搜索（可选） | Everything SDK（`Everything64.dll`，IPC 非注入） | 全盘即时；作为插件后端 |
| 打包 | `dotnet publish` 自包含单文件 + Inno Setup | 免装运行时；体积约 70–90MB，可接受 |
| 渲染降级路径 | 若分层窗口掉帧 → `Vortice.Windows` + DirectComposition + `WS_EX_NOREDIRECTIONBITMAP` | 仍是 C#，不换语言；保留接口即可切换 |

> 已知事实：WPF 的 `AllowsTransparency=true` 走 `UpdateLayeredWindow`，部分机器上是软件合成。菜单窗口尺寸受控（约 720×720 DIP）+ 几何缓存后通常可达 60fps；若不达标按上表降级，不改架构。

### 4.1 与「拼装现成工具」的对比

| 方案 | 覆盖度 | 缺口 |
|---|---|---|
| Quicker 轮盘 + 动作库 | 触发、扇区、二级轮盘、搜索 | 不能「浏览文件夹内容」并逐项拖出 |
| Stardock Fences | 收纳、整层隐藏 | 无环形菜单 |
| Everything / Listary | 搜索 | 无桌面环形交互 |
| AutoHotkey 脚本 | 长按 + 菜单 | 拖拽出、缩略图、多屏 DPI、可维护性都差 |

组合方案能凑出 70% 体验，但「二级环里直接拖文件到微信」这一条没有现成件，所以自研有明确价值。

---

## 5. 模块设计

### 5.1 TriggerService

```csharp
interface ITriggerSource { event Action<TriggerContext> Triggered; void Start(); void Stop(); }
```

| 触发器 | 实现 | 默认 |
|---|---|---|
| GlobalHotkey | `RegisterHotKey(hwnd, id, MOD_*, vk)`，注册失败（冲突）时托盘提示并回退 | `Ctrl+Alt+Space` |
| LongPress | 见 §6.1 | 开 |
| MiddleClickOnDesktop | 轮询 `VK_MBUTTON` + 桌面判定 | 关 |
| TrayMenu | 托盘「打开菜单」 | 开 |
| CommandLine | `DesktopOrbit.exe --menu`（给 AHK/StreamDeck 调用） | 开 |
| ShellContextMenu | §2.1 方案 C | M5 可选 |

统一出口：`MenuStateMachine.Open(context)`，并做去重（300ms 内重复触发忽略）。

`FullscreenGuard`：`SHQueryUserNotificationState()` 返回 `QUNS_RUNNING_D3D_FULL_SCREEN / QUNS_BUSY / QUNS_PRESENTATION_MODE` 时抑制弹出（可配置 `pauseWhenFullscreen`）。

### 5.2 DesktopLocator / DesktopProbe

职责：判断「当前点是否在桌面空白处」。

```
桌面宿主层级（两种形态都要支持）：
  Progman
    └ SHELLDLL_DefView
        └ SysListView32          ← 图标可见时
  WorkerW（壁纸轮播/某些显卡驱动下的宿主）
    └ SHELLDLL_DefView
        └ SysListView32
关闭「显示桌面图标」后，SHELLDLL_DefView 可能被销毁，只剩 Progman/WorkerW。
```

判定流程：

1. `GetCursorPos` → `WindowFromPoint` → `GetClassName`。
2. 类名 ∈ {`SysListView32`, `SHELLDLL_DefView`, `Progman`, `WorkerW`} → 视为桌面。
3. 若命中 `SysListView32` 且 `requireBlankSpot=true`：对目标坐标发 `LVM_HITTEST`（需远程缓冲区，见 §2.3）。`iItem == -1` 才是空白。
4. 桌面宿主句柄**每次触发都重新解析**，不做长期缓存（Explorer 重启、显示器热插拔都会变）。

### 5.3 OrbitWindow（环形菜单）

**窗口属性**

```
WindowStyle=None, ResizeMode=NoResize, ShowInTaskbar=false, Topmost=true, AllowsTransparency=true
扩展样式：WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE*（见 §6.3）
显示：ShowActivated=false 打开，但拖拽起手前临时激活
```

**几何**

- 中心 `hub`：半径 `R * hubRadiusRatio`（默认 0.36），承载搜索框/面包屑/最近项。
- 扇区：内径 `hub`，外径 `R`，扇区角度 = `360 / n`，间隔 `gapDeg`（默认 1.5°，可 0）。
- 命中：`dist ∈ [hub, R]`，`angle` 映射到槽位（含角度环绕与 gap 死区）。
- 二级环：外扩模式（`R2 = R + ringWidth`）或同径替换模式，默认外扩；`hoverDelayMs=320` 触发下钻，`Esc`/右键回退一级，面包屑显示 `桌面 / 项目 / 2026`。
- 每屏独立：按 `MonitorFromPoint` 取工作区，菜单中心限制在工作区内（留出 `shadowMargin`）。

**渲染**

- `Canvas` + `Path`（`ArcSegment`）生成扇区几何，`Geometry.Freeze()`，主题切换时重建。
- 悬停：沿角平分线外移 `hoverExpandPx`（默认 6）+ 高亮渐变 + 图标放大 1.08。
- 打开动画：scale 0.86→1.0 + opacity 0→1，`140ms` `CubicEase`；关闭 `90ms` 反向。
- 文本：`FormattedText`/`TextBlock` 截断 + 省略号；扇区数 > 8 时标签沿切向旋转或隐藏（配置）。

**状态机**：`Closed → Opening → IdleLevel(n) → Drilling(n+1) → Dragging → Closing`。所有状态切换走单一 `Reduce()` 函数，便于单测。

### 5.4 Sources / ItemProvider

```csharp
record MenuItem(string Path, string Label, ItemKind Kind, ImageSource? Icon, bool IsFolder, DateTime LastWrite);
enum ItemKind { Folder, File, Shortcut, Url, App }
```

- 桌面源：`SHGetKnownFolderPath(FOLDERID_Desktop)` + `FOLDERID_PublicDesktop` 合并去重（同名以用户目录优先）。
- 自定义源：`sources[]` 内 `type: folder|desktop|files|recent`。
- 枚举：`EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = Hidden|System }`（可配置显示隐藏项），过滤 `desktop.ini`、`thumbs.db`。
- 排序：默认「文件夹优先 → 名称自然序」；可选按修改时间/使用频率。
- 二级环条目上限：默认 12，超出折叠为「更多…」扇区（点击后转为列表视图，避免环无限细分）。
- 失效处理：路径不存在 → 扇区置灰 + 角标，不抛异常。

### 5.5 IconProvider

- 图标：`SHCreateItemFromParsingName` → `IShellItemImageFactory.GetImage(size, SIIGBF_ICONONLY | BIGGERSIZEOK)`。
- 缩略图：同接口 `SIIGBF_THUMBNAILONLY | SIIGBF_RESIZETOFIT`（图片/视频），配置开关，异步加载。
- 缓存：`%LOCALAPPDATA%\DesktopOrbit\cache\icons\{sha1(path|mtime|size)}.png`，LRU 上限 2000 项 / 200MB。
- 回退：`SHGetFileInfo(SHGFI_SYSICONINDEX)` + `SHGetImageList(SHIL_JUMBO)`。
- 加载策略：菜单打开时先用占位符，逐项批量回填；`CancellationToken` 绑定菜单关闭。

### 5.6 SearchService

三级后端，按 `search.backend = auto` 依次尝试：

1. **Windows Search（OLE DB）**
   ```
   Provider=Search.CollatorDSO;Extended Properties="Application=Windows";
   SELECT TOP 50 System.ItemPathDisplay, System.ItemNameDisplay, System.DateModified
   FROM SYSTEMINDEX
   WHERE System.ItemPathDisplay LIKE 'C:\Users\me\Desktop\%'
     AND System.FileName LIKE '%报告%'
   ORDER BY System.DateModified DESC
   ```
   注意：`LIKE` 需转义 `% _ [ ] '`；限定 scope 为已配置根目录，避免全盘扫描；`CommandTimeout=150ms`，超时即降级。
2. **本地索引**：对配置根做深度受限枚举（默认 `depth=2`），结果进 SQLite FTS5（`unicode61` 分词）+ 中文按子串匹配兜底；`FileSystemWatcher` 增量更新。
3. **Everything SDK（可选）**：`Everything_SetSearchW` → `Everything_QueryW` → `Everything_GetResultFullPathNameW`，走 IPC，不注入。

输入体验：首字符即出结果（输入法组合期间用 `Composition` 事件避免半成品查询），`↑↓` 选择、`Enter` 打开、`Ctrl+Enter` 定位、`Tab` 拖出。

### 5.7 RecentProvider

- 系统侧：读 `%APPDATA%\Microsoft\Windows\Recent\*.lnk`，用 `IShellLink` 解析目标，过滤不存在项，按 `.lnk` 修改时间排序。
- 自有侧：SQLite 表 `usage(path PRIMARY KEY, count, last_used)`，评分 `frecency = count * exp(-days/14)`。
- 合并去重，取前 8 条进 hub 的「最近」。

### 5.8 ActionService

| 动作 | 实现 |
|---|---|
| 打开文件 | `ShellExecuteExW(SEE_MASK_INVOKEIDLIST)`，verb `open` |
| 以管理员打开 | verb `runas`（触发 UAC） |
| 打开所在目录 | `explorer.exe /select,"<path>"` |
| 打开文件夹 | `ShellExecuteEx` 直接打开，或 `explorer.exe "<path>"` |
| 复制路径 | `Clipboard.SetText` |
| 拖出 | §6.3 |
| 加入收纳 | 移动到 vault 目录 + 更新配置 + 托盘提示 |

---

## 6. 关键实现细节

### 6.1 长按检测（默认轮询）

```csharp
// Probe 线程
const int HoldMs = 350, SlopPx = 6;
Point downPt = default; long downTicks = 0; bool armed = false;

loop (interval = lbuttonDown ? 16ms : 50ms):
    if (paused) continue;
    bool down = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
    var p = GetCursorPos();

    if (!down) { armed = false; downTicks = 0; continue; }
    if (!armed) { armed = true; downPt = p; downTicks = Environment.TickCount64; continue; }

    if (Environment.TickCount64 - downTicks < HoldMs) continue;
    if (Distance(p, downPt) > SlopPx) { armed = false; continue; }   // 已变成拖拽/框选
    if (!IsDesktopPoint(p)) { armed = false; continue; }
    if (FullscreenGuard.IsBlocked) { armed = false; continue; }
    if (ForegroundChanged) { ... }   // 可配置：仅在桌面为前台时触发

    armed = false;                    // 一次按住只触发一次
    RaiseTriggered(new TriggerContext(p, TriggerSource.LongPress));
```

要点：

- 轮询线程用 `WaitHandle.WaitOne(interval)`（可被退出信号唤醒），避免 `Thread.Sleep` 的唤醒抖动。
- 20Hz 空闲轮询的实际 CPU 开销在测量噪声内；电池敏感场景提供「仅热键模式」。
- 触发后 mouse-up 会落到桌面：仅清除图标选中，属良性副作用。若用户要求彻底吞掉，切严格模式（方案 B）。

### 6.2 拖拽起手（OLE）

```csharp
// 必须在 UI 线程、悬停项上按下并移动超过系统拖拽阈值后调用
SetForegroundWindow(menuHwnd);              // 关键：无前台窗口时目标程序不接
var data = new DataObject(DataFormats.FileDrop, new[] { item.Path });
data.SetData("Preferred DropEffect", new MemoryStream(new byte[] { (byte)DragDropEffects.Copy, 0, 0, 0 }));
DragDrop.DoDragDrop(sourceElement, data, DragDropEffects.Copy | DragDropEffects.Move);
```

约束与已知问题：

- **UIPI**：非提权进程无法拖入提权窗口（管理员 CMD/编辑器）；反之亦然。产品文档需明示「同完整性级别」。
- 菜单窗口若长期 `WS_EX_NOACTIVATE`，拖拽会失败：在 pointer-down 时临时清除该样式并激活，拖拽结束后恢复，或整个菜单就用普通激活窗口（失焦关闭）。
- 拖出后菜单应保持 ~200ms 再淡出，避免"东西没了"的观感突兀。
- 右键拖拽（移动/复制菜单）不在首版范围。

### 6.3 窗口激活策略（关键取舍）

| 策略 | 长按触发 | 拖出 | 焦点行为 | 结论 |
|---|---|---|---|---|
| 普通激活窗口 + `ShowActivated=true` | 可用 | 简单 | 会抢焦点，桌面失焦（桌面本来也会失焦） | **推荐** |
| `WS_EX_NOACTIVATE` | 可用 | 需要临时改造 | 不抢焦点，IME 输入框无法输入 | 仅用于「只浏览不搜索」的轻量模式 |

结论：**搜索需要真实焦点**，所以主菜单用普通激活窗口 + `Topmost`，失焦即关闭（可配置 `dismissOnFocusLoss`）。`WS_EX_TOOLWINDOW` 保证不进 Alt+Tab、不占任务栏。

### 6.4 DPI 与多屏

- `app.manifest`：`<dpiAwareness>PerMonitorV2</dpiAwareness>`；`SetProcessDpiAwarenessContext(PER_MONITOR_AWARE_V2)` 兜底。
- WPF 下按 `VisualTreeHelper.GetDpi(this)` 取 `DpiScale`，所有半径以 DIP 定义，几何按实际像素生成。
- `WM_DPICHANGED` 时 WPF 自动缩放，但菜单已打开且跨屏拖拽时需重建几何（监听 `DpiChanged`）。
- 判定屏：`MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST)`；工作区 `GetMonitorInfo`，菜单中心偏移避开任务栏。
- 混合 DPI（150%+200%）下图标位图按 `size * dpiScale` 请求，避免模糊。

### 6.5 中文输入（IME）

菜单内含搜索框，是**中文用户最容易踩的坑**：

- 分层窗口（`AllowsTransparency`）下 IME 候选窗定位偶尔偏移 → 需要 `WM_IME_*` 消息透传测试。
- 输入法组合期间（`WM_IME_COMPOSITION`）不发查询，`WM_IME_ENDCOMPOSITION` 后再查。
- 若实测有严重问题，降级方案：搜索面板改为独立的非分层小窗口（同一视觉风格），仅菜单主体用分层窗口。

### 6.6 图标/缩略图缓存键

```
key = sha1(normalizedPath + "|" + lastWriteTicks + "|" + pixelSize + "|" + thumbnailFlag)
```

目录内容变化时只失效受影响项，不清空整库。

---

## 7. 配置模型

位置：`%APPDATA%\DesktopOrbit\config.json`（首次启动生成默认值），`FileSystemWatcher` 热重载，解析失败保留旧配置并在托盘提示。

```json
{
  "$schema": "./config.schema.json",
  "version": 1,
  "general": {
    "startWithWindows": true,
    "language": "zh-CN",
    "pauseWhenFullscreen": true,
    "vaultPath": "%USERPROFILE%\\DesktopVault"
  },
  "trigger": {
    "hotkey": "Ctrl+Alt+Space",
    "longPress": {
      "enabled": true,
      "holdMs": 350,
      "moveSlopPx": 6,
      "requireBlankSpot": true,
      "mode": "polling"
    },
    "middleClickOnDesktop": false,
    "strictSwallowClick": false
  },
  "menu": {
    "outerRadiusDip": 240,
    "hubRadiusRatio": 0.36,
    "sectorGapDeg": 1.5,
    "openAnimationMs": 140,
    "hoverDelayMs": 320,
    "hoverExpandPx": 6,
    "maxSectors": 12,
    "submenuStyle": "outerRing",
    "dismissOnFocusLoss": true,
    "idleTimeoutMs": 8000
  },
  "appearance": {
    "theme": "dark-acrylic",
    "accentColor": "#7C5CFF",
    "backgroundOpacity": 0.82,
    "blur": true,
    "iconSizeDip": 40,
    "fontFamily": "Microsoft YaHei UI",
    "animations": true,
    "showLabels": true
  },
  "sources": [
    { "id": "desktop", "type": "desktop", "label": "桌面", "icon": "desktop" },
    { "id": "work",    "type": "folder",  "path": "D:\\Work", "label": "工作", "icon": "folder", "depth": 1 }
  ],
  "layout": {
    "pages": [
      { "id": "main", "sectors": [ { "sourceId": "desktop" }, { "sourceId": "work" }, { "path": "C:\\Tools" } ] }
    ]
  },
  "search": {
    "backend": "auto",
    "maxResults": 50,
    "timeoutMs": 150,
    "includeSubfolders": true
  },
  "actions": {
    "dragEnabled": true,
    "defaultOpenVerb": "open",
    "confirmAdminOpen": true
  }
}
```

主题单独成文件：`%APPDATA%\DesktopOrbit\themes\*.json`（颜色、渐变、圆角、发光、字体、动画曲线），与 `appearance.theme` 关联；支持用户覆盖 8 个关键色。

---

## 8. 性能预算

| 指标 | 目标 | 测量方式 |
|---|---|---|
| 冷启动到托盘可用 | < 300ms | `Stopwatch` 打点写日志 |
| 空闲 CPU（20Hz 轮询） | < 0.1% | 任务管理器 60s 均值 / `dotnet-counters` |
| 空闲 RSS | < 90MB（WPF 基线） | `Process.WorkingSet64` |
| 触发到首帧 | < 80ms（热态） | ETW + 打点 |
| 图标填充完成（12 项） | < 250ms（缓存命中 < 50ms） | 打点 |
| 动画帧率 | ≥ 55fps | `CompositionTarget.Rendering` 帧间隔采样 |
| 搜索首屏 | < 150ms | `CommandTimeout` + 打点 |

优化手段：菜单窗口启动时**预热**（创建隐藏窗口、预生成 Geometry、预加载主题 Brush）；图标全部异步；搜索结果虚拟化；任何 `Directory.Enumerate*` 都带 `CancellationToken` 和深度/数量上限。

---

## 9. 兼容性与风险矩阵

| 风险 | 触发条件 | 影响 | 缓解 |
|---|---|---|---|
| Explorer 重启 / 崩溃 | 更新、手动重启、第三方壳 | 桌面句柄失效 | 每次触发重新解析；`IsWindow` 校验；不缓存长期句柄 |
| 关闭「显示桌面图标」导致结构变化 | 用户操作 | 找不到 `SysListView32` | 状态机接受 `Progman/WorkerW` 作为桌面；命中判定降级 |
| 第三方桌面壳（Fences / StartAllBack / ExplorerPatcher） | 安装后类名/层级变化 | 长按失效 | 类名白名单 + 用户可配置；启用钩子严格模式作为备选 |
| 全屏游戏 / 演示模式 | `SHQueryUserNotificationState` 命中 | 弹菜单打断 | 直接抑制触发 |
| EDR / 杀软告警 | 跨进程读内存（空白色精确判定） | 被拦截或卸载 | 能力探测失败即降级，不写内存也能用；二进制签名 |
| UIPI | 拖入提权窗口 | 拖拽失败 | 文档说明；提示「目标程序需同权限级别」 |
| 混合 DPI 模糊 | 150%+200% 双屏 | 图标糊 | 按屏 DPI 请求位图；`DpiChanged` 重建 |
| 分层窗口掉帧 | 低端 GPU / 远程桌面 | 动画卡 | 缓存几何 + 降级到 DirectComposition 渲染 |
| 中文 IME 候选框错位 | 分层窗口 + 微软拼音 | 搜索体验差 | 见 §6.5；必要时搜索面板独立窗口 |
| 热键冲突 | 与 IDE/输入法冲突 | 唤不出 | 注册失败时托盘提示 + 引导改键（提供候选列表） |
| 多用户/远程 | RDP、切换用户 | 单实例互斥域 | `Local\` 互斥体（按会话），不跨会话干预 |

---

## 10. 安全与隐私

- 不联网（更新检查为显式 opt-in），不采集使用数据。
- 不注入进程、不写系统目录、不装驱动、不请求管理员权限（`runas` 仅作为用户主动动作）。
- 日志仅记录路径与错误，不含文件内容；日志轮转上限 5MB × 3。
- 拖拽/打开全部走 Shell，不自己拼接命令行字符串（防参数注入）。
- 代码签名（可选但建议）：自签或 EV 证书，降低 SmartScreen/AV 误报。

---

## 11. 里程碑与验收（按依赖排序，不绑定日历）

**M0 骨架**
托盘、单实例、开机启动、配置加载/热重载、热键唤出空壳环形窗口。
验收：`Ctrl+Alt+Space` 弹出/关闭；不占任务栏、不进 Alt+Tab；配置文件改半径即时生效。

**M1 一级环 + 桌面源**
扇区几何、命中、悬停、桌面/公共桌面合并枚举、打开动作。
验收：关闭「显示桌面图标」后仍列出全部项；点扇区能打开目标。

**M2 二级环 + 图标 + 搜索**
下钻/回退/面包屑、`IShellItemImageFactory` 图标与缩略图缓存、hub 搜索框（Windows Search + 本地索引降级）。
验收：二级环 12 项以内流畅；输入中文即时出结果；搜索超时自动降级不卡 UI。

**M3 拖出 + 最近使用**
`DoDragDrop` + `CF_HDROP`、拖拽起手置前、Recent + frecency、排序与「更多…」折叠。
验收：拖到资源管理器 / 微信窗口 / 浏览器上传框成功；拖出后菜单正确淡出。

**M4 长按 + 多屏打磨**
轮询检测、`requireBlankSpot` 精确判定与降级、全屏抑制、Per-Monitor V2、混合 DPI、菜单跨屏定位。
验收：桌面空白 350ms 弹出；图标上长按不弹（能力可用时）；150%/200% 双屏下图标清晰、命中准确。

**M5 配置化与产品化**
主题文件、动画/扇区数配置、托盘收纳动作（含整层隐藏开关）、崩溃恢复与日志、Inno Setup 安装包、可选 Shell 右键菜单集成。
验收：改主题 JSON 即时生效；断电式杀进程后重启无残留；安装/卸载干净（无残留注册表与开机项）。

---

## 12. 工程结构

```
projects/desktop-orbit/
  DesktopOrbit.sln
  src/
    DesktopOrbit.App/          WPF 外壳：托盘、单实例、DI、启动项、日志
    DesktopOrbit.Core/         领域模型、配置、布局引擎、菜单状态机（无 UI 依赖，可单测）
    DesktopOrbit.Interop/      P/Invoke（LibraryImport）、COM 互操作、结构体与常量
    DesktopOrbit.Shell/        桌面定位、枚举、图标、快捷方式、最近、搜索后端
    DesktopOrbit.UI/           环形菜单控件、动画、主题、搜索面板
  tests/
    DesktopOrbit.Tests/        xUnit：几何命中、状态机、配置校验、frecency、搜索降级
    DesktopOrbit.UI.Smoke/     FlaUI 冒烟：唤出、选扇区、启动记事本、拖拽到资源管理器
  docs/
    TECH_DESIGN.md             本文件
    config.schema.json
  build/
    app.manifest               PerMonitorV2、LongPathAware
    installer.iss
```

依赖方向：`App → UI → Core ← Shell`，`Interop` 被 `Shell`/`UI` 引用；`Core` 不出现 `System.Windows`。

---

## 13. 测试策略

| 层级 | 内容 |
|---|---|
| 单元 | 角度→槽位映射（环绕、gap 死区）、状态机非法转移、配置校验、frecency 排序、搜索 SQL 转义、LRU 缓存 |
| 集成 | 临时目录造 200 项压力枚举；文件增删触发 `FileSystemWatcher` 后菜单刷新；Windows Search 关闭时降级路径 |
| UI 冒烟（FlaUI） | 热键唤出 → 键盘选扇区 → 二级环 → 回车打开测试目标 → 拖拽到资源管理器（校验文件被复制） |
| 手动矩阵 | Win11 25H2 / Win10 22H2；100%/150%/200% DPI；双屏异 DPI；图标显示/隐藏；Explorer 重启；第三方壳各一；微软拼音中文搜索；全屏游戏抑制；RDP 会话 |

---

## 14. 待决策项（需要产品侧拍板）

1. **隐藏个别图标**：走「收纳目录」为主（推荐），还是也要做 `Hidden+System` 高级选项？
2. **空白色精确判定**：允许跨进程读 explorer 内存（更准，可能触发 AV），还是坚持零内存写读（更稳，图标上长按也会弹）？
3. **搜索后端**：只做 Windows Search + 本地降级，还是把 Everything SDK 作为官方可选后端？
4. **收纳动作**：是否需要「拖进菜单 → 自动移入 vault」这种改写文件位置的功能（涉及数据迁移，要确认提示策略）？
5. **分发形态**：自包含单文件（免装运行时，约 80MB）还是框架依赖（约 10MB，要求用户装 .NET 8 桌面运行时）？
6. **是否需要** Shell 右键菜单入口与命令行触发（给 AHK/StreamDeck 用）。

---

## 15. 实现状态（v0.1）

已实现并有实测记录：

| 能力 | 状态 | 说明 |
|---|---|---|
| 桌面目录读取（不依赖图标显示） | 完成 | 合并用户桌面与公共桌面，去重、自然序排序 |
| 环形菜单渲染 | 完成 | WPF 分层窗口 + 自绘，几何冻结，悬停缓动 |
| 二级下钻 / 返回 | 完成 | 悬停延时或单击下钻，右键 / Esc 返回 |
| 扇区数上限与翻页 | 完成 | 超出 `maxSectors` 折叠为 `+N` |
| 图标与缩略图 | 完成 | `IShellItemImageFactory` + 磁盘缓存，异步回填 |
| 搜索 | 完成（本地索引） | 输入即搜，结果叠加在环中央 |
| 拖出（CF_HDROP） | 完成 | 实测可拖入资源管理器 |
| 长按触发 | 完成 | 轮询实现，实测在桌面空白处触发 |
| 全局热键 | 完成 | `RegisterHotKey`，冲突时托盘提示 |
| 多屏 / Per-Monitor V2 | 完成 | 按光标所在屏定位，窗口夹紧在工作区内 |
| 托盘 / 单实例 / 开机启动 / 配置热重载 | 完成 | 托盘自绘 HICON，无 WinForms 依赖 |

未实现：

- 全局钩子严格模式（`trigger.strictSwallowClick` 目前无效）。
- Windows Search / Everything 搜索后端。
- `Hidden+System` 按项隐藏桌面图标、收纳目录自动迁移。
- Shell 右键菜单集成（§2.1 方案 C）。
- 安装包、代码签名。
- 主题文件外置（当前主题内联在配置里）。

实现过程中修正的设计偏差：

- 菜单窗口尺寸固定为正方形，搜索面板会超出窗口，故改为结果叠加在环中央、环降为 35% 不透明度。
- 定位夹紧必须用「窗口半宽（含阴影边距）」而非环半径，否则窗口会溢出工作区。
- 开场动画不参与透明度：动画计时器一旦被抑制，环会永久半透明。改为动画只驱动悬停缓动。
- 前台焦点需要 `AttachThreadInput` 变通，否则不可见输入框拿不到键盘与 IME 输入。

### 15.1 v0.2：设置面板与交互配置

| 能力 | 状态 | 说明 |
|---|---|---|
| 设置面板（分组 + 属性 + 实时预览） | 完成 | `SettingsWindow`；声明式 `SettingsCatalog` 直接读写 AppConfig，不存在副本 |
| 关闭模式：点击外部 | 完成 | 窗口内空白处立即关；窗口外由 `PointerMonitor` 轮询按下沿判定 |
| 关闭模式：移出关闭 | 完成 | 死区时间 + 边缘缓冲，可与点击模式同时开启 |
| 下钻触发：单击 / 悬停 X 毫秒 | 完成 | `menu.drill.onClick` 与 `onHover` 独立开关，延时 100–3000ms |
| 回退：Esc / 右键 | 完成 | 可单独关闭；Esc 默认「先回退、到底再关闭」 |
| 搜索可读性 | 修复 | 结果卡片近实心，抬头显示查询词与命中数；hub 内绘制输入文字与光标 |

本轮定位到的根因：

- **窗口永久半透明**：`HideMenu` 给 `Window.Opacity` 挂了 `DoubleAnimation`。WPF 中动画优先级高于直接赋值，之后 `Opacity = 1` 被忽略，窗口停在淡出中途。已移除窗口级 fade，只保留控件级缓动。
- **搜索输入不可见**：输入框是 0.01 不透明度的隐藏控件，hub 只画占位文字。改为在 hub 内绘制查询文字与光标，并在结果卡片抬头重复显示。
- **结果面板背景宽度为 0**：`SearchPanelView` 只调 `InvalidateVisual`，宿主 Canvas 不会重新排版，`ActualWidth` 始终为 0，于是背景矩形为 0 宽、文字照常绘制（看起来像"面板透明"）。已在 `SetResults` 补 `InvalidateMeasure`。
- **点击外部无法关闭**：窗口带圆形 region，落在窗口外的点击根本收不到。新增 `PointerMonitor` 轮询按下沿，仍然不装全局钩子。

---

## 17. 打包与图标（v0.3）

- **单文件自包含**：`-r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true`，产物 66.8MB，目标机器无需安装 .NET。已实测：复制到空目录后直接运行，菜单/图标/配置全部正常。
- **图标**：源图 1254×1254 带透明角，用 Pillow 生成 16/24/32/48/64/128/256 多尺寸 `orbit.ico`，写进 `ApplicationIcon`。
- **托盘图标**改为三级回退：`ExtractIconEx(自身 exe)` → 输出目录的 `orbit.ico` → 程序内绘制的圆环。这样单文件发布（旁边没有 .ico）也是正确图标，开发调试时也能换图。
- 已知限制：`System.Drawing.Icon` 无法解码 PNG 压缩的 ICO 帧（256 那帧），因此用 `ExtractIconEx` + `Icon.FromHandle` 校验；Windows Shell 本身支持 PNG 帧，Explorer 显示正常。

## 18. 参考

- Win32 桌面/窗口：`GetDesktopWindow`、`WindowFromPoint`、`GetClassName`、`SetWindowsHookEx`
  https://learn.microsoft.com/windows/win32/api/winuser/
- Shell 图标接口：`IShellItemImageFactory`、`SHGetFileInfo`、`SHGetImageList`
  https://learn.microsoft.com/windows/win32/api/shobjidl_core/nn-shobjidl_core-ishellitemimagefactory
- 已知文件夹：`SHGetKnownFolderPath` / `FOLDERID_Desktop` / `FOLDERID_PublicDesktop` / `FOLDERID_Recent`
  https://learn.microsoft.com/windows/win32/shell/knownfolderid
- 拖放：`DoDragDrop`、`CF_HDROP`、`Preferred DropEffect`
  https://learn.microsoft.com/windows/win32/api/ole2/nf-ole2-dodragdrop
- Windows Search OLE DB：`Search.CollatorDSO`
  https://learn.microsoft.com/windows/win32/search/windows-search
- 通知状态：`SHQueryUserNotificationState`
  https://learn.microsoft.com/windows/win32/api/shellapi/ne-shellapi-query_user_notification_state
- 高 DPI：`SetProcessDpiAwarenessContext` / Per-Monitor V2
  https://learn.microsoft.com/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows
```
