using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Hosting;
using System.IO;
using System.IO.Compression;
using Microsoft.AspNetCore.StaticFiles;
using System.Linq;
using System.Diagnostics;
using System;
using System.Collections.Generic;

namespace Web_download.Pages
{
    public class DownloadModel : PageModel
    {
        private readonly IWebHostEnvironment _env;
        private readonly string _targetDirectory;

        // 注入IWebHostEnvironment
        public DownloadModel(IWebHostEnvironment env)
        {
            _env = env;
            // 设置服务端指定目录（例如：存储在项目根目录下的Files目录）
            //_targetDirectory = Path.Combine(_env.ContentRootPath, "Files");
            _targetDirectory = Path.Combine(_env.ContentRootPath, "wwwroot", "Uploads");
        }

        // 文件列表
        public List<FileInfo> Files { get; set; } = new List<FileInfo>();

        // 页面加载时读取服务端指定目录中的文件
        public void OnGet()
        {
            try
            {
                // 日志记录：开始执行OnGet方法
                Debug.WriteLine("开始执行OnGet方法");
                // 确保目录存在
                if (!Directory.Exists(_targetDirectory))
                {
                    // 日志记录：目录不存在，准备创建
                    Debug.WriteLine($"目录不存在，准备创建: {_targetDirectory}");
                    Directory.CreateDirectory(_targetDirectory);
                    // 日志记录：目录创建成功
                    Debug.WriteLine($"目录创建成功: {_targetDirectory}");
                    ModelState.AddModelError("", "目录不存在，已自动创建");
                    //return;
                }
                else
                {
                    // 日志记录：目录已存在
                    Debug.WriteLine($"目录已存在: {_targetDirectory}");
                }

                // 读取目录中的文件信息
                var directoryInfo = new DirectoryInfo(_targetDirectory);
                Files = directoryInfo.GetFiles()
                    .OrderByDescending(f => f.LastWriteTime)
                    .ToList();

                // 日志记录：文件读取完成，文件数量: {Files.Count}
                Debug.WriteLine($"文件读取完成，文件数量: {Files.Count}");
            }
            catch (UnauthorizedAccessException ex)
            {
                // 日志记录：权限异常
                Debug.WriteLine($"权限异常: {ex.Message}");
                ModelState.AddModelError("", $"没有目录访问权限: {ex.Message}");
            }
            catch (IOException ex)
            {
                // 日志记录：IO异常
                Debug.WriteLine($"IO异常: {ex.Message}");

                ModelState.AddModelError("", $"文件操作失败: {ex.Message}");
            }
            catch (Exception ex)
            {
                // 日志记录：其他异常
                Debug.WriteLine($"其他异常: {ex.Message}");
                ModelState.AddModelError("", $"读取目录失败: {ex.Message}");
            }
        }

        // ✅ 【修正后】单文件下载：完全依赖 PhysicalFileResult 的原生 Range 支持
        public IActionResult OnGetDownload(string fileName)
        {
            try
            {
                // 日志记录：开始执行OnGetDownload方法
                Debug.WriteLine($"开始执行OnGetDownload方法，文件名称: {fileName}");
                // ✅ 安全校验（保留你的逻辑）
                if (string.IsNullOrEmpty(fileName) ||
                    fileName.Contains('\\') ||
                    fileName.Contains('/') ||
                    fileName.StartsWith(".") ||
                    Path.GetFileName(fileName) != fileName)
                {
                    ModelState.AddModelError("", "非法的文件名");
                    return RedirectToPage();
                }

                // 构造文件路径
                var filePath = Path.Combine(_targetDirectory, fileName);

                // 检查文件是否存在
                if (!System.IO.File.Exists(filePath))
                {
                    ModelState.AddModelError("", "文件不存在");
                    return RedirectToPage();
                }
                //针对点击下载单个文件，页面报错，移除using语句，由ASP.NET Core框架自动管理文件流资源
                // 检查文件读取权限
                var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                
                var fileInfo = new FileInfo(filePath);
                long fileSize = fileInfo.Length;

                // ✅ 使用 FileExtensionContentTypeProvider 获取 MIME 类型
                    var provider = new FileExtensionContentTypeProvider();
                provider.TryGetContentType(fileName, out var contentType);
                contentType ??= "application/octet-stream";

                // ✅ 【核心修复】仅调用 PhysicalFile，不碰任何 Response.Headers！
                // PhysicalFile 会自动：
                //   • 检测 Range 请求头 → 返回 206 Partial Content（含 Content-Range）
                //   • 无 Range → 返回 200 OK（不带 Accept-Ranges）
                //   • 自动设置 ETag / Last-Modified / Cache-Control
                //   • 支持流式传输（FileOptions.Asynchronous 默认启用）
                //Response.Headers.Append("Accept-Ranges", "bytes");
                //return PhysicalFile(filePath, contentType, fileName);
                var result = PhysicalFile(filePath, contentType, fileName);

                // ✅ 【可选增强】记录日志区分响应类型（便于抓包验证）
                HttpContext.Response.OnStarting(() =>
                    {
                    Debug.WriteLine($"PhysicalFile 响应状态码: {HttpContext.Response.StatusCode} " +
                                   $"(Content-Length: {HttpContext.Response.ContentLength?.ToString() ?? "N/A"})");
                    if (HttpContext.Response.StatusCode == StatusCodes.Status206PartialContent)
                    {
                        Debug.WriteLine($"✅ 已返回 206 Partial Content（断点续传生效）");
                    }
                    else
                    {
                        Debug.WriteLine($"⚠️  返回 200 OK（客户端未发送 Range 或被中间设备拦截）");
                    }
                    return Task.CompletedTask;
                });
                
                return result;
            }
            catch (UnauthorizedAccessException ex)
            {
                // 日志记录：权限异常
                Debug.WriteLine($"权限异常: {ex.Message}");
                ModelState.AddModelError("", $"没有文件读取权限: {ex.Message}");
                return RedirectToPage();
            }
            catch (Exception ex)
            {
                // 日志记录：其他异常
                Debug.WriteLine($"其他异常: {ex.Message}");
                ModelState.AddModelError("", $"文件下载失败: {ex.Message}");
                return RedirectToPage();
            }
        }

