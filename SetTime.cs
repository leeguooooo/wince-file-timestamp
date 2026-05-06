using System;
using System.Collections;
using System.Globalization;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

static class Program
{
    private const string MagicWord = "settime";
    private const string DateFormat = "yyyy-MM-dd HH:mm:ss";
    private const int MaxFilesPerRule = 10000;
    private const int MaxFailureMessages = 8;

    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

    private static readonly IntPtr InvalidHandleValue = new IntPtr(-1);
    private static bool isDryRun;
    private static bool modifyCreationTime = true;
    private static bool modifyLastWriteTime = true;
    private static bool modifyLastAccessTime = true;

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    private sealed class Rule
    {
        public string PathPattern;
        public DateTime StartTime;
        public TimeSpan Step;
        public ArrayList Files;
    }

    private sealed class PreviewItem
    {
        public string FilePath;
        public DateTime TargetTime;
    }

#if WIN10_DESKTOP
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Winapi)]
#else
    [DllImport("coredll.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Winapi)]
#endif
    private static extern IntPtr CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

#if WIN10_DESKTOP
    [DllImport("kernel32.dll", EntryPoint = "SetFileTime", SetLastError = true, CallingConvention = CallingConvention.Winapi)]
#else
    [DllImport("coredll.dll", EntryPoint = "SetFileTime", SetLastError = true, CallingConvention = CallingConvention.Winapi)]
#endif
    private static extern bool SetFileTime(
        IntPtr hFile,
        IntPtr lpCreationTime,
        IntPtr lpLastAccessTime,
        IntPtr lpLastWriteTime);

#if WIN10_DESKTOP
    [DllImport("kernel32.dll", EntryPoint = "CloseHandle", SetLastError = true, CallingConvention = CallingConvention.Winapi)]
#else
    [DllImport("coredll.dll", EntryPoint = "CloseHandle", SetLastError = true, CallingConvention = CallingConvention.Winapi)]
#endif
    private static extern bool CloseHandle(IntPtr hObject);

#if WIN10_DESKTOP
    [STAThread]
#endif
    static void Main(string[] args)
    {
        if (args == null || args.Length == 0)
        {
            Application.Run(new MainForm());
            return;
        }

        string configPath;
        string decodedConfig;
        string error;
        string[] configArgs = ParseArguments(args);

        if (!LoadConfig(configArgs, out configPath, out decodedConfig, out error))
        {
            ShowMessage("Config error:\r\n" + error);
            return;
        }

        ArrayList rules;
        string configDirectory = GetDirectoryName(configPath);
        if (!ParseConfig(decodedConfig, configDirectory, out rules, out error))
        {
            ShowMessage("Config error:\r\n" + error);
            return;
        }

        int successCount = 0;
        int failureCount = 0;
        StringBuilder failures = new StringBuilder();
        ArrayList previewItems = new ArrayList();

        for (int i = 0; i < rules.Count; i++)
        {
            Rule rule = (Rule)rules[i];
            if (!ExpandRule(rule, out error))
            {
                failureCount++;
                AppendFailure(failures, "Expand failed: " + rule.PathPattern + " : " + error);
                continue;
            }

            if (rule.Files.Count == 0)
            {
                failureCount++;
                AppendFailure(failures, "No files matched: " + rule.PathPattern);
                continue;
            }

            for (int fileIndex = 0; fileIndex < rule.Files.Count; fileIndex++)
            {
                string filePath = (string)rule.Files[fileIndex];
                DateTime targetTime = rule.StartTime.AddTicks(rule.Step.Ticks * fileIndex);

                if (isDryRun)
                {
                    try
                    {
                        FileAttributes _ = (new FileInfo(filePath)).Attributes;
                    }
                    catch (Exception ex)
                    {
                        failureCount++;
                        AppendFailure(failures, filePath + " : " + ex.Message);
                        continue;
                    }

                    PreviewItem previewItem = new PreviewItem();
                    previewItem.FilePath = filePath;
                    previewItem.TargetTime = targetTime;
                    previewItems.Add(previewItem);
                }

                if (SetAllFileTimes(filePath, targetTime, out error))
                {
                    successCount++;
                }
                else
                {
                    failureCount++;
                    AppendFailure(failures, filePath + " : " + error);
                }
            }
        }

        StringBuilder summary = new StringBuilder();
        if (isDryRun)
        {
            AppendDryRunSummary(summary, configPath, successCount, failureCount, previewItems);
        }
        else
        {
            summary.Append("Config: ");
            summary.Append(configPath);
            summary.Append("\r\nSuccess: ");
            summary.Append(successCount.ToString(CultureInfo.InvariantCulture));
            summary.Append("\r\nFailed: ");
            summary.Append(failureCount.ToString(CultureInfo.InvariantCulture));
        }

        if (failures.Length > 0)
        {
            summary.Append("\r\n\r\nFailures:\r\n");
            summary.Append(failures.ToString());
        }

        ShowMessage(summary.ToString());
    }

    private static string[] ParseArguments(string[] args)
    {
        if (args == null || args.Length == 0)
        {
            return args;
        }

        ArrayList configArgs = new ArrayList();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] != null && EqualsIgnoreCase(args[i].Trim(), "--dry-run"))
            {
                isDryRun = true;
            }
            else
            {
                configArgs.Add(args[i]);
            }
        }

        string[] result = new string[configArgs.Count];
        for (int i = 0; i < configArgs.Count; i++)
        {
            result[i] = (string)configArgs[i];
        }

        return result;
    }

    public static bool TryParseFlexibleDateTime(string text, out DateTime value)
    {
        value = DateTime.MinValue;

        if (text == null)
        {
            return false;
        }

        string trimmed = text.Trim();
        if (TryParseFlexibleDateTimeExact(trimmed, out value))
        {
            return true;
        }

        string normalized = NormalizeFlexibleDateTimeText(trimmed);
        return TryParseFlexibleDateTimeExact(normalized, out value);
    }

    private static bool TryParseFlexibleDateTimeExact(string text, out DateTime value)
    {
        string[] formats = new string[]
        {
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd HH:mm",
            "yyyy-M-d H:m",
            "yyyy/MM/dd HH:mm:ss",
            "yyyy/MM/dd HH:mm",
            "yyyy/M/d H:m",
            "yyyy-MM-dd",
            "yyyy/MM/dd",
            "yyyy-M-d",
            "yyyy/M/d"
        };

        for (int i = 0; i < formats.Length; i++)
        {
            try
            {
                value = DateTime.ParseExact(text, formats[i], CultureInfo.InvariantCulture);
                return true;
            }
            catch (FormatException)
            {
            }
        }

        value = DateTime.MinValue;
        return false;
    }

    private static string NormalizeFlexibleDateTimeText(string text)
    {
        string normalized = text.Replace('\uFF1A', ':');
        normalized = normalized.Replace('\u5E74', '-');
        normalized = normalized.Replace('\u6708', '-');
        normalized = normalized.Replace('\u65E5', ' ');
        normalized = normalized.Replace('\u70B9', ':');
        normalized = normalized.Replace('\u5206', ':');
        normalized = normalized.Replace("\u79D2", "");

        StringBuilder builder = new StringBuilder(normalized.Length);
        bool previousWasSpace = false;
        for (int i = 0; i < normalized.Length; i++)
        {
            char current = normalized[i];
            if (Char.IsWhiteSpace(current))
            {
                if (!previousWasSpace)
                {
                    builder.Append(' ');
                    previousWasSpace = true;
                }
            }
            else
            {
                builder.Append(current);
                previousWasSpace = false;
            }
        }

        string result = builder.ToString().Trim();
        while (result.EndsWith(":"))
        {
            result = result.Substring(0, result.Length - 1);
        }

        return result;
    }

    public static bool SetAllFileTimes(string filePath, DateTime targetTime)
    {
        string error;
        return SetAllFileTimes(filePath, targetTime, out error);
    }

    private static void AppendDryRunSummary(StringBuilder summary, string configPath, int planCount, int failureCount, ArrayList previewItems)
    {
        summary.Append("Config: ");
        summary.Append(configPath);
        summary.Append("\r\nDRY RUN \u2014 \u4E0D\u4F1A\u4FEE\u6539\u4EFB\u4F55\u6587\u4EF6");
        summary.Append("\r\n\u8BA1\u5212\u4FEE\u6539 ");
        summary.Append(planCount.ToString(CultureInfo.InvariantCulture));
        summary.Append(" \u4E2A\u6587\u4EF6\uFF0C\u524D ");

        int previewCount = previewItems.Count;
        if (previewCount > MaxFailureMessages)
        {
            previewCount = MaxFailureMessages;
        }

        summary.Append(previewCount.ToString(CultureInfo.InvariantCulture));
        summary.Append(" \u4E2A\uFF1A");

        for (int i = 0; i < previewCount; i++)
        {
            PreviewItem item = (PreviewItem)previewItems[i];
            summary.Append("\r\n- ");
            summary.Append(item.FilePath);
            summary.Append(" -> ");
            summary.Append(item.TargetTime.ToString(DateFormat, CultureInfo.InvariantCulture));
        }

        summary.Append("\r\nFailed: ");
        summary.Append(failureCount.ToString(CultureInfo.InvariantCulture));
    }

    private static bool LoadConfig(string[] args, out string configPath, out string decodedConfig, out string error)
    {
        configPath = null;
        decodedConfig = null;
        error = null;

        if (args != null && args.Length > 0 && args[0] != null && args[0].Trim().Length > 0)
        {
            configPath = NormalizePath(args[0].Trim());
            return TryDecodeConfigFile(configPath, out decodedConfig, out error) && HasMagic(decodedConfig, out error);
        }

        string exeDirectory = GetExecutableDirectory();
        string[] files;
        try
        {
            files = Directory.GetFiles(exeDirectory);
        }
        catch (Exception ex)
        {
            error = "Cannot scan exe directory: " + exeDirectory + "\r\n" + ex.Message;
            return false;
        }

        SortStrings(files);

        for (int i = 0; i < files.Length; i++)
        {
            string candidateText;
            string candidateError;
            if (TryDecodeConfigFile(files[i], out candidateText, out candidateError))
            {
                string magicError;
                if (HasMagic(candidateText, out magicError))
                {
                    configPath = files[i];
                    decodedConfig = candidateText;
                    return true;
                }
            }
        }

        error = "No base64 config with first decoded line 'settime' found in " + exeDirectory;
        return false;
    }

    private static bool TryDecodeConfigFile(string path, out string decodedConfig, out string error)
    {
        decodedConfig = null;
        error = null;

        try
        {
            byte[] bytes = ReadAllBytes(path);
            string encoded = Encoding.UTF8.GetString(bytes, 0, bytes.Length);
            string compact = StripWhitespace(encoded);
            byte[] decodedBytes = Convert.FromBase64String(compact);
            decodedConfig = Encoding.UTF8.GetString(decodedBytes, 0, decodedBytes.Length);
            return true;
        }
        catch (Exception ex)
        {
            error = path + " : " + ex.Message;
            return false;
        }
    }

    private static bool HasMagic(string decodedConfig, out string error)
    {
        error = null;
        string[] lines = SplitLines(decodedConfig);
        if (lines.Length == 0 || !EqualsIgnoreCase(CleanLine(lines[0]), MagicWord))
        {
            error = "First decoded line is not 'settime'.";
            return false;
        }

        return true;
    }

    private static bool ParseConfig(string decodedConfig, string configDirectory, out ArrayList rules, out string error)
    {
        rules = new ArrayList();
        error = null;

        string[] lines = SplitLines(decodedConfig);
        if (lines.Length == 0 || !EqualsIgnoreCase(CleanLine(lines[0]), MagicWord))
        {
            error = "First decoded line must be 'settime'.";
            return false;
        }

        for (int i = 1; i < lines.Length; i++)
        {
            string line = CleanLine(lines[i]);
            if (line.Length == 0)
            {
                continue;
            }

            int firstComma = line.IndexOf(',');
            if (firstComma < 0)
            {
                error = "Line " + (i + 1).ToString(CultureInfo.InvariantCulture) + " is missing comma.";
                return false;
            }

            string pathPart = line.Substring(0, firstComma).Trim();
            string remainder = line.Substring(firstComma + 1).Trim();
            if (pathPart.Length == 0 || remainder.Length == 0)
            {
                error = "Line " + (i + 1).ToString(CultureInfo.InvariantCulture) + " has empty path or datetime.";
                return false;
            }

            string[] fields = remainder.Split(',');
            if (fields.Length < 1 || fields.Length > 2)
            {
                error = "Line " + (i + 1).ToString(CultureInfo.InvariantCulture) + " must be path,datetime[,step].";
                return false;
            }

            DateTime startTime;
            if (!TryParseDate(fields[0].Trim(), out startTime))
            {
                error = "Line " + (i + 1).ToString(CultureInfo.InvariantCulture) + " datetime must be yyyy-MM-dd HH:mm:ss.";
                return false;
            }

            TimeSpan step = TimeSpan.Zero;
            if (fields.Length == 2 && !TryParseStep(fields[1].Trim(), out step))
            {
                error = "Line " + (i + 1).ToString(CultureInfo.InvariantCulture) + " step must be +Ns, +Nm, +Nh, or +Nd.";
                return false;
            }

            Rule rule = new Rule();
            rule.PathPattern = MakeAbsolutePath(pathPart, configDirectory);
            rule.StartTime = startTime;
            rule.Step = step;
            rule.Files = new ArrayList();
            rules.Add(rule);
        }

        if (rules.Count == 0)
        {
            error = "No file rules found after magic line.";
            return false;
        }

        return true;
    }

    private static bool ExpandRule(Rule rule, out string error)
    {
        error = null;
        rule.Files.Clear();

        string path = NormalizePath(rule.PathPattern);

        try
        {
            if (ContainsRecursiveWildcard(path))
            {
                string rootDirectory;
                string filePattern;
                if (!SplitRecursivePattern(path, out rootDirectory, out filePattern, out error))
                {
                    return false;
                }

                GetFilesRecursive(rootDirectory, filePattern, rule.Files);
            }
            else if (ContainsWildcard(path))
            {
                string directory = GetDirectoryName(path);
                string filePattern = Path.GetFileName(path);
                if (directory == null || directory.Length == 0)
                {
                    directory = GetExecutableDirectory();
                }

                AddMatchingFiles(directory, filePattern, false, rule.Files);
            }
            else if (Directory.Exists(path))
            {
                AddMatchingFiles(path, "*", false, rule.Files);
            }
            else
            {
                rule.Files.Add(path);
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        if (rule.Files.Count > MaxFilesPerRule)
        {
            error = "Matched " + rule.Files.Count.ToString(CultureInfo.InvariantCulture) +
                    " files; limit is " + MaxFilesPerRule.ToString(CultureInfo.InvariantCulture) + ".";
            return false;
        }

        SortArrayListStrings(rule.Files);
        return true;
    }

    private static void AddMatchingFiles(string directory, string pattern, bool recursive, ArrayList results)
    {
        if (pattern == null || pattern.Length == 0)
        {
            pattern = "*";
        }

        string[] files = Directory.GetFiles(directory);
        SortStrings(files);
        for (int i = 0; i < files.Length; i++)
        {
            string name = Path.GetFileName(files[i]);
            if (WildcardMatch(name, pattern))
            {
                results.Add(files[i]);
                if (results.Count > MaxFilesPerRule)
                {
                    return;
                }
            }
        }

        if (!recursive)
        {
            return;
        }

        string[] directories = Directory.GetDirectories(directory);
        SortStrings(directories);
        for (int i = 0; i < directories.Length; i++)
        {
            AddMatchingFiles(directories[i], pattern, true, results);
            if (results.Count > MaxFilesPerRule)
            {
                return;
            }
        }
    }

    private static void GetFilesRecursive(string directory, string pattern, ArrayList results)
    {
        AddMatchingFiles(directory, pattern, true, results);
    }

    private static bool SetAllFileTimes(string filePath, DateTime targetTime, out string error)
    {
        error = null;
        if (isDryRun)
        {
            return true;
        }

        FileAttributes originalAttributes = FileAttributes.Normal;
        bool haveAttributes = false;
        IntPtr handle = InvalidHandleValue;

        try
        {
            FileInfo info = new FileInfo(filePath);
            originalAttributes = info.Attributes;
            haveAttributes = true;

            FileAttributes writableAttributes = originalAttributes & ~(FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System);
            if (writableAttributes != originalAttributes)
            {
                info.Attributes = writableAttributes;
            }

            handle = CreateFile(
                filePath,
                GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_ATTRIBUTE_NORMAL,
                IntPtr.Zero);

            if (handle == InvalidHandleValue)
            {
                error = "CreateFileW failed, GetLastWin32Error=" + Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture);
                return false;
            }

            FILETIME fileTime = DateTimeToFileTime(targetTime);
            IntPtr pCreation = IntPtr.Zero;
            IntPtr pAccess = IntPtr.Zero;
            IntPtr pWrite = IntPtr.Zero;
            int sizeOfFiletime = Marshal.SizeOf(typeof(FILETIME));
            try
            {
                if (modifyCreationTime)
                {
                    pCreation = Marshal.AllocHGlobal(sizeOfFiletime);
                    Marshal.StructureToPtr(fileTime, pCreation, false);
                }
                if (modifyLastAccessTime)
                {
                    pAccess = Marshal.AllocHGlobal(sizeOfFiletime);
                    Marshal.StructureToPtr(fileTime, pAccess, false);
                }
                if (modifyLastWriteTime)
                {
                    pWrite = Marshal.AllocHGlobal(sizeOfFiletime);
                    Marshal.StructureToPtr(fileTime, pWrite, false);
                }
                if (!SetFileTime(handle, pCreation, pAccess, pWrite))
                {
                    error = "SetFileTime failed, GetLastWin32Error=" +
                            Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture);
                    return false;
                }
            }
            finally
            {
                if (pCreation != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(pCreation);
                }
                if (pAccess != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(pAccess);
                }
                if (pWrite != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(pWrite);
                }
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            if (handle != InvalidHandleValue)
            {
                CloseHandle(handle);
            }

            if (haveAttributes)
            {
                try
                {
                    (new FileInfo(filePath)).Attributes = originalAttributes;
                }
                catch (Exception ex)
                {
                    if (error == null)
                    {
                        error = "Failed to restore attributes: " + ex.Message;
                    }
                }
            }
        }

        return error == null;
    }

    private static FILETIME DateTimeToFileTime(DateTime value)
    {
        long fileTime = value.ToFileTime();
        FILETIME result = new FILETIME();
        result.dwLowDateTime = (uint)(fileTime & 0xFFFFFFFF);
        result.dwHighDateTime = (uint)(fileTime >> 32);
        return result;
    }

    private static bool SplitRecursivePattern(string path, out string rootDirectory, out string filePattern, out string error)
    {
        rootDirectory = null;
        filePattern = null;
        error = null;

        int marker = path.IndexOf("**");
        if (marker < 0)
        {
            error = "Missing ** marker.";
            return false;
        }

        rootDirectory = TrimTrailingSeparatorsPreserveRoot(path.Substring(0, marker));
        if (rootDirectory == null || rootDirectory.Length == 0)
        {
            rootDirectory = GetExecutableDirectory();
        }

        string after = path.Substring(marker + 2);
        after = TrimLeadingSeparators(after);
        if (after.Length == 0)
        {
            filePattern = "*";
            return true;
        }

        if (after.IndexOf('\\') >= 0)
        {
            error = "Only one file pattern is allowed after **.";
            return false;
        }

        filePattern = after;
        return true;
    }

    private static bool ContainsWildcard(string value)
    {
        return value.IndexOf('*') >= 0 || value.IndexOf('?') >= 0;
    }

    private static bool ContainsRecursiveWildcard(string value)
    {
        return value.IndexOf("**") >= 0;
    }

    private static bool WildcardMatch(string text, string pattern)
    {
        return WildcardMatchAt(text, 0, pattern, 0);
    }

    private static bool WildcardMatchAt(string text, int textIndex, string pattern, int patternIndex)
    {
        while (patternIndex < pattern.Length)
        {
            char patternChar = pattern[patternIndex];

            if (patternChar == '*')
            {
                while (patternIndex + 1 < pattern.Length && pattern[patternIndex + 1] == '*')
                {
                    patternIndex++;
                }

                if (patternIndex + 1 == pattern.Length)
                {
                    return true;
                }

                for (int i = textIndex; i <= text.Length; i++)
                {
                    if (WildcardMatchAt(text, i, pattern, patternIndex + 1))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (textIndex >= text.Length)
            {
                return false;
            }

            if (patternChar != '?' && ToUpper(patternChar) != ToUpper(text[textIndex]))
            {
                return false;
            }

            patternIndex++;
            textIndex++;
        }

        return textIndex == text.Length;
    }

    private static char ToUpper(char value)
    {
        return Char.ToUpper(value, CultureInfo.InvariantCulture);
    }

    private static bool TryParseDate(string text, out DateTime value)
    {
        value = DateTime.MinValue;
        try
        {
            value = DateTime.ParseExact(text, DateFormat, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseStep(string text, out TimeSpan step)
    {
        step = TimeSpan.Zero;

        if (text == null || text.Length < 3 || text[0] != '+')
        {
            return false;
        }

        char unit = Char.ToLower(text[text.Length - 1], CultureInfo.InvariantCulture);
        string numberText = text.Substring(1, text.Length - 2);
        int number;
        try
        {
            number = Int32.Parse(numberText, CultureInfo.InvariantCulture);
        }
        catch
        {
            return false;
        }

        if (number <= 0)
        {
            return false;
        }

        if (unit == 's')
        {
            step = TimeSpan.FromSeconds(number);
            return true;
        }
        if (unit == 'm')
        {
            step = TimeSpan.FromMinutes(number);
            return true;
        }
        if (unit == 'h')
        {
            step = TimeSpan.FromHours(number);
            return true;
        }
        if (unit == 'd')
        {
            step = TimeSpan.FromDays(number);
            return true;
        }

        return false;
    }

    private static string MakeAbsolutePath(string path, string baseDirectory)
    {
        string normalized = NormalizePath(path);
        if (Path.IsPathRooted(normalized))
        {
            return normalized;
        }

        return Path.Combine(baseDirectory, normalized);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('/', '\\');
    }

    private static string GetExecutableDirectory()
    {
        string path = null;

        try
        {
#if WIN10_DESKTOP
            path = Assembly.GetExecutingAssembly().Location;
#else
            path = Assembly.GetExecutingAssembly().GetName().CodeBase;
#endif
        }
        catch
        {
        }

        if (path == null || path.Length == 0)
        {
            return "\\";
        }

        path = NormalizePath(path);
        if (path.StartsWith("file:"))
        {
            path = path.Substring(5);
            while (path.Length > 1 && path[0] == '\\' && path[1] == '\\')
            {
                path = path.Substring(1);
            }
        }

        string directory = Path.GetDirectoryName(path);
        if (directory == null || directory.Length == 0)
        {
            return "\\";
        }

        return directory;
    }

    private static string GetDirectoryName(string path)
    {
        string directory = Path.GetDirectoryName(NormalizePath(path));
        if (directory == null || directory.Length == 0)
        {
            return GetExecutableDirectory();
        }

        return directory;
    }

    private static string TrimTrailingSeparatorsPreserveRoot(string path)
    {
        if (path == null || path.Length == 0)
        {
            return path;
        }

        string root = Path.GetPathRoot(path);
        int minimumLength = root == null ? 0 : root.Length;
        int end = path.Length;
        while (end > minimumLength && path[end - 1] == '\\')
        {
            end--;
        }

        return path.Substring(0, end);
    }

    private static string TrimLeadingSeparators(string path)
    {
        int index = 0;
        while (index < path.Length && path[index] == '\\')
        {
            index++;
        }

        return path.Substring(index);
    }

    private static string[] SplitLines(string text)
    {
        return text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    }

    private static string CleanLine(string line)
    {
        string cleaned = line.Trim();
        if (cleaned.Length > 0 && cleaned[0] == '\uFEFF')
        {
            cleaned = cleaned.Substring(1).Trim();
        }

        return cleaned;
    }

    private static string StripWhitespace(string text)
    {
        StringBuilder builder = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (!Char.IsWhiteSpace(text[i]) && text[i] != '\uFEFF')
            {
                builder.Append(text[i]);
            }
        }

        return builder.ToString();
    }

    private static bool EqualsIgnoreCase(string left, string right)
    {
        return String.Compare(left, right, true, CultureInfo.InvariantCulture) == 0;
    }

    private static void SortStrings(string[] values)
    {
        Array.Sort(values);
    }

    private static void SortArrayListStrings(ArrayList values)
    {
        string[] strings = new string[values.Count];
        for (int i = 0; i < values.Count; i++)
        {
            strings[i] = (string)values[i];
        }

        SortStrings(strings);
        values.Clear();
        for (int i = 0; i < strings.Length; i++)
        {
            values.Add(strings[i]);
        }
    }

    private static void AppendFailure(StringBuilder builder, string message)
    {
        int currentCount = 0;
        for (int i = 0; i < builder.Length; i++)
        {
            if (builder[i] == '\n')
            {
                currentCount++;
            }
        }

        if (currentCount < MaxFailureMessages)
        {
            builder.Append("- ");
            builder.Append(message);
            builder.Append("\r\n");
        }
    }

    private static void ShowMessage(string message)
    {
        MessageBox.Show(message, isDryRun ? "SetTime (DRY RUN)" : "SetTime");
    }

    private static byte[] ReadAllBytes(string path)
    {
        FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        try
        {
            byte[] buffer = new byte[(int)stream.Length];
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = stream.Read(buffer, offset, buffer.Length - offset);
                if (read <= 0)
                {
                    break;
                }

                offset += read;
            }

            if (offset == buffer.Length)
            {
                return buffer;
            }

            byte[] trimmed = new byte[offset];
            Array.Copy(buffer, 0, trimmed, 0, offset);
            return trimmed;
        }
        finally
        {
            stream.Close();
        }
    }

    private sealed class MainForm : Form
    {
        private Label lblDir;
        private TextBox tbDir;
        private Button btnBrowse;
        private Label lblFilter;
        private TextBox tbFilter;
        private CheckBox cbRecurse;
        private CheckBox cbCreation;
        private CheckBox cbModified;
        private CheckBox cbAccessed;
        private Label lblTime;
        private TextBox tbTime;
        private Label lblHint;
        private Button btnCancel;
        private Button btnRun;

        public MainForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "SetTime";
            ClientSize = new System.Drawing.Size(320, 470);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = true;

            lblDir = new Label();
            lblDir.Text = "\u76EE\u5F55";
            lblDir.Location = new System.Drawing.Point(10, 16);
            lblDir.Size = new System.Drawing.Size(60, 24);

            tbDir = new TextBox();
            tbDir.Location = new System.Drawing.Point(10, 42);
            tbDir.Size = new System.Drawing.Size(230, 24);

            btnBrowse = new Button();
            btnBrowse.Text = "\u6D4F\u89C8";
            btnBrowse.Location = new System.Drawing.Point(244, 41);
            btnBrowse.Size = new System.Drawing.Size(70, 26);
            btnBrowse.Click += new EventHandler(OnBrowseClick);

            lblFilter = new Label();
            lblFilter.Text = "\u6587\u4EF6\u540D\u8FC7\u6EE4";
            lblFilter.Location = new System.Drawing.Point(10, 82);
            lblFilter.Size = new System.Drawing.Size(120, 24);

            tbFilter = new TextBox();
            tbFilter.Text = "*";
            tbFilter.Location = new System.Drawing.Point(10, 108);
            tbFilter.Size = new System.Drawing.Size(304, 24);

            cbRecurse = new CheckBox();
            cbRecurse.Text = "\u5305\u542B\u5B50\u76EE\u5F55";
            cbRecurse.Location = new System.Drawing.Point(10, 146);
            cbRecurse.Size = new System.Drawing.Size(180, 24);

            cbCreation = new CheckBox();
            cbCreation.Text = "\u521B\u5EFA\u65F6\u95F4";
            cbCreation.Checked = true;
            cbCreation.Location = new System.Drawing.Point(10, 174);
            cbCreation.Size = new System.Drawing.Size(95, 24);

            cbModified = new CheckBox();
            cbModified.Text = "\u4FEE\u6539\u65F6\u95F4";
            cbModified.Checked = true;
            cbModified.Location = new System.Drawing.Point(110, 174);
            cbModified.Size = new System.Drawing.Size(95, 24);

            cbAccessed = new CheckBox();
            cbAccessed.Text = "\u8BBF\u95EE\u65F6\u95F4";
            cbAccessed.Checked = true;
            cbAccessed.Location = new System.Drawing.Point(210, 174);
            cbAccessed.Size = new System.Drawing.Size(95, 24);

            lblTime = new Label();
            lblTime.Text = "\u76EE\u6807\u65F6\u95F4";
            lblTime.Location = new System.Drawing.Point(10, 216);
            lblTime.Size = new System.Drawing.Size(120, 24);

            tbTime = new TextBox();
            tbTime.Location = new System.Drawing.Point(10, 242);
            tbTime.Size = new System.Drawing.Size(304, 24);

            lblHint = new Label();
            lblHint.Text = "\u652F\u6301\u793A\u4F8B\uFF1A\r\n" +
                "2026-05-06 14:30:00\r\n" +
                "2026-05-06 14:30\r\n" +
                "2026/05/06 14:30\r\n" +
                "2026-5-6\r\n" +
                "2026\u5E745\u67086\u65E5 14\u70B930\u5206";
            lblHint.Location = new System.Drawing.Point(10, 282);
            lblHint.Size = new System.Drawing.Size(304, 110);

            btnCancel = new Button();
            btnCancel.Text = "\u53D6\u6D88";
            btnCancel.Location = new System.Drawing.Point(154, 416);
            btnCancel.Size = new System.Drawing.Size(74, 30);
            btnCancel.Click += new EventHandler(OnCancelClick);

            btnRun = new Button();
            btnRun.Text = "\u4FEE\u6539";
            btnRun.Location = new System.Drawing.Point(240, 416);
            btnRun.Size = new System.Drawing.Size(74, 30);
            btnRun.Click += new EventHandler(OnRunClick);

            Controls.Add(lblDir);
            Controls.Add(tbDir);
            Controls.Add(btnBrowse);
            Controls.Add(lblFilter);
            Controls.Add(tbFilter);
            Controls.Add(cbRecurse);
            Controls.Add(cbCreation);
            Controls.Add(cbModified);
            Controls.Add(cbAccessed);
            Controls.Add(lblTime);
            Controls.Add(tbTime);
            Controls.Add(lblHint);
            Controls.Add(btnCancel);
            Controls.Add(btnRun);
        }

        private void OnBrowseClick(object sender, EventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog();
            try
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    string directory = Path.GetDirectoryName(dialog.FileName);
                    if (directory != null && directory.Length > 0)
                    {
                        tbDir.Text = directory;
                        tbFilter.Text = Path.GetFileName(dialog.FileName);
                    }
                }
            }
            finally
            {
                dialog.Dispose();
            }
        }

        private void OnCancelClick(object sender, EventArgs e)
        {
            Close();
        }

        private void OnRunClick(object sender, EventArgs e)
        {
            string directory = tbDir.Text.Trim();
            if (!Directory.Exists(directory))
            {
                MessageBox.Show("\u8BF7\u9009\u62E9\u6709\u6548\u7684\u76EE\u5F55\u3002", "SetTime");
                return;
            }

            DateTime targetTime;
            if (!Program.TryParseFlexibleDateTime(tbTime.Text, out targetTime))
            {
                MessageBox.Show("\u65F6\u95F4\u683C\u5F0F\u4E0D\u6B63\u786E\u3002\r\n\u793A\u4F8B\uFF1A2026-05-06 14:30:00", "SetTime");
                return;
            }

            if (!cbCreation.Checked && !cbModified.Checked && !cbAccessed.Checked)
            {
                MessageBox.Show("\u8BF7\u81F3\u5C11\u9009\u62E9\u4E00\u9879\u8981\u4FEE\u6539\u7684\u65F6\u95F4\u6233", "SetTime");
                return;
            }

            modifyCreationTime = cbCreation.Checked;
            modifyLastWriteTime = cbModified.Checked;
            modifyLastAccessTime = cbAccessed.Checked;
            string timestampNames = GetSelectedTimestampNames();

            string filter = tbFilter.Text.Trim();
            if (filter.Length == 0)
            {
                filter = "*";
            }

            ArrayList files;
            try
            {
                files = GetGuiFiles(directory, filter, cbRecurse.Checked);
            }
            catch (Exception ex)
            {
                MessageBox.Show("\u8BFB\u53D6\u6587\u4EF6\u5931\u8D25\uFF1A\r\n" + ex.Message, "SetTime");
                return;
            }

            if (files.Count == 0)
            {
                MessageBox.Show("\u672A\u627E\u5230\u7B26\u5408\u6761\u4EF6\u7684\u6587\u4EF6\u3002", "SetTime");
                return;
            }

            if (files.Count > MaxFilesPerRule)
            {
                MessageBox.Show("\u627E\u5230\u7684\u6587\u4EF6\u8D85\u8FC7 10000 \u4E2A\uFF0C\u5DF2\u62D2\u7EDD\u64CD\u4F5C\u3002", "SetTime");
                return;
            }

            if (!ConfirmRun(files, timestampNames))
            {
                return;
            }

            int successCount = 0;
            int failureCount = 0;
            StringBuilder failures = new StringBuilder();

            for (int i = 0; i < files.Count; i++)
            {
                string error;
                string filePath = (string)files[i];
                if (Program.SetAllFileTimes(filePath, targetTime, out error))
                {
                    successCount++;
                }
                else
                {
                    failureCount++;
                    if (failureCount <= 3)
                    {
                        failures.Append("- ");
                        failures.Append(Path.GetFileName(filePath));
                        failures.Append(" : ");
                        failures.Append(error);
                        failures.Append("\r\n");
                    }
                }
            }

            StringBuilder result = new StringBuilder();
            result.Append("\u6210\u529F ");
            result.Append(successCount.ToString(CultureInfo.InvariantCulture));
            result.Append(" \u4E2A\uFF0C\u5931\u8D25 ");
            result.Append(failureCount.ToString(CultureInfo.InvariantCulture));
            result.Append(" \u4E2A\u3002");

            if (failures.Length > 0)
            {
                result.Append("\r\n\r\n\u524D 3 \u4E2A\u5931\u8D25\u539F\u56E0\uFF1A\r\n");
                result.Append(failures.ToString());
            }

            MessageBox.Show(result.ToString(), "SetTime");
        }

        private static ArrayList GetGuiFiles(string directory, string filter, bool recursive)
        {
            ArrayList files = new ArrayList();
            AddMatchingFiles(directory, filter, recursive, files);
            return files;
        }

        private string GetSelectedTimestampNames()
        {
            StringBuilder builder = new StringBuilder();
            AppendTimestampName(builder, cbCreation.Checked, "\u521B\u5EFA\u65F6\u95F4");
            AppendTimestampName(builder, cbModified.Checked, "\u4FEE\u6539\u65F6\u95F4");
            AppendTimestampName(builder, cbAccessed.Checked, "\u8BBF\u95EE\u65F6\u95F4");
            return builder.ToString();
        }

        private static void AppendTimestampName(StringBuilder builder, bool selected, string name)
        {
            if (!selected)
            {
                return;
            }

            if (builder.Length > 0)
            {
                builder.Append(" \u548C ");
            }

            builder.Append(name);
        }

        private bool ConfirmRun(ArrayList files, string timestampNames)
        {
            StringBuilder message = new StringBuilder();
            message.Append("\u5C06\u4FEE\u6539 ");
            message.Append(files.Count.ToString(CultureInfo.InvariantCulture));
            message.Append(" \u4E2A\u6587\u4EF6\u7684");
            message.Append(timestampNames);
            message.Append("\u3002\r\n\r\n");

            int previewCount = files.Count < 5 ? files.Count : 5;
            for (int i = 0; i < previewCount; i++)
            {
                message.Append("- ");
                message.Append(Path.GetFileName((string)files[i]));
                message.Append("\r\n");
            }

            if (files.Count > previewCount)
            {
                message.Append("...\r\n");
            }

            message.Append("\r\n\u786E\u8BA4\u7EE7\u7EED\uFF1F");

            return MessageBox.Show(message.ToString(), "SetTime", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }
    }
}
