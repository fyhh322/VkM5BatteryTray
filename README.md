# VK M5 电量托盘

给 **VK M5（瓦尔基里 M5）无线鼠标**用的任务栏电量工具，参照 [InphicBatteryTray](https://github.com/Tan-TanZi/InphicBatteryTray) 的思路，但协议完全不同——VK M5 走的是 24G 接收器的 Feature Report。

托盘上直接显示电量数字：

- `97` = 97%
- 颜色分档：灰 `?` 未知 / 红 ≤10 / 橙 ≤30 / 黄 ≤60 / 绿 >60
- 鼠标休眠或未连接时显示 `?`

![托盘图标实际渲染，6 倍放大](icheck/taskbar_mock_x6.png)

上图为按托盘真实尺寸（20×20）渲染后 6 倍放大的效果，从左到右：未知、5%、20%、45%、80%、100%。

## 直接使用

双击 `VkM5BatteryTray.exe` 即可，图标出现在任务栏托盘区。

只支持 64 位 Windows，不需要装 .NET SDK（用的是系统自带的 .NET Framework 4.x）。**不需要**装厂商驱动也能读，但厂商的 `Valkyrie Driver` 同时开着也没问题。

右键菜单：

| 项 | 作用 |
| --- | --- |
| 显示详情 | 电量、在线状态、目标设备、原始帧，以及自启项的实际状态 |
| 立即刷新 | 立刻读一次 |
| 常驻任务栏（不进溢出区）　开/关 | 写 `NotifyIconSettings\<hash>\IsPromoted`，重启 explorer 后生效 |
| 开机自启　开/关 | 写 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` |
| 打开日志 | 同目录 `vk-m5-tray.log` |
| 退出 | 关闭 |

两个开关项右侧直接写明当前状态（`开` / `关`），左侧的勾选标记与之同步。每次展开菜单都会重新读一遍注册表，所以用命令行或任务管理器改过之后，菜单里的字样会立刻跟上，不用重启托盘。

自启项如果指向一个已经不存在的 exe（比如整个文件夹被挪走），菜单会把它显示成红色的 **`路径失效`**，勾选标记取消——因为这种状态下登录时它会静默失败，看起来跟"开着"一样，但其实什么都没做。

命令行参数 `--interval <毫秒>`（500–60000，默认 2000）。

### 开机自启

默认**首次运行就会自动注册**，不需要手动勾菜单。三种情况会自动修正：

1. **注册表项不存在**（新装、被清理软件删了）→ 启动时自动补上
2. **注册表项指向旧的 exe 路径**（挪了文件夹、改名）→ 启动时自动改成当前路径
3. **被任务管理器「启动」页禁用过**（`StartupApproved\Run` 里的残留标记会让写入的项依然不生效）→ 每次开关自启都会清掉该标记

只有在菜单里**主动取消勾选**后，程序才会记住"不要自启"（`HKCU\Software\VkM5BatteryTray` 下 `AutoStart=0`），之后不再自动补写。

不想开菜单也可以从命令行控制：

```bat
VkM5BatteryTray.exe --autostart on
VkM5BatteryTray.exe --autostart off
```

排查：右键「显示详情」最下面一段会列出注册表项的值、目标文件是否还存在、Explorer 是否记录了禁用状态。常见失效原因是路径变了（比如整个文件夹被移动），此时详情里会显示「目标文件：不存在！路径已失效」。

### 如果图标不出现

先看这个 exe 的完整性标签：

```bat
icacls VkM5BatteryTray.exe | findstr Mandatory
```

正常应该是 `Medium Mandatory Level`。如果显示 `Low Mandatory Level`，托盘会**静默丢弃**这个图标——不会报错，也不会有任何提示，进程照常在跑、日志照常在写，只是任务栏里什么都没有。修复：

```bat
icacls VkM5BatteryTray.exe /setintegritylevel Medium
```

`build.bat` 已经自动做了这一步。之所以要手动提，是因为本项目所在目录本身带 Low 标签（沙箱遗留），csc 编出来的 exe 会继承它。

## 设备与协议

VK M5 接收器是 USB 复合设备 `VID_374A & PID_A322`，电量数据在 **`mi_02`** 接口上，通过 Feature Report 交互：

```
\\?\hid#vid_374a&pid_a322&mi_02#...#{4d1e55b2-f16f-11cf-88cb-001111000030}
Usage Page 0xFFFF / Usage 0x0002 / FeatureReportByteLength = 65
```

读取一次 = `HidD_SetFeature(0xF7 请求帧)` → 等 12 ms → `HidD_GetFeature` 读回。实测回帧：

```
00 00 00 62 01 00 01 02 00 00 01 00 00 ...   (其余补 0)
```

字节含义（`resp[i]`）：

| 字节 | 含义 |
| --- | --- |
| `resp[2]` | 键盘电量 % |
| `resp[3]` | 鼠标电量 % |
| `resp[4]` | `0` = 键盘在线 |
| `resp[5]` | `0` = 鼠标在线 |
| `resp[6]` | canSend |
| `resp[7]` | 当前目标（1 键盘 / 2 鼠标 / 3 both） |
| `resp[9]` | rfBoot |

`resp[1]` 在厂商固件里是 canRead，但这台机器上恒为 0，**不能**拿它判断数据有效性；本工具用 `鼠标电量 > 0 || 键盘电量 > 0` 判断。

两个坑（已在代码里规避）：

- **不要发 `FC`**（厂商的 notice-read）。发下去之后 F7 会返回全零，约 3 秒才恢复。
- **不需要发 `F6`**（切换目标）。单鼠标场景下它不改变任何字节。

## 与厂商驱动共存

打开设备时用 `GENERIC_READ | GENERIC_WRITE` + `FILE_SHARE_READ | FILE_SHARE_WRITE`，可以和 `Valkyrie Driver` 同时运行，两边读数互不干扰。

## 图标为什么只有数字

托盘实际尺寸是 20×20（125% DPI，`SM_CXSMICON = 20`）。测试过三种形态：

- 32px 电池 + 内嵌数字：32px 好看，缩到 20px 数字糊成一团
- 纯竖条：可读但没有数值
- **纯数字 + 深色 halo 描边**：`? / 5 / 20 / 45 / 80 / 100` 全部清晰

所以最终按托盘真实像素尺寸直接渲染位图，再用 32bpp DIB 手工构造 `HICON`（`Bitmap.GetHicon()` 会丢 alpha，托盘上变黑方块）。三位数会按外框宽度等比缩放，保证不出界。

## 自己编译

双击 `build.bat` 即可，只依赖 .NET Framework 自带的 `csc.exe`，不需要 SDK。脚本会先关掉正在运行的实例再编译（csc 不能覆盖运行中的 exe）。

手动编译：

```bat
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /platform:x64 /optimize+ ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Core.dll ^
  /out:VkM5BatteryTray.exe src\VkM5BatteryTray.cs
```

无界面自检（打印 N 次读数后退出）：

```bat
selftest.exe 3
```

## 文件

| 路径 | 说明 |
| --- | --- |
| `VkM5BatteryTray.exe` | 成品，双击运行 |
| `build.bat` | 一键编译 |
| `src\VkM5BatteryTray.cs` | 全部实现（HID 枚举、协议、图标、托盘） |
| `tools\SelfTest.cs` | 无界面读数自检 |
| `tools\IconCheck.cs` | 按托盘真实尺寸渲染图标对比图 |
| `tools\HidProbe.cs` | HID 接口枚举 / Feature 读写探针 |
| `icheck\tray20_x6.png` | 图标效果（深色 / 浅色两行，6 倍放大） |
| `icheck\taskbar_mock_x6.png` | 模拟任务栏上的一排电量图标 |

`tools\` 下其余文件（`Caps.cs`、`F7Probe.cs`、`AutoStartCheck.cs`、`NProbe.cs`）是逆向协议时用的一次性探针，留着当参考。
