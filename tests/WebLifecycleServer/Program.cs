var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:8765");
var app = builder.Build();
app.UseStaticFiles();
app.MapGet("/health", () => Results.Text("ok"));
app.Run();
