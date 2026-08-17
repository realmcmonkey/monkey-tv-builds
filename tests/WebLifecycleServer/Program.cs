var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:8765");
var app = builder.Build();
app.UseStaticFiles();
app.MapGet("/health", () => Results.Text("ok"));
app.MapGet("/slow/{service}", async (string service) =>
{
    await Task.Delay(TimeSpan.FromSeconds(2));
    var safeName = System.Net.WebUtility.HtmlEncode(service.ToUpperInvariant());
    return Results.Content($$"""
        <!doctype html><meta charset="utf-8"><title>{{safeName}} transition test</title>
        <style>body{background:#13255f;color:white;font:32px Segoe UI;padding:60px}</style>
        <h1>{{safeName}} TEST PAGE</h1><p id="launch"></p>
        <script>let n=+(localStorage.launches||0)+1;localStorage.launches=n;launch.textContent=`Profile launches: ${n}`;</script>
        """, "text/html");
});
app.Run();
