# Build Guide

This document describes how to build `SetTime.exe` (Windows CE / .NET Compact Framework 3.5) and `SetTime_Desktop.exe` (Windows 10 desktop test build) from `SetTime.cs`.

## Prerequisites

| Requirement | Why |
|---|---|
| Windows 10 / 11 (or Windows Server) | Host OS for `csc.exe` |
| .NET Framework 3.5 enabled | Provides `csc.exe` v3.5 |
| .NET Compact Framework 3.5 reference assemblies | Required for the CE build |

## Step 1: Enable .NET Framework 3.5

Windows 10 ships without .NET 3.5 by default but supports enabling it:

```powershell
# Run in elevated PowerShell
Enable-WindowsOptionalFeature -Online -FeatureName NetFx3 -All -NoRestart
```

Or via Control Panel → Programs → Turn Windows features on or off → check ".NET Framework 3.5 (includes .NET 2.0 and 3.0)".

Verify:

```cmd
dir C:\Windows\Microsoft.NET\Framework\v3.5\csc.exe
```

## Step 2: Obtain CF 3.5 Reference Assemblies

The four DLLs `mscorlib.dll`, `System.dll`, `System.Windows.Forms.dll`, `System.Drawing.dll` for **.NET Compact Framework 3.5** ship only with **Visual Studio 2008 Professional or higher** (VS2008 Standard does NOT include them; Express never did).

Installing the full VS2008 on Windows 10 often fails with **MSI error 1603** due to compatibility. The simplest workaround is to **extract** the embedded MSI without a full install.

### Option A — Extract from VS2008 Pro ISO (recommended)

1. Download the VS2008 Professional ISO (e.g. from `archive.org`)
2. Mount the ISO:
   ```powershell
   Mount-DiskImage -ImagePath C:\path\to\vs2008.iso
   ```
3. The CE/CF SDK installer is at:
   ```
   <ISO drive>:\WCU\NetCF\NetCFSetupv35.msi
   ```
4. Run an **administrative install** (extract without registering):
   ```powershell
   msiexec /a "<ISO drive>:\WCU\NetCF\NetCFSetupv35.msi" /qn TARGETDIR=C:\dev\cf35
   ```
5. The reference assemblies appear at:
   ```
   C:\dev\cf35\v3.5\WindowsCE\mscorlib.dll
   C:\dev\cf35\v3.5\WindowsCE\System.dll
   C:\dev\cf35\v3.5\WindowsCE\System.Windows.Forms.dll
   C:\dev\cf35\v3.5\WindowsCE\System.Drawing.dll
   ```

### Option B — VS2008 normal install

If you can install VS2008 Pro successfully, the assemblies will be at:

```
C:\Program Files (x86)\Microsoft.NET\SDK\CompactFramework\v3.5\WindowsCE\
```

## Step 3: Configure the Build Script

Edit `build_settime_ce.bat` and set `CFSDK` to the path containing the four reference DLLs from Step 2:

```bat
set CSC=%WINDIR%\Microsoft.NET\Framework\v3.5\csc.exe
set CFSDK=C:\dev\cf35\v3.5\WindowsCE
```

## Step 4: Build

### Production CE binary

```bat
build_settime_ce.bat
```

Output: `SetTime.exe` (~32 KB).

### Desktop test binary (for verifying logic on Windows 10)

```bat
build_settime_win10.bat
```

Output: `SetTime_Desktop.exe`. Defines `WIN10_DESKTOP` preprocessor symbol, which switches P/Invoke from `coredll.dll` to `kernel32.dll` and adds `[STAThread]` for `MessageBox`.

## Step 5: Verify the CE Binary

The CE binary cannot run on Windows 10 desktop directly (CF assemblies redirect to desktop CLR which lacks `coredll.dll`). To verify it's a valid CF 3.5 build:

```powershell
$asm = [System.Reflection.Assembly]::ReflectionOnlyLoadFrom("C:\path\to\SetTime.exe")
$asm.GetReferencedAssemblies() | Select-Object Name, Version, @{Name="Token"; Expression={[BitConverter]::ToString($_.GetPublicKeyToken())}}
```

Expected output:

```
Name                  Version  Token
mscorlib              3.5.0.0  96-9D-B8-05-3D-33-22-AC
System.Windows.Forms  3.5.0.0  96-9D-B8-05-3D-33-22-AC
```

The token `969DB8053D3322AC` is the **Compact Framework public key token**. The desktop framework uses `B77A5C561934E089` instead.

PE machine type:

```cmd
dumpbin /headers SetTime.exe | findstr machine
```

Expected: `14C` (i386 / x86), matching the architecture of most legacy CE devices.

## Step 6: Deploy

Copy `SetTime.exe` to the device via:

- ActiveSync / Windows Mobile Device Center (host PC ↔ device sync)
- SD card / CF card swap
- FTP, if the device runs an FTP server
- Network share, if the CE image supports SMB

A common deployment path on Chinese-OEM CE devices is `\硬盘\SetTime\`, on Pocket PCs `\Storage Card\SetTime\`.

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `csc.exe` not found | .NET 3.5 disabled | Enable via `Enable-WindowsOptionalFeature` (Step 1) |
| `error CS0006: 'mscorlib.dll'` | `CFSDK` path wrong | Re-check Step 2 path |
| `error CS0234: namespace not found` | Mixing CF and desktop refs | Ensure `/noconfig /nostdlib` in build script |
| MSI 1603 on full VS2008 install | Win10 / Win11 compatibility | Use Option A (admin install extract) instead |
| `dumpbin` not found | VS Build Tools not installed | Use `corflags` (similar info) or skip — not strictly required |
