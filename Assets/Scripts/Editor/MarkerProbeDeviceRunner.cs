using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

using Debug = UnityEngine.Debug;

/// <summary>
/// Installs the isolated Marker Probe development APK on a connected Quest and keeps
/// a host-side logcat capture. Running ADB from the Editor process also works when the
/// automation shell is not allowed to open ADB's localhost server socket.
/// </summary>
public static class MarkerProbeDeviceRunner
{
    const string PackageName = "com.DefaultCompany.MixedRealityTemplate";
    const string LaunchActivity = "com.unity3d.player.UnityPlayerActivity";
    const string QuestApk = "Builds/MarkerProbe/Quest/MarkerProbe-Quest.apk";
    const string LogDirectory = "Builds/MarkerProbe/Logs";

    static readonly object ProcessGate = new object();
    static readonly object LogGate = new object();
    static Process logcatProcess;

    [MenuItem("MRBase/Build/Marker Probe/Quest Install, Run and Monitor")]
    public static void InstallRunAndMonitorQuest()
    {
        string projectRoot = Path.GetDirectoryName(Application.dataPath);
        string apkPath = Path.GetFullPath(Path.Combine(projectRoot, QuestApk));
        string logRoot = Path.GetFullPath(Path.Combine(projectRoot, LogDirectory));
        string adbPath = ResolveAdbPath();

        if (!File.Exists(apkPath))
            throw new FileNotFoundException("Quest Marker Probe APK not found.", apkPath);
        if (!File.Exists(adbPath))
            throw new FileNotFoundException("Unity Android SDK adb not found.", adbPath);

        Directory.CreateDirectory(logRoot);
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string commandLog = Path.Combine(logRoot, $"Quest-{stamp}-adb.log");
        string liveLog = Path.Combine(logRoot, $"Quest-{stamp}-logcat.log");

        Debug.Log($"[MarkerProbeDevice] Quest install/run queued. adbLog={commandLog} liveLog={liveLog}");
        _ = Task.Run(() => InstallRunAndCapture(adbPath, apkPath, commandLog, liveLog));
    }

    [MenuItem("MRBase/Build/Marker Probe/Quest Pull Device Logs")]
    public static void PullQuestDeviceLogs()
    {
        string projectRoot = Path.GetDirectoryName(Application.dataPath);
        string logRoot = Path.GetFullPath(Path.Combine(projectRoot, LogDirectory));
        string adbPath = ResolveAdbPath();
        Directory.CreateDirectory(logRoot);
        string destination = Path.Combine(
            logRoot,
            $"Quest-device-files-{DateTime.Now:yyyyMMdd-HHmmss}");

        _ = Task.Run(() =>
        {
            string auditLog = destination + "-pull.log";
            try
            {
                string serial = RequireQuestDevice(adbPath, auditLog);
                Directory.CreateDirectory(destination);
                CommandResult pull = RunAdb(
                    adbPath,
                    auditLog,
                    "-s", serial,
                    "pull", $"/sdcard/Android/data/{PackageName}/files", destination);
                if (pull.ExitCode != 0)
                    throw new InvalidOperationException($"adb pull failed ({pull.ExitCode}): {pull.Output}");
                Debug.Log($"[MarkerProbeDevice] Quest device logs pulled to {destination}");
            }
            catch (Exception exception)
            {
                AppendLine(auditLog, "ERROR " + exception);
                Debug.LogError($"[MarkerProbeDevice] Pull failed: {exception.Message}; auditLog={auditLog}");
            }
        });
    }

    [MenuItem("MRBase/Build/Marker Probe/Stop Quest Log Monitor")]
    public static void StopQuestLogMonitor()
    {
        lock (ProcessGate)
        {
            if (logcatProcess == null || logcatProcess.HasExited)
                return;
            logcatProcess.Kill();
            logcatProcess.Dispose();
            logcatProcess = null;
        }
        Debug.Log("[MarkerProbeDevice] Quest logcat monitor stopped.");
    }

    static void InstallRunAndCapture(
        string adbPath,
        string apkPath,
        string commandLog,
        string liveLog)
    {
        try
        {
            string serial = RequireQuestDevice(adbPath, commandLog);
            RequireSuccess(RunAdb(
                adbPath, commandLog, "-s", serial, "install", "-r", "-d", apkPath),
                "install");
            RunAdb(adbPath, commandLog, "-s", serial, "shell", "am", "force-stop", PackageName);
            RunAdb(adbPath, commandLog, "-s", serial, "logcat", "-c");
            RequireSuccess(RunAdb(
                adbPath,
                commandLog,
                "-s", serial,
                "shell", "am", "start", "-n", $"{PackageName}/{LaunchActivity}"),
                "launch");

            System.Threading.Thread.Sleep(2000);
            CommandResult pidResult = RunAdb(
                adbPath, commandLog, "-s", serial, "shell", "pidof", "-s", PackageName);
            RequireSuccess(pidResult, "resolve application pid");
            string pid = pidResult.Output.Trim();
            if (string.IsNullOrEmpty(pid) || pid.Any(character => !char.IsDigit(character)))
                throw new InvalidOperationException($"Invalid Quest application pid: '{pid}'");

            StartLogcat(adbPath, serial, pid, liveLog, commandLog);
            Debug.Log(
                $"[MarkerProbeDevice] Quest Marker Probe running. serial={serial} pid={pid} " +
                $"liveLog={liveLog}");
        }
        catch (Exception exception)
        {
            AppendLine(commandLog, "ERROR " + exception);
            Debug.LogError(
                $"[MarkerProbeDevice] Quest install/run failed: {exception.Message}; " +
                $"auditLog={commandLog}");
        }
    }

