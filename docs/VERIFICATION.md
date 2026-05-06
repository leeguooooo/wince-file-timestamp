# SetTime CE 版验证计划

本文档用于验证 `SetTime.cs` 的 Windows CE / .NET Compact Framework 3.5 版本。验证分三层：

1. Mac/Linux 上的静态与逻辑验证：成本最低，主要发现语法错误和解析逻辑错误。
2. Windows 桌面 + VS2008/CF SDK/模拟器：确认能按 Compact Framework 目标编译，并能在 CE/Windows Mobile 环境启动。
3. 真实 CE 设备：最终确认目标路径、文件系统、权限和三个时间戳是否按预期生效。

注意：Mac/Linux 上的验证不能证明 .NET CF 3.5 兼容；Windows 桌面模拟器也不能完全替代真实设备。最终结论必须以真机验证为准。

## 1. 静态 / Mac-friendly 验证

### 1.1 先做基础文件检查

在仓库根目录运行：

```bash
cd .
grep -n "Console\|SetCreationTime\|SetLastAccessTime\|SetLastWriteTime\|SearchOption\|File.ReadAllBytes\|System.Linq" SetTime.cs || true
grep -n "Directory.Exists(path)" -A5 -B5 SetTime.cs
grep -n "ContainsRecursiveWildcard(path)" -A12 SetTime.cs
python3 - <<'PY'
import base64
from pathlib import Path
raw = Path("settime_sample.txt").read_text().strip()
print(base64.b64decode(raw).decode("utf-8"))
PY
```

应确认：

- 没有 `Console`、`File.SetCreationTime`、`File.SetLastAccessTime`、`File.SetLastWriteTime`、`SearchOption`、`File.ReadAllBytes`、`System.Linq`。
- `Directory.Exists(path)` 分支使用单层枚举，例如 `AddMatchingFiles(path, "*", false, rule.Files)`。
- `ContainsRecursiveWildcard(path)` 分支仍调用 `GetFilesRecursive(...)`。
- `settime_sample.txt` 能解码，第一行是 `settime`。

### 1.2 用 Mono 做语法烟测

如果 Mac/Linux 已安装 Mono，可以运行：

```bash
cd .
mono --version
mcs -target:winexe \
  -r:System.Windows.Forms.dll \
  -r:System.Drawing.dll \
  -out:/tmp/SetTime.mono.syntax.exe \
  SetTime.cs
```

这个命令的价值：

- 能发现 C# 语法错误、缺少 `using`、明显类型错误。
- 能验证 `System.Windows.Forms.MessageBox`、`DllImport`、`IntPtr` 等桌面 Mono 也认识的基础形状。

限制：

- Mono 编译出来的是桌面 CLR 程序，不是 .NET Compact Framework 程序。
- 它不能证明 `SetTime.cs` 只使用了 CF 3.5 子集 API。
- 不要在 Mac/Linux 上运行这个 exe；`coredll.dll` 是 CE DLL，运行会失败。

### 1.3 用 .NET 6/7/8 做语法烟测

.NET SDK 在 Mac/Linux 上不能直接编译 .NET Compact Framework 3.5，也不能直接引用 CE 的 `mscorlib.dll`。如果只想用 Roslyn 再做一层语法检查，可以建临时 Windows Forms 项目：

```bash
cd .
rm -rf /tmp/settime-syntax
mkdir -p /tmp/settime-syntax
cp SetTime.cs /tmp/settime-syntax/Program.cs
cat >/tmp/settime-syntax/SetTimeSyntax.csproj <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net6.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <EnableWindowsTargeting>true</EnableWindowsTargeting>
    <ImplicitUsings>false</ImplicitUsings>
    <Nullable>disable</Nullable>
  </PropertyGroup>
</Project>
EOF
dotnet build /tmp/settime-syntax/SetTimeSyntax.csproj
```

这个检查也只是语法/桌面 API 烟测。它使用现代 Windows Desktop 参考程序集，不能证明 CF 3.5 兼容。若机器没有 Windows Desktop reference pack，`dotnet restore/build` 可能需要联网下载包。

### 1.4 Python 单元测试计划（不需要设备）

建议在后续单独新增 Python 测试文件时，镜像 C# 的纯逻辑，不直接调用 CE API。测试结构可以按函数分组：

#### base64 解码与 magic 行

测试用例：

- 输入 `settime\n/硬盘/test/a.dat,2025-01-01 00:00:00\n` 的 UTF-8 Base64，断言解码成功。
- Base64 文本中插入换行、空格、Tab，断言去空白后仍可解码。
- 第一行是 `SETTIME`、` settime `，断言 magic 检查通过。
- 第一行是 `config` 或空文件，断言配置错误。

#### 行解析

测试用例：

