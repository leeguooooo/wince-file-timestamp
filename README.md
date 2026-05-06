# WinCE File Timestamp Tool

> Modify file **creation**, **last write**, and **last access** timestamps on
> **Windows CE** / **Windows Mobile** devices running **.NET Compact Framework 3.5**.
> A `touch`-equivalent for embedded Windows. GUI + CLI.

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Language: C#](https://img.shields.io/badge/language-C%23-178600.svg)](SetTime.cs)
[![Target: .NET CF 3.5](https://img.shields.io/badge/target-.NET%20CF%203.5-512bd4.svg)](https://learn.microsoft.com/en-us/previous-versions/windows/embedded/cc500517(v=msdn.10))
[![Platform: WinCE](https://img.shields.io/badge/platform-Windows%20CE%20%7C%20Mobile-0078d4.svg)](#)

A small, dependency-free C# tool to change file timestamps on Windows CE devices.
Originally built for industrial PM2.5 monitoring devices (CEP series), but works on
any Windows CE 5.x / 6.x / Windows Mobile 5/6 device with .NET Compact Framework 3.5.

## Why

Windows CE / Windows Mobile devices rarely ship with a `touch`-equivalent utility,
and the .NET Compact Framework `File` API does **not reliably expose**
`SetCreationTime` / `SetLastAccessTime` (only `SetLastWriteTime` is consistent).

This tool calls the underlying Win32 API `SetFileTime` via P/Invoke into
`coredll.dll` (CE) or `kernel32.dll` (desktop test build), which lets you set
all three timestamps independently and reliably.

Use cases:

- **Industrial PDAs / HMIs / Pocket PCs** — fix data file timestamps after a clock issue
- **Battery-died devices** — restore correct timestamps after RTC reset
- **Pre-production normalization** — set deterministic timestamps on golden images
- **Air quality / environmental monitors** — correct device-side data file metadata
- **Forensic / archival** — controlled backdating in audited workflows

## Features

- ✅ Modify creation / last write / last access **independently** (each can be skipped)
- ✅ **GUI mode** — double-click on the device, no command line needed
- ✅ **Batch mode** — Base64-encoded config, deployable via SD card / FTP / over-the-air
- ✅ **Tolerant Chinese-friendly date parsing** — `2025-01-15 10:30`, `2025年1月15日`, `2025/1/15`, etc.
- ✅ **Wildcard expansion** — `*.dat`, `**/*.txt` recursive globs
- ✅ **Dry-run preview** in batch mode
- ✅ **Single 32 KB exe**, no installer, no runtime download (CF 3.5 is shipped on most CE images)
- ✅ **Dual build** — CE binary for production, desktop test binary for verifying logic on Win10

## Quick Start

### 1. Build

On a Windows machine with **.NET Framework 3.5** enabled and the
**.NET Compact Framework 3.5 reference assemblies** available
(typically from a Visual Studio 2008 Pro install — see [docs/BUILD_GUIDE.md](docs/BUILD_GUIDE.md)):

```bat
build_settime_ce.bat
```

Output: `SetTime.exe` (~32 KB).

### 2. Deploy

Copy `SetTime.exe` to the device (e.g. `\Storage Card\SetTime\` or `\硬盘\SetTime\`).

### 3. Use

**GUI mode** (recommended for end-users):

Double-click `SetTime.exe` on the device. A window appears with:

- Directory picker (Browse button uses `OpenFileDialog`)
- Filename filter (e.g. `*.dat`)
- "Include subdirectories" checkbox
- Target time input (accepts many formats)
- Three checkboxes: ☑ Creation / ☑ Last Write / ☑ Last Access
- Confirm dialog before applying

**Batch mode** (for automation):

Provide a Base64-encoded config:

```
settime
\Storage Card\data\sample.txt,2025-01-15 10:30:00
\Storage Card\data\*.dat,2025-03-01 08:00:00,+30s
```

Encode it (PowerShell):

```powershell
[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes((Get-Content .\config.txt -Raw))) | Set-Content .\settime.ini -Encoding ASCII
```

Then run:

```
SetTime.exe settime.ini
SetTime.exe settime.ini --dry-run    # preview without modifying
```

See [`settime_sample.txt`](settime_sample.txt) for a working example.

## Date Format Support (GUI Mode)

The GUI input accepts (case-insensitive, whitespace-tolerant):

```
2025-01-15 10:30:00
2025-01-15 10:30
2025-1-15 10:30
2025/01/15 10:30:00
2025/1/15
2025-01-15
2025年1月15日 10:30
2025年1月15日
2025年1月15日 10点30分
```

If only a date is given, time defaults to `00:00:00`.

## Builds

| Build | Output | Target | Use |
|---|---|---|---|
| `build_settime_ce.bat` | `SetTime.exe` | .NET CF 3.5 | **Production — deploy to CE devices** |
| `build_settime_win10.bat` | `SetTime_Desktop.exe` | .NET Framework 3.5 desktop | Development verification on Windows 10 |

Both are produced from the **same `SetTime.cs`** via the `WIN10_DESKTOP` preprocessor symbol,
which switches:

- P/Invoke target: `coredll.dll` (CE) ↔ `kernel32.dll` (desktop)
- `[STAThread]` attribute on `Main` (required for desktop `MessageBox`, absent on CF)
- Assembly path resolution: `Assembly.Location` (desktop) ↔ `GetName().CodeBase` (CF)

## How It Works

P/Invoke chain in [`SetTime.cs`](SetTime.cs):

```csharp
[DllImport("coredll.dll", EntryPoint = "CreateFileW", ...)]   // CE
// or "kernel32.dll" on desktop
private static extern IntPtr CreateFile(string fileName, uint access, ...);

[DllImport("coredll.dll", EntryPoint = "SetFileTime", ...)]
private static extern bool SetFileTime(
    IntPtr hFile,
    IntPtr lpCreationTime,    // IntPtr.Zero to skip
    IntPtr lpLastAccessTime,
    IntPtr lpLastWriteTime);

[DllImport("coredll.dll", EntryPoint = "CloseHandle", ...)]
private static extern bool CloseHandle(IntPtr handle);
```

Each timestamp pointer is allocated via `Marshal.AllocHGlobal` only when that
checkbox is ticked, allowing **independent modification of any subset** of the
three timestamps. `IntPtr.Zero` tells the Win32 API to leave that field unchanged.

Read-only / hidden / system attributes are temporarily cleared before
`SetFileTime` and restored after, so protected files can be modified without
permanently changing their attributes.

## CF 3.5 API Quirks Handled

If you're targeting CF 3.5 yourself, watch out for these (all addressed in this code):

| Desktop API | CF 3.5 alternative |
|---|---|
| `[STAThread]` on `Main` | Not supported; use plain `Main` |
| `File.GetAttributes` / `File.SetAttributes` | Use `FileInfo.Attributes` instead |
| `Application.ExecutablePath` | Use `Assembly.GetExecutingAssembly().GetName().CodeBase` |
| `Control.SetBounds(x, y, w, h)` | Use `Location = new Point(...)` and `Size = new Size(...)` |
| `MessageBox.Show(text, caption, buttons)` (3-arg) | Must include `MessageBoxIcon` and `MessageBoxDefaultButton` |
| `DateTime.TryParseExact` | Use `ParseExact` wrapped in `try/catch (FormatException)` |
| `Directory.GetFiles(path, pattern, AllDirectories)` | Manually recurse with `Directory.GetDirectories` |
| `File.ReadAllBytes` | Use `FileStream.Read` loop |
| Generic `List<T>` | Use `ArrayList` for portability across CF revisions |

## Documentation

- [BUILD_GUIDE.md](docs/BUILD_GUIDE.md) — extracting CF 3.5 reference assemblies from a VS2008 ISO without a full install
- [VERIFICATION.md](docs/VERIFICATION.md) — three-tier verification approach (static / desktop emulator / device)

## Limitations

- **No multi-file picker** in GUI mode — pick a directory + filter, or pick one file via `OpenFileDialog`. Multi-select on CF 3.5 `OpenFileDialog` is unreliable across CE images.
- **No relative date support** — `today`, `now`, etc. are not parsed (deliberately, to avoid timezone surprises).
- **Time zone**: timestamps are interpreted in the device's local time. Verify the device clock displays the expected time before running.
- **CE x86 target only** — recompile for ARM if needed (csc with `/platform:ARM` is not available; use `corflags` or rebuild with appropriate flags).

## Contributing

Issues and PRs welcome. Common asks:

- ARM build instructions
- Native C++ port (for CE images without .NET CF)
- More date formats (e.g. ISO 8601 with timezone)
- Localization beyond Chinese / English

## License

MIT — see [LICENSE](LICENSE).

---

## 中文简介

WinCE 文件时间戳修改工具——给跑 .NET Compact Framework 3.5 的 Windows CE / Windows Mobile 设备改文件的**创建时间、修改时间、访问时间**。最初为工业 PM2.5 监测设备开发，适用于任何带 CF 3.5 的 PocketPC / WM5 / WM6 / WinCE 5.x / WinCE 6.x 设备。

**功能**：

- 三个时间戳分别勾选（任选其一或全选）
- GUI 模式：设备上双击，零命令行
- 批处理模式：base64 编码配置，可走 SD 卡 / FTP / 远程下发
- 中文友好的日期解析（年月日点分秒，全角冒号都吃）
- 通配符 `*` `**`，支持递归
- 32 KB 单文件 exe，无依赖

**构建**：

需要 Windows 上的 **.NET Framework 3.5** + **.NET CF 3.5 引用程序集**（VS2008 Pro ISO 里有）。详见 [`docs/BUILD_GUIDE.md`](docs/BUILD_GUIDE.md)。

**部署**：把 `SetTime.exe` 拷到设备（如 `\硬盘\SetTime\`）双击运行。

**许可证**：MIT