    static string RequireQuestDevice(string adbPath, string auditLog)
    {
        RequireSuccess(RunAdb(adbPath, auditLog, "start-server"), "start adb server");
        CommandResult devices = RunAdb(adbPath, auditLog, "devices", "-l");
        RequireSuccess(devices, "list devices");

        List<string> serials = devices.Output
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(
                new[] { '\t', ' ' },
                StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length >= 2 && parts[1] == "device")
            .Select(parts => parts[0])
            .ToList();
        if (serials.Count != 1)
            throw new InvalidOperationException($"Expected exactly one authorized Android device; found {serials.Count}.");

        string serial = serials[0];
        string manufacturer = RunAdb(
            adbPath, auditLog, "-s", serial, "shell", "getprop", "ro.product.manufacturer").Output.Trim();
        string model = RunAdb(
            adbPath, auditLog, "-s", serial, "shell", "getprop", "ro.product.model").Output.Trim();
        string identity = $"{manufacturer} {model}";
        if (identity.IndexOf("oculus", StringComparison.OrdinalIgnoreCase) < 0 &&
            identity.IndexOf("meta", StringComparison.OrdinalIgnoreCase) < 0 &&
            identity.IndexOf("quest", StringComparison.OrdinalIgnoreCase) < 0)
        {
            throw new InvalidOperationException(
                $"Connected device is not recognized as Quest: serial={serial}, identity='{identity}'.");
        }

        AppendLine(auditLog, $"QUEST_DEVICE serial={serial} identity={identity}");
        return serial;
    }

    static void StartLogcat(
        string adbPath,
        string serial,
        string pid,
        string liveLog,
        string auditLog)
    {
        lock (ProcessGate)
        {
            if (logcatProcess != null && !logcatProcess.HasExited)
                logcatProcess.Kill();
            logcatProcess?.Dispose();

            File.WriteAllText(
                liveLog,
                $"# Quest Marker Probe logcat started {DateTime.Now:O}; serial={serial}; pid={pid}{Environment.NewLine}");
            var startInfo = CreateStartInfo(
                adbPath,
                "-s", serial,
                "logcat", "--pid", pid, "-v", "threadtime");
            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data == null) return;
                AppendLine(liveLog, args.Data);
                if (args.Data.Contains("[MarkerProbe]")) Debug.Log(args.Data);
            };
            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data == null) return;
                AppendLine(liveLog, "STDERR " + args.Data);
            };
            process.Exited += (_, __) => AppendLine(
                liveLog,
                $"# logcat exited {DateTime.Now:O}; exitCode={process.ExitCode}");
            if (!process.Start())
                throw new InvalidOperationException("Could not start adb logcat.");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            logcatProcess = process;
            AppendLine(auditLog, $"LOGCAT_STARTED pid={pid} file={liveLog}");
        }
    }

    static CommandResult RunAdb(string adbPath, string auditLog, params string[] arguments)
    {
        AppendLine(auditLog, "> adb " + string.Join(" ", arguments.Select(QuoteArgument)));
        using var process = new Process { StartInfo = CreateStartInfo(adbPath, arguments) };
        if (!process.Start())
            throw new InvalidOperationException("Could not start adb process.");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        string output = (stdout + Environment.NewLine + stderr).Trim();
        AppendLine(auditLog, $"EXIT {process.ExitCode}{Environment.NewLine}{output}");
        return new CommandResult(process.ExitCode, output);
    }

    static ProcessStartInfo CreateStartInfo(string executable, params string[] arguments)
    {
        return new ProcessStartInfo
        {
            FileName = executable,
            Arguments = string.Join(" ", arguments.Select(QuoteArgument)),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
    }

    static string ResolveAdbPath()
    {
        DirectoryInfo candidateRoot = Directory.Exists(EditorApplication.applicationPath)
            ? new DirectoryInfo(EditorApplication.applicationPath)
            : new FileInfo(EditorApplication.applicationPath).Directory;
        for (int depth = 0; candidateRoot != null && depth < 8; depth++)
        {
            string candidate = Path.Combine(
                candidateRoot.FullName,
                "PlaybackEngines", "AndroidPlayer", "SDK", "platform-tools", "adb");
            if (File.Exists(candidate))
                return candidate;
            candidateRoot = candidateRoot.Parent;
        }

        throw new FileNotFoundException(
            $"Could not resolve Unity Android SDK adb from '{EditorApplication.applicationPath}'.");
    }

    static string QuoteArgument(string argument)
    {
        if (argument == null) return "\"\"";
        return "\"" + argument.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    static void RequireSuccess(CommandResult result, string operation)
    {
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"adb {operation} failed ({result.ExitCode}): {result.Output}");
    }

    static void AppendLine(string path, string message)
    {
        lock (LogGate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.AppendAllText(path, $"[{DateTime.Now:O}] {message}{Environment.NewLine}");
        }
    }

    readonly struct CommandResult
    {
        public readonly int ExitCode;
        public readonly string Output;

        public CommandResult(int exitCode, string output)
        {
            ExitCode = exitCode;
            Output = output ?? string.Empty;
        }
    }
}
