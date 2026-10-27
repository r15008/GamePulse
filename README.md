# GamePulse

面向《无畏契约》等竞技游戏的 Windows 性能控制中心。原生中文桌面程序，显示真实采集数据。

**作者：d2STR0Y**  
**Instagram：[0724stephen](https://www.instagram.com/0724stephen/)**  
**微信 ID：QouC6_11**

## 下载和运行

- [下载最新版本及便携包](https://github.com/r15008/GamePulse/releases/latest)
- [直接下载 GamePulse.exe](https://github.com/r15008/GamePulse/raw/refs/heads/main/downloads/GamePulse.exe)
- Windows 10 / 11 **64 位**，需要 .NET Framework 4.8（现代 Windows 通常已有）。
- 双击 EXE 即可运行。若游戏帧采集提示权限不足，使用程序“设置与连接 → 以管理员身份重启”。不需要安装整套 PresentMon。
- 程序目前未进行商业代码签名；仓库和发布页提供 SHA-256 校验值。

## 已实现

| 页面 | 功能 |
| --- | --- |
| 实时监控 | CPU 占用、GPU 占用、物理内存占用、可用温度、真实 FPS、帧时间、1% Low、指定目标 ICMP 延迟、趋势图、独立悬浮窗 |
| 硬件信息 | 电脑型号、Windows 版本、CPU、主板、每条内存与总量、显卡与驱动、显示器与显示模式、声卡、网卡、BIOS、运行时间；可导出文本 |
| 性能方案 | 切换本机已有 Windows 电源计划；保存首次切换前的原计划，支持跨重启恢复 |
| 超频调校 | 自动发现／手动连接 Ryzen Master、Intel XTU、NVIDIA App、AMD Software；打开官方自动或手动调校功能；加载用户在 MSI Afterburner 中保存且自行验证的配置槽 |
| 测试记录 | 本地 JSON 记录、平均 FPS、1% Low、P99、帧时间尖峰、负载线索、最近两次比较、系统采样与逐帧 CSV 导出 |
| 设置与连接 | 网络目标、调校工具路径、温度来源、采集权限、作者与联系方式 |

## 使用流程

1. 启动游戏，进入稳定的测试场景。
2. 在实时监控页刷新进程列表，选择游戏的实际渲染进程，例如 `VALORANT-Win64-Shipping.exe`。
3. 点击“开始采集”，等待真实帧数据出现，再点击“开始记录”。
4. 完成后点击“结束并保存”，进入测试记录查看统计或导出 CSV。
5. 使用相同场景、画质、帧率上限和时长进行前后对比。

不采集游戏也可以记录系统负载，这类记录会标明没有游戏帧数据。单次记录最多 30 分钟或 200 万帧；达到限制会自动保存。关闭程序时会保存正在记录的测试。

## 数据来源与限制

- **CPU**：Windows `GetSystemTimes`；系统总利用率不能代表单个核心是否成为瓶颈。
- **NVIDIA GPU**：驱动自带 NVML，优先读取第一张 NVIDIA GPU 的占用、核心温度和功耗。NVML 不可用时，使用 Windows GPU 3D 引擎计数器，取各 GPU 引擎汇总值中的最大值。不同来源的占用口径可能不同。
- **内存**：Windows 物理内存统计和 WMI。DDR 配置速率标为 MT/s；固件没有提供的项目保留为不可用。
- **CPU / AMD GPU 温度**：读取本机 `root\LibreHardwareMonitor` 或 `root\OpenHardwareMonitor` WMI 服务。可使用带 WMI 的 LibreHardwareMonitor（如 [v0.9.4](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/tag/v0.9.4)）；以管理员身份运行并保持开启，WMI 提供程序随程序启动。不同硬件、工具版本和驱动的支持不一样，新版本可能移除 WMI。CPU 显示最高可用 CPU 温度；不支持时显示 `—`，不会伪造数值。GamePulse 不安装温度采集内核驱动。
- **FPS**：内置 Intel 签名的 [PresentMon 2.3.0](https://github.com/GameTechDev/PresentMon/releases/tag/v2.3.0)，通过 Windows ETW 采集指定进程，使用首个有效交换链。多交换链程序需要核对目标；统计为应用呈现帧节奏，并不保证等同于显示器实际显示帧率或包含驱动生成帧。
- **网络**：探测用户指定的 IP / 主机名；默认网络网关。它**不是游戏内 Ping**。目标阻止 ICMP 或探测失败时显示 `—`；成功回应比例不等于游戏丢包率。
- **悬浮窗**：独立置顶窗口，不注入游戏。独占全屏模式可能无法显示。
- **超频**：本版本**没有实现跨硬件的自主超频算法，也不直接写入频率、电压、内存时序或 BIOS**。自动／手动调校需在厂商工具内操作。Afterburner“加载配置”仅发送 `-Profile1` 至 `-Profile5` 请求，不会把发送成功当成调校成功。内存 XMP / EXPO 通常需进入主板 BIOS。
- 超频可能导致不稳定、数据或硬件损坏并影响保修；同型号硬件也不能保证同一频率稳定。记录分析提供线索，不能自动确认故障根因或保证超频稳定性。

## 统计口径

- 平均 FPS = `1000 / 所有有效帧时间的平均值`。
- 1% Low = `1000 / 最慢 ceil(帧数 × 1%) 帧的平均帧时间`。
- P99 使用最近秩百分位；实时数据窗口为最近 2 秒，报告使用完整记录。
- 帧时间尖峰阈值 = `max(25 ms, 全程中位帧时间 × 2)`，超过该值计为一次尖峰。
- 不足两秒的瞬时统计、菜单和切换场景均可能影响结果。

## 本地数据与隐私

设置和记录位于 `%LOCALAPPDATA%\GamePulse`，记录在 `records` 子目录，内置采集器按需解压到 `tools\presentmon-2.3.0` 并校验哈希。

应用不会自动上传硬件信息、进程列表或测试记录。网络探测只联系设置中的目标；打开官网或 GitHub 按钮会调用浏览器。源码仓库不包含开发机器的硬件报告、IP、测试记录或个人配置。

## 从源码构建

使用 Windows PowerShell 或 PowerShell 7，Windows .NET Framework 4.8 自带编译器即可，不依赖 Node / Python / .NET SDK：

```powershell
.\build.ps1
.\dist\GamePulse.exe
```

`vendor/PresentMon.exe` 来自 Intel 官方发布，SHA-256：

```text
690533CF5C2591EF571A3037AA4C205C8E75860EF3F56927BF3A98ACF7198485
```

验证：

```powershell
Start-Process .\dist\GamePulse.exe -ArgumentList '--self-test', "$PWD\checks.txt" -Wait
# 可选：真实 ETW 集成测试，显示约 13 秒的动画测试窗口；建议管理员运行。
Start-Process .\dist\GamePulse.exe -ArgumentList '--capture-test', "$PWD\capture-check.json" -Wait
```

开发时完成了 18 项逻辑检查、真实硬件读取、真实 ETW 帧采集和六页面渲染检查。没有进行实际硬件超频或《无畏契约》游戏内长期稳定性认证。

## 第三方组件

PresentMon 的 MIT 许可见 [vendor/PresentMon-LICENSE.txt](vendor/PresentMon-LICENSE.txt)。GamePulse 本身版权归 d2STR0Y 所有；仓库公开不代表额外授予本项目许可。
