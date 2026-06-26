using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.IO;
using System.Runtime.ConstrainedExecution;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;


var builder = WebApplication.CreateBuilder(args);
//确认 Data Protection 是否初始化失败
builder.Logging.AddConsole().AddFilter("Microsoft.AspNetCore.DataProtection", LogLevel.Debug);

var pfxPassword = Environment.GetEnvironmentVariable("KESTREL_PFX_PASSWORD")
                  ?? "MySecurePfxPassword123!"; // fallback only for dev
// ✅ 添加这行（必须在 app.UseRouting() 之前）
builder.Services.AddHttpsRedirection(options =>
{
    options.RedirectStatusCode = StatusCodes.Status307TemporaryRedirect;
    options.HttpsPort = 443; // 或你实际的 HTTPS 端口
});

// ✅ 【关键修复】确保 Uploads 目录存在（加在这里！）
//var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Uploads");
//Directory.CreateDirectory(uploadsPath);
// 添加Razor Pages服务
builder.Services.AddRazorPages();
// 注入IWebHostEnvironment
builder.Services.AddSingleton<IWebHostEnvironment>(builder.Environment);
// ✅ 1. 添加 Razor Pages 服务
// Add services to the container.
builder.Services.AddRazorPages().AddRazorPagesOptions(options =>
{
    options.Conventions.Clear();
    //options.RootDirectory = "/Pages";
    // 👇 关键：移除 Index.cshtml 对根路径 "/" 的默认映射
    options.Conventions.AddPageRoute("/Download", "");//将Download.cshtml改为默认起始页,需手动删除Index.cshtml，否则起冲突
    options.Conventions.AddPageRoute("/Error", "/Error"); // 保留错误页（必需！）
});

// ✅ 【关键】配置 Data Protection 使用固定目录（推荐放在 wwwroot 同级或专用目录）
var keysPath = Path.Combine(builder.Environment.ContentRootPath, "DataProtection-Keys");
Directory.CreateDirectory(keysPath); // 自动创建目录
// ✅ 强制将密钥存到应用本地目录（无需管理员权限）
builder.Services.AddDataProtection()
    .SetApplicationName("Web_download") // 命名隔离（避免多应用冲突）
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath)); // 自动创建目录

//在加载证书前打印实际解析路径
//var certPath = Path.Combine(builder.Environment.ContentRootPath, "DataProtection-Keys"); // ✅ 推荐放项目根
//Debug.WriteLine($"Certificate path: {certPath} -> Exists: {File.Exists(certPath)}");
//if (!File.Exists(certPath))
//    throw new FileNotFoundException($"Certificate not found: {certPath}");
var PFXPath = Path.Combine(builder.Environment.ContentRootPath, "smydownload.pfx");
//Directory.CreateDirectory(PFXPath);
var certPassword = "MySecurePfxPassword123!"; 
// ✅ 步骤1：显式验证文件存在 & 可读
if (!File.Exists(PFXPath))
{
    throw new InvalidOperationException($"HTTPS 证书文件未找到：{PFXPath}。请确认文件已部署到应用根目录。");
}

// ✅ 步骤2：尝试加载证书（捕获具体异常）
X509Certificate2? certificate = null;
try
{
    //certificate = new X509Certificate2(PFXPath, certPassword,
    // 从文件加载 PKCS12
    var cert = X509CertificateLoader.LoadPkcs12FromFile(
    "smydownload.pfx",
    certPassword
    );
    //X509KeyStorageFlags.DefaultKeySet // 可选：指定密钥存储标志
    // ✅ 安全查找 KeyUsage 扩展（OID: 2.5.29.15）
    var keyUsageExt = cert.Extensions
        .OfType<X509KeyUsageExtension>()
        .FirstOrDefault();

    if (keyUsageExt == null)
    {
        Console.WriteLine("⚠️ 证书不包含 Key Usage 扩展 → 默认策略：RFC 5280 规定‘无扩展’时密钥用途不受限，但实践中应谨慎对待。");
        // 👉 建议：按业务策略决定是否允许（如：CA 证书通常需显式声明）
    }
    else
    {
        // ✅ Step 2：读取 KeyUsages 标志
        X509KeyUsageFlags usages = keyUsageExt.KeyUsages; // [5]()

        // ✅ Step 3：检查 DigitalSignature 标志（位与运算）
        bool supportsDigitalSignature = usages.HasFlag(X509KeyUsageFlags.DigitalSignature);

        Console.WriteLine($"Key Usages: {usages}"); // 输出如：DigitalSignature, KeyEncipherment
        Console.WriteLine($"支持 DigitalSignature: {supportsDigitalSignature}"); // true / false
    }
}
catch (CryptographicException ex) when (ex.Message.Contains("找不到指定的文件"))
{
    throw new InvalidOperationException(
        $"证书加载失败：无法访问私钥。请检查：\n" +
        $"• 密码是否正确（当前传入：'{certPassword}'）\n" +
        $"• Windows 下是否为运行用户授予了私钥 ACL 权限\n" +
        $"• Linux/macOS 下是否对 '{PFXPath}' 设置了读取权限（chmod 600）", ex);
}
catch (Exception ex)
{
    throw new InvalidOperationException($"证书加载失败：{ex.Message}", ex);
}




