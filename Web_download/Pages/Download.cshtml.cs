using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Hosting;
using System.IO;
using System.IO.Compression;
using Microsoft.AspNetCore.StaticFiles;
using System.Linq;
using System.Diagnostics; // 引入日志命名空间

namespace Web_download.Pages
{
    public class DownloadModel : PageModel
    {
        private readonly IWebHostEnvironment _env;
        private readonly string _targetDirectory; // 服务端指定目录

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
                    .OrderByDescending(f => f.LastWriteTime) // 按修改时间降序排列
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

        // 单文件下载
        public IActionResult OnGetDownload(string fileName)
        {
            try
            {
                // 日志记录：开始执行OnGetDownload方法
                Debug.WriteLine($"开始执行OnGetDownload方法，文件名称: {fileName}");
                // 验证文件名，防止路径遍历攻击
                if (string.IsNullOrEmpty(fileName) || fileName.Contains('\\') || fileName.Contains('/'))
                {
                    ModelState.AddModelError("", "非法的文件名");
                    return RedirectToPage(); // 返回文件管理器页面
                }

                // 构造文件路径
                var filePath = Path.Combine(_targetDirectory, fileName);

                // 检查文件是否存在
                if (!System.IO.File.Exists(filePath))
                {
                    ModelState.AddModelError("", "文件不存在");
                    return RedirectToPage(); // 返回文件管理器页面
                }
                //针对点击下载单个文件，页面报错，移除using语句，由ASP.NET Core框架自动管理文件流资源
                // 检查文件读取权限
                var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                
                    // 获取文件MIME类型
                    var provider = new FileExtensionContentTypeProvider();
                    if (!provider.TryGetContentType(fileName, out var contentType))
                    {
                        contentType = "application/octet-stream"; // 默认MIME类型
                    }

                    // 返回文件流
                    return new FileStreamResult(stream, contentType)
                    {
                        FileDownloadName = fileName // 指定下载文件名
                    };
                
            }
            catch (UnauthorizedAccessException ex)
            {
                // 日志记录：权限异常
                Debug.WriteLine($"权限异常: {ex.Message}");
                ModelState.AddModelError("", $"没有文件读取权限: {ex.Message}");
                return RedirectToPage(); // 返回文件管理器页面
            }
            catch (Exception ex)
            {
                // 日志记录：其他异常
                Debug.WriteLine($"其他异常: {ex.Message}");
                ModelState.AddModelError("", $"文件下载失败: {ex.Message}");
                return RedirectToPage(); // 返回文件管理器页面
            }
        }

        // 多文件打包下载
        public IActionResult OnPostDownloadMultiple(List<string> selectedFiles)
        {
            try
            {
                // 验证输入参数
                if (selectedFiles == null || selectedFiles.Count == 0)
                {
                    ModelState.AddModelError("", "请选择要下载的文件");
                    return RedirectToPage(); // 返回文件管理器页面
                }

                // 创建临时ZIP文件
                var zipFileName = $"download_{DateTime.Now:yyyyMMddHHmmss}.zip";
                var tempDir = Path.Combine(_env.ContentRootPath, "Temp"); // 使用项目根目录下的Temp目录
                var zipFilePath = Path.Combine(tempDir, zipFileName);

                // 确保临时目录存在
                if (!Directory.Exists(tempDir))
                {
                    Directory.CreateDirectory(tempDir);
                }

                // 将多个文件打包成ZIP文件
                using (var zipArchive = ZipFile.Open(zipFilePath, ZipArchiveMode.Create))
                {
                    foreach (var fileName in selectedFiles)
                    {
                        // 验证文件名，防止路径遍历攻击
                        if (string.IsNullOrEmpty(fileName) || fileName.Contains('\\') || fileName.Contains('/'))
                        {
                            continue; // 跳过非法文件名
                        }

                        var filePath = Path.Combine(_targetDirectory, fileName);
                        if (System.IO.File.Exists(filePath))
                        {
                            // 将文件添加到ZIP存档
                            zipArchive.CreateEntryFromFile(filePath, fileName);
                        }
                    }
                }

                // 检查ZIP文件是否创建成功
                if (!System.IO.File.Exists(zipFilePath))
                {
                    ModelState.AddModelError("", "ZIP文件创建失败");
                    return RedirectToPage(); // 返回文件管理器页面
                }

        // 移除using语句，由ASP.NET Core框架自动管理文件流资源
        // 检查文件读取权限
        var stream = new FileStream(zipFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        // 返回ZIP文件下载响应
        var contentType = "application/zip";
                // 返回文件流
                return new FileStreamResult(stream, contentType)
                {
                    FileDownloadName = zipFileName // 指定下载文件名
                };
            }
            catch (UnauthorizedAccessException ex)
            {
                ModelState.AddModelError("", $"没有文件读写权限: {ex.Message}");
                return RedirectToPage(); // 返回文件管理器页面
            }
            catch (IOException ex)
            {
                ModelState.AddModelError("", $"文件操作失败: {ex.Message}");
                return RedirectToPage(); // 返回文件管理器页面
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"ZIP文件创建失败: {ex.Message}");
                return RedirectToPage(); // 返回文件管理器页面
            }
            finally
            {
                // 异步删除临时ZIP文件（延迟删除，避免返回响应前删除文件）
                Task.Run(async () =>
                {
                    await Task.Delay(5000); // 延迟5秒删除临时文件
                    var tempDir = Path.Combine(_env.ContentRootPath, "Temp");
                    var zipFileName = $"download_{DateTime.Now:yyyyMMddHHmmss}.zip";
                    var zipFilePath = Path.Combine(tempDir, zipFileName);
                    if (System.IO.File.Exists(zipFilePath))
                    {
                        try
                        {
                            System.IO.File.Delete(zipFilePath);
                        }
                        catch
                        {
                            // 忽略删除失败的情况
                        }
                    }
                });
            }
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