- `/硬盘/test/sample.txt,2025-01-15 10:30:00` 解析为 path + datetime，step 为 0。
- `/硬盘/test/*.dat,2025-03-01 08:00:00,+30s` 解析为 path + datetime + 30 秒 step。
- 路径中不含逗号时，只按第一个逗号切分；datetime 和 step 在剩余部分继续解析。
- 缺少逗号、空 path、空 datetime、datetime 格式错误时断言失败。

#### step 解析

测试用例：

- `+30s` 得到 30 秒。
- `+5m` 得到 5 分钟。
- `+1h` 得到 1 小时。
- `+2d` 得到 2 天。
- `30s`、`-30s`、`+0s`、`+xs`、`+1w` 都失败。

#### 通配符与递归区别

用临时目录构造：

```text
root/
  a.dat
  b.txt
  sub/
    c.dat
```

测试用例：

- config path 是 `root` 裸目录时，只匹配 `root/a.dat` 和 `root/b.txt`，不包含 `root/sub/c.dat`。
- config path 是 `root/*.dat` 时，只匹配 `root/a.dat`，不包含 `root/sub/c.dat`。
- config path 是 `root/**/*.dat` 时，匹配 `root/a.dat` 和 `root/sub/c.dat`。
- 匹配结果按文件名排序，step 按排序后的下标递增。

#### MakeAbsolutePath 行为

测试用例：