// ✅ 在 builder.Build() 之前添加 
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    //serverOptions.ListenAnyIP(80); // HTTP
    serverOptions.ListenAnyIP(443, listenOptions =>
    {
        listenOptions.UseHttps("smydownload.pfx",
            "MySecurePfxPassword123!"); // 后续生成
    });
    // 🔑 关键：绑定到 0.0.0.0（所有接口），端口 7153
    serverOptions.ListenAnyIP(7153, listenOptions =>
    {
        //    // ✅ 加载 mkcert 生成的证书（路径务必正确！）
        listenOptions.UseHttps(
        //    //"C:\\certificate\\192.168.3.2.pem",      // ← 替换为你的 .pem 路径
        //    //"C:\\certificate\\192.168.3.2-key.pem"   // ← 替换为你的 -key.pem 路径
            "smydownload.pfx",
            "MySecurePfxPassword123!"
            );
        listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
     
        // 显式加载 PKCS12 格式
        //    var cert = X509CertificateLoader.LoadPkcs12FromFile(
        //    "server.pfx",
        //    "MySecurePfxPassword123!",
        //    X509KeyStorageFlags.DefaultKeySet // 可选：指定密钥存储标志
        //);

    });



    //serverOptions.ConfigureHttpsDefaults(httpsOptions =>
    //{
    //    httpsOptions.ServerCertificate =
    //        new X509Certificate2("server.pfx", pfxPassword);
    //    // 可选：启用客户端证书验证（零信任场景）
    //    // httpsOptions.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
    //});


    //    try
    //    {
    //        // 读取证书 PEM（公钥）
    //        var certPem = File.ReadAllText("C:\\certificate\\192.168.3.2.pem");
    //        // 读取私钥 PEM（必须是 PKCS#8 格式，mkcert 生成的就是）
    //        var keyPem = File.ReadAllText("C:\\certificate\\192.168.3.2-key.pem");

    //        // 合并为 X509Certificate2（自动识别 PEM 中的 cert + key）
    //        var cert = new X509Certificate2(
    //            Encoding.UTF8.GetBytes(certPem + keyPem), // ✅ 关键：拼接！
    //            "", // 无密码（mkcert 私钥无密码）
    //            X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.MachineKeySet
    //           );
    //        listenOptions.UseHttps(cert);
    //        listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
    //    }
    //    catch (Exception ex)
    //    {
    //        Console.WriteLine($"❌ HTTPS 加载失败: {ex}");
    //        Environment.Exit(1);
    //    }
    //});
    //serverOptions.ListenAnyIP(5057, listenOptions =>
    //    {
    //        listenOptions.Protocols = HttpProtocols.Http1;
    //    });

    // 🔑 允许 HTTP（可选，仅调试用）
    serverOptions.ListenAnyIP(5057, listenOptions =>
        {
            //listenOptions.UseHttpServer();
            listenOptions.Protocols = HttpProtocols.Http1; // 可选：明确协议
        });
    });

var app = builder.Build();
// ✅ 2. HTTP Pipeline：严格按照官方推荐顺序
// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}
app.UseHttpsRedirection();
// ✅ 3. 【关键】静态文件中间件：只注册一次，且在 UseRouting() 之后！
// 配置默认静态文件目录（wwwroot）
app.UseStaticFiles();// ← 启用默认 wwwroot（/js, /css 等）

// 修复：确保Files目录存在，然后再配置静态文件目录
//var filesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Uploads");
//if (!Directory.Exists(filesDirectory))
//{
//    // 创建Files目录
//    Directory.CreateDirectory(filesDirectory);
//}

//// 配置自定义静态文件目录（可选，适用于存储在项目根目录下的文件）
//app.UseStaticFiles(new StaticFileOptions
//{
//    FileProvider = new PhysicalFileProvider(filesDirectory),
//    RequestPath = "/Uploads" // 访问路径
//});
// ✅ 4. 【核心修复】自定义 Uploads 目录映射（必须在 UseRouting() 之前？不！必须在之后！见下方）
// ⚠️ 注意：UseStaticFiles() 必须在 UseRouting() 之后！我们稍后放对位置。
app.UseRouting();
// ✅ 5. 【正确位置】注册 /Uploads 映射（在 UseRouting() 之后，UseEndpoints() 之前）
var uploadsPath = Path.Combine(app.Environment.WebRootPath, "Uploads");
Directory.CreateDirectory(uploadsPath); // 确保目录存在（即使为空）

// 配置自定义静态文件目录（可选，适用于存储在项目根目录下的文件）
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsPath),
    RequestPath = "/Uploads",
    // ✅ 启用 Range 支持（默认已开启，但显式声明更清晰）
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.Append("Accept-Ranges", "bytes");
        // 可选：强制缓存策略
        // ctx.Context.Response.Headers.Append("Cache-Control", "public, max-age=31536000");
    }
});

app.UseRouting();

app.UseAuthorization();
// ✅ 6. Map endpoints 以上关键注册点在纳米AI中有具体分析
app.MapStaticAssets();// 如使用 Microsoft.AspNetCore.StaticFiles 的扩展方法
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
