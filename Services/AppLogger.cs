using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WebViewHub.Services
{
    /// <summary>
    /// 应用日志管理器 - 负责日志记录和历史日志压缩
    /// </summary>
    public static class AppLogger
    {
        private static readonly string AppName = "WebViewHub";
        private static readonly string LogsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        private static readonly string ArchiveDir = Path.Combine(LogsDir, "archive");
        private static readonly string LogFilePrefix = AppName + "-";
        
        // 配置参数
        private const int MaxArchiveDays = 30;          // 压缩包保留天数
        private const long MaxLogsSizeMB = 200;         // 日志总大小限制(MB)
        private const int CompressionDelaySeconds = 10;  // 启动延迟压缩秒数
        private const int MaxCompressionThreads = 4;     // 最大压缩线程数
        
        private static readonly string InfoLogPath;
        private static readonly string ErrorLogPath;
        private static readonly object _logLock = new object();
        
        // UI日志事件 - 用于在界面上显示日志
        public static event Action<string, string>? OnLogMessage;
        
        static AppLogger()
        {
            // 确保目录存在
            if (!Directory.Exists(LogsDir))
                Directory.CreateDirectory(LogsDir);
            if (!Directory.Exists(ArchiveDir))
                Directory.CreateDirectory(ArchiveDir);
            
            // 当天日期作为日志文件名
            string today = DateTime.Now.ToString("yyyyMMdd");
            InfoLogPath = Path.Combine(LogsDir, $"{LogFilePrefix}{today}.log");
            ErrorLogPath = Path.Combine(LogsDir, $"{LogFilePrefix}{today}_error.log");
        }
        
        /// <summary>
        /// 程序启动时调用 - 初始化日志并启动后台压缩
        /// </summary>
        public static void Initialize()
        {
            // 记录启动日志
            Info("========== 程序启动 ==========");
            Info($"版本: {GetVersion()}");
            Info($"工作目录: {AppDomain.CurrentDomain.BaseDirectory}");
            Info($"操作系统: {Environment.OSVersion}");
            
            // 启动后台日志压缩任务（延迟执行）
            Task.Run(async () =>
            {
                try
                {
                    // 延迟启动，避免影响主程序启动速度
                    await Task.Delay(TimeSpan.FromSeconds(CompressionDelaySeconds));
                    
                    Info("开始执行历史日志压缩任务...");
                    await CompressHistoryLogsAsync();
                    CleanupOldArchives();
                    EnforceLogsSizeLimit();
                    Info("历史日志压缩任务完成");
                }
                catch (Exception ex)
                {
                    Error("日志压缩任务异常", ex);
                }
            });
        }
        
        /// <summary>
        /// 获取程序版本
        /// </summary>
        private static string GetVersion()
        {
            return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
        }
        
        #region 日志写入
        
        /// <summary>
        /// 记录信息日志
        /// </summary>
        public static void Info(string message)
        {
            WriteLog("INFO", message, InfoLogPath);
        }
        
        /// <summary>
        /// 记录错误日志
        /// </summary>
        public static void Error(string message, Exception? ex = null)
        {
            string fullMessage = ex != null ? $"{message}: {ex.Message}\n{ex.StackTrace}" : message;
            WriteLog("ERROR", fullMessage, ErrorLogPath);
            WriteLog("ERROR", fullMessage, InfoLogPath); // 同时写入主日志
        }
        
        /// <summary>
        /// 记录警告日志
        /// </summary>
        public static void Warning(string message)
        {
            WriteLog("WARN", message, InfoLogPath);
        }
        
        /// <summary>
        /// 记录调试日志
        /// </summary>
        public static void Debug(string message)
        {
            #if DEBUG
            WriteLog("DEBUG", message, InfoLogPath);
            #endif
        }
        
        private static void WriteLog(string level, string message, string filePath)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                string logLine = $"[{timestamp}] [{level}] {message}";
                
                lock (_logLock)
                {
                    File.AppendAllText(filePath, logLine + Environment.NewLine);
                }
                
                // 触发UI事件
                try { OnLogMessage?.Invoke(level, logLine); } catch { }
            }
            catch
            {
                // 忽略日志写入失败，避免死循环
            }
        }
        
        #endregion
        
        #region 日志压缩
        
        /// <summary>
        /// 获取昨天的日志日期（用于判断当前日志）
        /// </summary>
        private static string GetYesterdayDateStr()
        {
            return DateTime.Now.AddDays(-1).ToString("yyyyMMdd");
        }
        
        /// <summary>
        /// 判断文件是否为当前需要保留的日志（昨天和今天的日志不压缩）
        /// </summary>
        private static bool IsCurrentLogFile(FileInfo file)
        {
            string yesterday = GetYesterdayDateStr();
            string today = DateTime.Now.ToString("yyyyMMdd");
            
            // 检查是否匹配 app-YYYYMMDD.log 格式
            string fileName = file.Name;
            if (!fileName.StartsWith(LogFilePrefix) || !fileName.EndsWith(".log"))
                return false;
            
            // 提取日期部分
            int prefixLen = LogFilePrefix.Length;
            int suffixLen = ".log".Length;
            if (fileName.Length < prefixLen + suffixLen + 8)
                return false;
            
            string dateStr = fileName.Substring(prefixLen, 8);
            
            // 昨天和今天的日志不压缩
            return dateStr == yesterday || dateStr == today;
        }
        
        /// <summary>
        /// 按日期分组历史日志文件
        /// </summary>
        private static Dictionary<string, List<FileInfo>> GroupLogsByDate(DirectoryInfo logsDir)
        {
            var groups = new Dictionary<string, List<FileInfo>>();
            
            foreach (var file in logsDir.GetFiles("*.log", SearchOption.TopDirectoryOnly))
            {
                // 跳过当前日志
                if (IsCurrentLogFile(file))
                    continue;
                
                // 提取日期
                string dateStr = ExtractDateFromFileName(file.Name);
                if (string.IsNullOrEmpty(dateStr))
                    continue;
                
                if (!groups.ContainsKey(dateStr))
                    groups[dateStr] = new List<FileInfo>();
                
                groups[dateStr].Add(file);
            }
            
            return groups;
        }
        
        /// <summary>
        /// 从文件名提取日期
        /// </summary>
        private static string? ExtractDateFromFileName(string fileName)
        {
            // 格式: app-20260305.log 或 app-20260305.log.1
            if (!fileName.StartsWith(LogFilePrefix))
                return null;
            
            int prefixLen = LogFilePrefix.Length;
            if (fileName.Length < prefixLen + 8)
                return null;
            
            string datePart = fileName.Substring(prefixLen, 8);
            if (DateTime.TryParseExact(datePart, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out _))
                return datePart;
            
            return null;
        }
        
        /// <summary>
        /// 多线程压缩历史日志
        /// </summary>
        private static async Task CompressHistoryLogsAsync()
        {
            if (!Directory.Exists(LogsDir))
                return;
            
            var logsDir = new DirectoryInfo(LogsDir);
            
            // 按日期分组
            var groups = GroupLogsByDate(logsDir);
            
            if (groups.Count == 0)
            {
                Info("没有需要压缩的历史日志");
                return;
            }
            
            Info($"发现 {groups.Count} 组历史日志待压缩");
            
            // 使用线程池并行压缩
            var tasks = new List<Task>();
            var semaphore = new SemaphoreSlim(MaxCompressionThreads);
            
            foreach (var group in groups)
            {
                await semaphore.WaitAsync();
                
                var task = Task.Run(async () =>
                {
                    try
                    {
                        await CompressLogGroupAsync(group.Key, group.Value);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });
                
                tasks.Add(task);
            }
            
            await Task.WhenAll(tasks);
        }
        
        /// <summary>
        /// 压缩一组同一天的日志文件
        /// </summary>
        private static async Task CompressLogGroupAsync(string dateStr, List<FileInfo> logFiles)
        {
            if (logFiles.Count == 0)
                return;
            
            try
            {
                string zipFileName = $"{LogFilePrefix}{dateStr}.zip";
                string zipPath = Path.Combine(ArchiveDir, zipFileName);
                
                // 如果压缩包已存在，跳过
                if (File.Exists(zipPath))
                {
                    Info($"压缩包 {zipFileName} 已存在，跳过");
                    return;
                }
                
                // 创建压缩包
                await Task.Run(() =>
                {
                    using var zipStream = new FileStream(zipPath, FileMode.Create);
                    using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);
                    
                    foreach (var logFile in logFiles)
                    {
                        try
                        {
                            // 跳过正在写入的文件
                            if (IsFileLocked(logFile))
                                continue;
                            
                            var entry = archive.CreateEntry(logFile.Name, CompressionLevel.Optimal);
                            using var entryStream = entry.Open();
                            using var fileStream = File.OpenRead(logFile.FullName);
                            fileStream.CopyTo(entryStream);
                        }
                        catch (Exception ex)
                        {
                            Warning($"压缩文件 {logFile.Name} 失败: {ex.Message}");
                        }
                    }
                });
                
                Info($"已压缩 {dateStr} 日志: {logFiles.Count} 个文件 -> {zipFileName}");
                
                // 删除原日志文件
                foreach (var logFile in logFiles)
                {
                    try
                    {
                        if (!IsFileLocked(logFile))
                        {
                            File.Delete(logFile.FullName);
                        }
                    }
                    catch (Exception ex)
                    {
                        Warning($"删除原日志 {logFile.Name} 失败: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Error($"压缩日志组 {dateStr} 失败", ex);
            }
        }
        
        /// <summary>
        /// 检查文件是否被锁定（正在被写入）
        /// </summary>
        private static bool IsFileLocked(FileInfo file)
        {
            try
            {
                using var stream = File.Open(file.FullName, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return false;
            }
            catch (IOException)
            {
                return true;
            }
        }
        
        /// <summary>
        /// 清理30天前的压缩包
        /// </summary>
        private static void CleanupOldArchives()
        {
            if (!Directory.Exists(ArchiveDir))
                return;
            
            var archiveDir = new DirectoryInfo(ArchiveDir);
            var cutoffDate = DateTime.Now.AddDays(-MaxArchiveDays);
            int deletedCount = 0;
            
            foreach (var zipFile in archiveDir.GetFiles("*.zip", SearchOption.TopDirectoryOnly))
            {
                if (zipFile.LastWriteTime < cutoffDate)
                {
                    try
                    {
                        File.Delete(zipFile.FullName);
                        deletedCount++;
                    }
                    catch (Exception ex)
                    {
                        Warning($"删除旧压缩包 {zipFile.Name} 失败: {ex.Message}");
                    }
                }
            }
            
            if (deletedCount > 0)
                Info($"已清理 {deletedCount} 个过期压缩包（>{MaxArchiveDays}天）");
        }
        
        /// <summary>
        /// 强制限制日志总大小
        /// </summary>
        private static void EnforceLogsSizeLimit()
        {
            if (!Directory.Exists(LogsDir))
                return;
            
            long maxSizeBytes = MaxLogsSizeMB * 1024 * 1024;
            
            // 计算当前日志目录大小
            long currentSize = GetDirectorySize(LogsDir);
            
            if (currentSize <= maxSizeBytes)
                return;
            
            Info($"日志总大小 {currentSize / 1024 / 1024}MB 超过限制 {MaxLogsSizeMB}MB，开始清理...");
            
            // 获取所有日志和压缩包，按修改时间排序（旧的在前）
            var allFiles = new List<FileInfo>();
            allFiles.AddRange(new DirectoryInfo(LogsDir).GetFiles("*.log", SearchOption.TopDirectoryOnly));
            allFiles.AddRange(new DirectoryInfo(ArchiveDir).GetFiles("*.zip", SearchOption.TopDirectoryOnly));
            
            var sortedFiles = allFiles.OrderBy(f => f.LastWriteTime).ToList();
            
            // 从最旧的文件开始删除，直到大小合适
            foreach (var file in sortedFiles)
            {
                if (currentSize <= maxSizeBytes)
                    break;
                
                try
                {
                    long fileSize = file.Length;
                    File.Delete(file.FullName);
                    currentSize -= fileSize;
                    Info($"删除日志文件 {file.Name} 以释放空间");
                }
                catch (Exception ex)
                {
                    Warning($"删除日志文件 {file.Name} 失败: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// 计算目录大小
        /// </summary>
        private static long GetDirectorySize(string dirPath)
        {
            long size = 0;
            try
            {
                var dir = new DirectoryInfo(dirPath);
                foreach (var file in dir.GetFiles("*", SearchOption.AllDirectories))
                {
                    size += file.Length;
                }
            }
            catch { }
            return size;
        }
        
        #endregion
        
        /// <summary>
        /// 程序关闭时调用
        /// </summary>
        public static void Shutdown()
        {
            Info("========== 程序关闭 ==========");
        }
    }
}
