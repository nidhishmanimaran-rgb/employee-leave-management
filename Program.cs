using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var dataProtectionKeysDirectory = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtectionKeys");
Directory.CreateDirectory(dataProtectionKeysDirectory);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysDirectory));

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddSession(options =>
{
    options.Cookie.Name = ".EmployeeLeaveManagementSystem.HrSession";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.IdleTimeout = TimeSpan.FromMinutes(30);
});

// Beginner-friendly helper for sending SMTP emails (MailKit).
builder.Services.AddSingleton<EmployeeLeaveManagementSystem.Services.EmailService>();
builder.Services.AddSingleton<EmployeeLeaveManagementSystem.Services.LeaveRequestStore>();
builder.Services.AddSingleton<EmployeeLeaveManagementSystem.Services.SqlLeaveRequestStore>();
builder.Services.AddSingleton<EmployeeLeaveManagementSystem.Services.AppUserStore>();
builder.Services.AddHttpClient<EmployeeLeaveManagementSystem.Services.PublicHolidayService>((serviceProvider, client) =>
{
    var config = serviceProvider.GetRequiredService<IConfiguration>();
    var baseUrl = config["ExternalApis:PublicHolidays:BaseUrl"] ?? "https://date.nager.at/";
    var timeoutSeconds = config.GetValue<int?>("ExternalApis:PublicHolidays:TimeoutSeconds") ?? 10;

    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 60));
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseSession();
app.UseAuthorization();

// Static files (wwwroot)
app.UseStaticFiles();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Auth}/{action=Login}/{id?}");

// Web API routes (attribute routing inside controllers like [HttpPost])
app.MapControllers();

// Ensure MVC attribute routed actions can be reached reliably
app.MapControllerRoute(
    name: "mvc_default",
    pattern: "{controller}/{action}/{id?}");


app.Run();
