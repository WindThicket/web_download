using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.IO;
using Microsoft.Extensions.FileProviders;


var builder = WebApplication.CreateBuilder(args);
// ✅ 【关键修复】确保 Uploads 目录存在（加在这里！）
//var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Uploads");
//Directory.CreateDirectory(uploadsPath);
// 添加Razor Pages服务
builder.Services.AddRazorPages();
// 注入IWebHostEnvironment
builder.Services.AddSingleton<IWebHostEnvironment>(builder.Environment);
// Add services to the container.
builder.Services.AddRazorPages().AddRazorPagesOptions(options =>
{
    options.Conventions.Clear();
    //options.RootDirectory = "/Pages";
    // 👇 关键：移除 Index.cshtml 对根路径 "/" 的默认映射
    options.Conventions.AddPageRoute("/Download", "");//将Download.cshtml改为默认起始页,需手动删除Index.cshtml，否则起冲突
    options.Conventions.AddPageRoute("/Error", "/Error"); // 保留错误页（必需！）
});


var app = builder.Build();

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

// 配置默认静态文件目录（wwwroot）
app.UseStaticFiles();

// 修复：确保Files目录存在，然后再配置静态文件目录
var filesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Uploads");
if (!Directory.Exists(filesDirectory))
{
    // 创建Files目录
    Directory.CreateDirectory(filesDirectory);
}

// 配置自定义静态文件目录（可选，适用于存储在项目根目录下的文件）
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(filesDirectory),
    RequestPath = "/Uploads" // 访问路径
});

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