- `/硬盘/test/a.dat` 保持绝对路径。
- `test/a.dat` 在 config 所在目录 `/硬盘/SetTime` 下变成 `/硬盘/SetTime/test/a.dat`。
- 路径分隔符 `/` 和 `\` 统一处理。

#### WildcardMatch 行为

测试用例：

- `*.dat` 匹配 `a.dat`，不匹配 `a.txt`。
- `a?.dat` 匹配 `a1.dat`，不匹配 `a12.dat`。
- 大小写不敏感：`*.DAT` 匹配 `a.dat`。
- `*` 匹配任意文件名。

### 1.5 PEVerify / ILVerify 是否可用

`peverify` 是 .NET Framework SDK / Visual Studio 附带的 IL 与 metadata 验证工具，适合在 Windows 上对已编译的程序集做验证。它不是 CF 编译器，也不能替代设备运行验证。

可用方式：

```bat
peverify SetTime.exe /md /il /verbose /nologo /unique
```

如果 `/il` 因为 Compact Framework 引用解析失败，可以先退到 metadata 检查：

```bat
peverify SetTime.exe /md /nologo /unique
```

`ILVerify` 是较新的 .NET 工具，主要面向 CoreCLR/.NET 现代运行时。可以尝试：

```bat
dotnet tool install -g dotnet-ilverify
ilverify SetTime.exe -r "%CFSDK%\mscorlib.dll" -r "%CFSDK%\System.dll" -r "%CFSDK%\System.Windows.Forms.dll" -r "%CFSDK%\System.Drawing.dll"
```

但对 .NET Compact Framework 3.5 程序，`ILVerify` 结果只能作为参考；它不了解 CE 运行时全部差异。最可靠的编译验证仍是 CF SDK 的 `csc.exe`，最可靠的运行验证仍是真机。

## 2. Windows 桌面验证（无 CE 设备）

### 2.1 安装组件

推荐使用 Windows XP SP3 / Windows 7 32-bit 虚拟机，或一台能正常运行 VS2008 旧工具链的 Windows PC。

安装顺序建议：

1. Visual Studio 2008 Professional Edition 或更高版本；Express 版不适合 Smart Device 开发。
2. Visual Studio 2008 SP1。
3. .NET Compact Framework 3.5 SDK / 设备 SDK。如果目标只是通用 Windows Mobile 6 模拟器，安装 `Windows Mobile 6 Professional SDK Refresh`；该 SDK 包含文档、示例、头文件、库、模拟器镜像和工具。
4. Microsoft Device Emulator 3.0（若 VS2008/SDK 未自动安装）。
5. ActiveSync 4.5（Windows XP），或 Windows Mobile Device Center / WMDC（Vista/Windows 7）。

关键组件名：

- `Visual Studio 2008 Professional Edition`
- `Visual Studio 2008 SP1`
- `Windows Mobile 6 Professional SDK Refresh`
- `Windows Mobile 6 Standard SDK Refresh`（如果需要 Standard 镜像）
- `Microsoft Device Emulator 3.0`
- `ActiveSync 4.5`
- `Windows Mobile Device Center`

参考资料：Microsoft 的 Windows Mobile 6 SDK Refresh 下载页说明该 SDK 会向 Visual Studio 添加文档、示例、库、模拟器镜像和工具；Microsoft 的模拟器文档说明可通过 Device Emulator Manager 连接并 cradle 模拟器。

### 2.2 用 CF 3.5 SDK 编译

在 Windows 上打开 VS2008 Command Prompt 或普通 `cmd.exe`：

```bat
cd /d C:\path\to\cep
notepad build_settime_ce.bat
```

确认 `CFSDK` 指向实际 Compact Framework 参考程序集目录，例如：

```bat
set CFSDK=C:\Program Files\Microsoft.NET\SDK\CompactFramework\v3.5\WindowsCE
```

然后运行：

```bat
build_settime_ce.bat
```

期望：

- 生成 `SetTime.exe`。
- 无 C# 编译错误。
- 如果提示找不到 `mscorlib.dll`、`System.dll`、`System.Windows.Forms.dll`、`System.Drawing.dll`，说明 `CFSDK` 路径不对或 CF SDK 未安装。

### 2.3 在 Windows Mobile 6 / WinCE 模拟器运行

可以。只要模拟器镜像包含 .NET CF 3.5 或能部署 CF 3.5 runtime，就可以先在模拟器中运行 `SetTime.exe`。步骤：

1. 打开 Visual Studio 2008。
2. 菜单进入 `Tools` -> `Device Emulator Manager`。
3. 在列表中选择一个 Windows Mobile 6 Classic/Professional 或目标 CE SDK 的模拟器。
4. 右键选择 `Connect`，等待模拟器窗口启动。
5. 启动 ActiveSync 或 WMDC。
6. 回到 Device Emulator Manager，对同一个模拟器右键选择 `Cradle`。
7. ActiveSync/WMDC 连接成功后，PC 上应能浏览模拟器文件系统。

如果不使用 VS2008，也可以安装 `Microsoft Device Emulator 3.0` 和对应 emulator images，然后运行 `dvcemumanager.exe`。

### 2.4 复制文件到模拟器

可选方式：

- 通过 ActiveSync/WMDC 的文件浏览，把 `SetTime.exe`、`settime.ini`、测试目标文件复制到模拟器目录。
- 在 Device Emulator Manager 中连接和 cradle 后，通过同步软件浏览设备文件系统。
- 部分模拟器支持共享文件夹或拖放；如果拖放不可用，优先用 ActiveSync/WMDC。

建议目录：

```text
\My Documents\SetTime\
```

模拟器中没有 `/硬盘` 时，先改测试配置为模拟器存在的路径，例如：

```text
settime
\My Documents\SetTime\sample.txt,2025-01-15 10:30:00
\My Documents\SetTime\*.dat,2025-03-01 08:00:00,+30s
```

再用 PowerShell 重新生成 Base64 配置。

### 2.5 观察 MessageBox 输出

`SetTime.exe` 是 `/target:winexe`，不显示 Console。运行结束后会弹出 MessageBox，内容包括：

- `Config: ...`
- `Success: N`
- `Failed: M`
- 前几条失败原因

在模拟器中双击 exe，或从文件管理器中打开。若没有弹窗：

- 确认模拟器已安装 .NET CF 3.5 runtime。
- 确认 exe 与配置文件路径正确。
- 用显式参数启动时，确认启动方式真的传入了 config 路径；双击模式更适合先验证自动扫描。

## 3. On-device CE 最终确认

### 3.1 复制 exe 到设备

可选方式：

- ActiveSync / WMDC：连接设备后，从 PC 复制 `SetTime.exe` 和 Base64 配置文件到设备目录。
- SD 卡 / CF 卡：把文件复制到卡上，再在设备文件管理器中复制到 `/硬盘/SetTime/` 或直接从卡上运行。
- FTP：如果设备运行 FTP server，可通过 FTP 上传。
- SMB/共享目录：如果 CE 镜像支持网络共享，也可通过局域网复制。

建议部署目录：

```text
/硬盘/SetTime/SetTime.exe
/硬盘/SetTime/settime.ini
```

目标测试文件：

```text
/硬盘/test/sample.txt
/硬盘/test/a.dat
/硬盘/test/b.dat
/硬盘/test/sub/c.dat
```

用于验证递归开关：

- 裸目录 `/硬盘/test` 应只处理 `sample.txt`、`a.dat`、`b.dat` 等直接子文件，不处理 `/硬盘/test/sub/c.dat`。
- `/硬盘/test/*.dat` 应只处理直接子目录下的 `.dat`。
- `/硬盘/test/**/*.dat` 才应处理子目录里的 `.dat`。

### 3.2 运行方式

显式配置：

```text
SetTime.exe /硬盘/SetTime/settime.ini
```

或双击运行：

```text
/硬盘/SetTime/SetTime.exe
```

双击时程序会扫描 exe 同目录所有文件，找到 Base64 解码后第一行是 `settime` 的文件。

### 3.3 不写验证 exe 时如何看三个时间戳

优先级从高到低：

1. 设备自带文件管理器的属性页：查看是否能显示 Created / Modified / Accessed。很多 CE 文件管理器只显示 Modified，因此不一定够。
2. CE `cmd.exe`：先运行 `dir /?` 看是否支持 `/T`。
3. VS2008 Remote Tools / Remote File Viewer：连接设备后查看远程文件属性。不同 SDK/设备暴露的列不同，至少通常能看 Modified。
4. 第三方 CE 文件管理工具：例如设备厂商自带维护工具、Total Commander CE、Resco Explorer 等，查看文件属性页是否显示三个时间字段。
5. FTP server 的 `MDTM` 或目录列表：通常只能验证 Modified，不能验证 Created/Accessed。

### 3.4 CE cmd.exe 的 `dir /TC`、`/TW`、`/TA`

桌面 Windows 的 `dir` 支持 `/T:C`（创建时间）、`/T:W`（写入时间）、`/T:A`（访问时间）。但 Windows CE 的 `cmd.exe` 由镜像裁剪决定，不同设备差异很大，不能假设一定支持。

在设备上先执行：

```text
cmd.exe
dir /?
```

如果帮助里有 `/T`，再试：

```text
dir "\硬盘\test\sample.txt" /T:C
dir "\硬盘\test\sample.txt" /T:W
dir "\硬盘\test\sample.txt" /T:A
```

也可以试无冒号写法：

```text
dir "\硬盘\test\sample.txt" /TC
dir "\硬盘\test\sample.txt" /TW
dir "\硬盘\test\sample.txt" /TA
```

判定：

- `/T:C` 或 `/TC` 显示目标时间：创建时间验证通过。
- `/T:W` 或 `/TW` 显示目标时间：写入时间验证通过。
- `/T:A` 或 `/TA` 显示目标时间：访问时间验证通过。
- 如果提示 invalid switch 或帮助里没有 `/T`，说明该 CE 镜像的 `dir` 不支持此验证方式。

如果 `dir` 不支持 `/T`，不要用 `dir` 的默认输出证明三个时间戳；默认输出通常只能代表写入/修改时间。

### 3.5 ActiveSync 拷回 PC 时如何避免时间戳被覆盖

不要把“直接从设备复制文件到 PC 后在 Windows Explorer 查看属性”作为唯一证据。ActiveSync/WMDC、FTP、资源管理器复制都可能改变目标文件在 PC 上的创建时间，甚至访问时间。

可选缓解：

- 在设备端先用压缩工具把目标文件打包成 zip，再把 zip 拷回 PC。zip 通常能保留文件的 modified/write time，适合验证 LastWriteTime；但普通 zip 不可靠地保存 Created/Accessed。
- 如果设备上有 FTP server，用 FTP 的目录列表或 `MDTM` 检查 modified time；这仍不能覆盖 Created/Accessed。
- 对 Created/Accessed 的最终确认，优先在设备端完成：文件属性页、CE `dir /T`、Remote File Viewer 或第三方 CE 文件管理器。

### 3.6 最终验收矩阵

建议记录以下用例结果：

| 用例 | 配置路径 | 期望 |
| --- | --- | --- |
| 单文件 | `/硬盘/test/sample.txt` | 只修改该文件，三个时间戳相同 |
| 单层通配符 | `/硬盘/test/*.dat` | 只修改 `/硬盘/test` 直接子文件中的 `.dat` |
| 裸目录 | `/硬盘/test` | 只修改直接子文件，不进入 `sub` |
| 显式递归 | `/硬盘/test/**/*.dat` | 修改直接子文件和子目录中的 `.dat` |
| step | `+30s` | 排序后的第 0 个文件为起始时间，第 1 个 +30 秒，第 2 个 +60 秒 |
| 只读/隐藏/系统属性 | 设置属性后运行 | 程序可修改时间戳，并在结束后恢复原属性 |
| 错误路径 | 不存在文件 | MessageBox 显示失败数量和失败原因 |

每个用例至少记录：

- 配置明文。
- Base64 配置文件名。
- MessageBox 成功/失败数量。
- 验证方式（文件属性、`dir /T`、Remote File Viewer、第三方工具）。
- 三个时间戳的实际值或验证限制说明。

## 4. 参考资料

- Microsoft Learn: `PEVerify` 工具说明：<https://learn.microsoft.com/en-us/dotnet/framework/tools/peverify-exe-peverify-tool>
- Microsoft Download Center: Windows Mobile 6 Professional and Standard SDK Refresh：<https://www.microsoft.com/en-gb/download/details.aspx?id=6135>
- Microsoft Learn: 设置 Mobile Device Emulator：<https://learn.microsoft.com/en-us/previous-versions/office/developer/sharepoint-2010/ee535525(v=office.14)>
- Microsoft Learn: 桌面 Windows `dir` 命令的 `/T` 语义：<https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/dir>