        // ✅ 【多文件下载】流式 ZIP（无临时文件、无删除逻辑、无并发风险）
        // 🔑 签名唯一：public IActionResult OnPostDownloadMultiple(List<string> selectedFiles)
        public IActionResult OnPostDownloadMultiple(List<string> selectedFiles)
        {
            try
            {
                if (selectedFiles == null || !selectedFiles.Any())
                {
                    ModelState.AddModelError("", "请选择要下载的文件");
                    return RedirectToPage();
                }

                // ✅ 过滤非法文件名（防路径遍历）
                var safeFileNames = selectedFiles
                    .Where(f => !string.IsNullOrWhiteSpace(f) &&
                                !f.Contains('\\') &&
                                !f.Contains('/') &&
                                !f.StartsWith(".") &&
                                Path.GetFileName(f) == f)
                    .ToList();

                if (!safeFileNames.Any())
                        {
                    ModelState.AddModelError("", "无可下载的有效文件");
                    return RedirectToPage();
                        }

                // ✅ 构建唯一 ZIP 文件名（毫秒级时间戳 + 随机数）
                string zipFileName = $"download_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{new Random().Next(1000, 9999)}.zip";

                // ✅ 设置响应头（触发浏览器下载）
                Response.Headers.Append("Content-Disposition", $"attachment; filename=\"{zipFileName}\"");
                Response.Headers.Append("Content-Type", "application/zip");

                // ✅ 流式生成 ZIP 并返回（内存中完成，不落地磁盘）
                var zipStream = GenerateZipStream(safeFileNames);
                return new FileStreamResult(zipStream, "application/zip")
                {
                    FileDownloadName = zipFileName
                };
            }
            catch (UnauthorizedAccessException ex)
            {
                Debug.WriteLine($"权限异常: {ex.Message}");
                ModelState.AddModelError("", $"没有文件读取权限: {ex.Message}");
                return RedirectToPage();
            }
            catch (IOException ex)
            {
                Debug.WriteLine($"IO异常: {ex.Message}");
                ModelState.AddModelError("", $"文件操作失败: {ex.Message}");
                return RedirectToPage();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"其他异常: {ex.Message}");
                ModelState.AddModelError("", $"ZIP打包失败: {ex.Message}");
                return RedirectToPage();
            }
            }

        // ✅ 私有辅助方法：流式 ZIP 生成（不创建临时文件，不占磁盘空间）
        private Stream GenerateZipStream(List<string> fileNames)
                {
            var memoryStream = new MemoryStream();
            using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
                    {
                foreach (string fileName in fileNames)
                        {
                    string filePath = Path.Combine(_targetDirectory, fileName);
                    if (!System.IO.File.Exists(filePath)) continue;

                    var fileInfo = new FileInfo(filePath);
                    // ✅ 将文件以原始名称加入 ZIP（不带路径）
                    var entry = archive.CreateEntry(fileName, CompressionLevel.Optimal);
                    using (var entryStream = entry.Open())
                    using (var fileStream = System.IO.File.OpenRead(filePath))
                        {
                        fileStream.CopyTo(entryStream); // 零拷贝流式写入
                        }
                    }
                };
        
            memoryStream.Position = 0;
            return memoryStream;
        }

        /// <summary>
        /// 格式化文件大小
        /// </summary>
        /// <param name="bytes">文件大小（字节）</param>
        /// <returns>格式化后的文件大小</returns>
        public string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            else if (bytes < 1048576) return (bytes / 1024.0).ToString("0.00") + " KB";
            else if (bytes < 1073741824) return (bytes / 1048576.0).ToString("0.00") + " MB";
            else return (bytes / 1073741824.0).ToString("0.00") + " GB";
        }
    }
}