# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] — 2026-05-06

### Added

- Initial public release.
- GUI mode: directory + filename filter, recurse, three independent timestamp checkboxes (creation / last write / last access), tolerant date parser (English + Chinese).
- Batch mode: Base64-encoded `settime` config with wildcard expansion (`*`, `**`) and `--dry-run` preview.
- Dual build via `WIN10_DESKTOP` preprocessor symbol:
  - `build_settime_ce.bat` → `SetTime.exe` for Windows CE / .NET Compact Framework 3.5 (P/Invoke `coredll.dll`).
  - `build_settime_win10.bat` → `SetTime_Desktop.exe` for Windows 10 desktop testing (P/Invoke `kernel32.dll`, `[STAThread]`).
- P/Invoke `SetFileTime` with `IntPtr` parameters so any subset of the three timestamps can be skipped via `IntPtr.Zero`.
- Read-only / hidden / system attributes are temporarily cleared and restored around each modification.
- 16-tag GitHub topic set for SEO.
- Full English README plus brief 中文 summary.
- Build guide covering `csc.exe` + extraction of CF 3.5 reference assemblies from a VS2008 Pro ISO via `msiexec /a`.
- Three-tier verification plan (static / desktop emulator / on-device).

### Notes

- Tested:
  - Compiles on Windows 10 + .NET Framework 3.5 with CF 3.5 reference assemblies extracted from VS2008 Pro ISO.
  - Static metadata verified (CF 3.5 public key token `969DB8053D3322AC`, PE machine `0x014C` / i386).
  - GUI logic exercised on Windows 10 desktop test build.
- Pending field validation on real Windows CE devices.
