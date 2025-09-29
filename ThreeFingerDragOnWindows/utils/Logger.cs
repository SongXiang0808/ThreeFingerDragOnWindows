using System.Collections.Concurrent;
using System.Diagnostics;
using Windows.Storage;

namespace ThreeFingerDragOnWindows.utils;

using System;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;

public class Logger {
    private static readonly ConcurrentQueue<string> _logMessages = new();
    private static readonly int _maxLogCount = 10000;
    private static readonly string _logFilePath;
    private static readonly Timer _flushTimer;
    private static readonly object _fileLock = new object();

    static Logger()
    {
        string logDirectory;
        try
        {
            var localPath = ApplicationData.Current?.LocalFolder?.Path;
            logDirectory = string.IsNullOrWhiteSpace(localPath)
                ? Path.Combine(Path.GetTempPath(), "ThreeFingerDragOnWindows", "log")
                : Path.Combine(localPath, "log");
        }
        catch
        {
            logDirectory = Path.Combine(Path.GetTempPath(), "ThreeFingerDragOnWindows", "log");
        }

        try
        {
            Directory.CreateDirectory(logDirectory);
        }
        catch
        {
            logDirectory = Path.Combine(Path.GetTempPath(), "ThreeFingerDragOnWindows", "log");
            Directory.CreateDirectory(logDirectory);
        }

        _logFilePath = Path.Combine(logDirectory, $"mouselike_debug_{DateTime.Now:yyyyMMdd_HHmmss}.log");

        try
        {
            _flushTimer = new Timer(FlushLogsToFile, null, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
        }
        catch
        {
            _flushTimer = new Timer(_ => { }, null, Timeout.Infinite, Timeout.Infinite);
        }

        Log("Logger initialized with file: " + _logFilePath);
    }

    public static void Log(string message){
        Debug.WriteLine(message);

        if(App.SettingsData != null && !App.SettingsData.RecordLogs){
            return;
        }

        string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        string logEntry = $"[{timestamp}] {message}";

        if(_logMessages.Count >= _maxLogCount){
            _logMessages.TryDequeue(out _);
        }
        _logMessages.Enqueue(logEntry);
    }
    private static void FlushLogsToFile(object state)
    {
        if(App.SettingsData != null && !App.SettingsData.RecordLogs){
            while(_logMessages.TryDequeue(out _)){}
            return;
        }

        if (_logMessages.IsEmpty) return;

        try
        {
            lock (_fileLock)
            {
                var logsToWrite = new List<string>();
                while (_logMessages.TryDequeue(out string logEntry) && logsToWrite.Count < 100)
                {
                    logsToWrite.Add(logEntry);
                }

                if (logsToWrite.Count > 0)
                {
                    File.AppendAllLines(_logFilePath, logsToWrite);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to write logs to file: {ex.Message}");
        }
    }

    public static Task ExportLogsAsync(string path){
        return File.WriteAllLinesAsync(path, _logMessages.ToArray());
    }

    public static string GetLogFilePath() => _logFilePath;

    /// <summary>
    /// 强制立即刷新所有日志到文件
    /// </summary>
    public static void FlushNow()
    {
        FlushLogsToFile(null);
    }
}



