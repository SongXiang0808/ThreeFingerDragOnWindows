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
        // 将日志文件保存在指定的log目录下
        string logDirectory = @"F:\TouchPadCode\log";
        Directory.CreateDirectory(logDirectory); // 确保目录存在
        _logFilePath = Path.Combine(logDirectory, $"mouselike_debug_{DateTime.Now:yyyyMMdd_HHmmss}.log");

        // 每500毫秒自动刷新日志到文件（更频繁）
        _flushTimer = new Timer(FlushLogsToFile, null, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));

        Log("Logger initialized with file: " + _logFilePath);
    }

    public static void Log(string message){
        Debug.WriteLine(message);

        string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        string logEntry = $"[{timestamp}] {message}";

        // 始终记录到内存队列（不检查SettingsData，因为初始化时可能为null）
        if(_logMessages.Count >= _maxLogCount){
            _logMessages.TryDequeue(out _);
        }
        _logMessages.Enqueue(logEntry);

        // 如果设置存在且禁用了日志记录，只记录到调试输出和内存，不写入文件
        if(App.SettingsData != null && !App.SettingsData.RecordLogs){
            return;
        }
    }

    private static void FlushLogsToFile(object state)
    {
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
