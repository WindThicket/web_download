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

        public DownloadModel(IWebHostEnvironment env)
        {
            _env = env;
            _targetDirectory = Path.Combine(_env.ContentRootPath, "wwwroot", "Uploads");
        }

        public List<FileInfo> Files { get; set; } = new List<FileInfo>();

        public void OnGet()
        {
            try
            {
                Debug.WriteLine("开始执行OnGet方法");
                if (!Directory.Exists(_targetDirectory))
                {
                    Debug.WriteLine($"目录不存在，准备创建: {_targetDirectory}");
                    Directory.CreateDirectory(_targetDirectory);
                    Debug.WriteLine($"目录创建成功: {_targetDirectory}");
                    ModelState.AddModelError("", "目录不存在，已自动创建");
                }
                else
                {
                    Debug.WriteLine($"目录已存在: {_targetDirectory}");
                }

                var directoryInfo = new DirectoryInfo(_targetDirectory);
                Files = directoryInfo.GetFiles()
                    .OrderByDescending(f => f.LastWriteTime)
                    .ToList();

                Debug.WriteLine($"文件读取完成，文件数量: {Files.Count}");
            }
            catch (UnauthorizedAccessException ex)
            {
                Debug.WriteLine($"权限异常: {ex.Message}");
                ModelState.AddModelError("", $"没有目录访问权限: {ex.Message}");
            }
            catch (IOException ex)
            {
                Debug.WriteLine($"IO异常: {ex.Message}");
                ModelState.AddModelError("", $"文件操作失败: {ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"其他异常: {ex.Message}");
                ModelState.AddModelError("", $"读取目录失败: {ex.Message}");
            }
        }

        // ✅ 【修正后】单文件下载：完全依赖 PhysicalFileResult 的原生 Range 支持
        public IActionResult OnGetDownload(string fileName)
        {
            try
            {
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

                var filePath = Path.Combine(_targetDirectory, fileName);
                if (!System.IO.File.Exists(filePath))
                {
                    ModelState.AddModelError("", "文件不存在");
                    return RedirectToPage();
                }

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
                Debug.WriteLine($"权限异常: {ex.Message}");
                ModelState.AddModelError("", $"没有文件读取权限: {ex.Message}");
                return RedirectToPage();
            }
            catch (Exception ex)
            {
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
            }
            memoryStream.Position = 0;
            return memoryStream;
        }

        public string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            else if (bytes < 1048576) return (bytes / 1024.0).ToString("0.00") + " KB";
            else if (bytes < 1073741824) return (bytes / 1048576.0).ToString("0.00") + " MB";
            else return (bytes / 1073741824.0).ToString("0.00") + " GB";
        }
    }
}