var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:8765");
var app = builder.Build();
app.UseStaticFiles();
app.MapGet("/health", () => Results.Text("ok"));
foreach (var service in new[] { "youtube-tv", "disney", "paramount" })
{
    var name = service;
    app.MapGet($"/{name}.html", () => Results.Content($$"""
        <!doctype html><meta charset="utf-8"><title>{{name}} local lifecycle test</title>
        <style>body{background:#13255f;color:white;font:32px Segoe UI;padding:60px}button,a{font:18px Segoe UI;margin:12px;padding:10px}</style>
        <h1>{{name.ToUpperInvariant()}} TEST PAGE</h1><p id="launch"></p>
        <button onclick="window.open('/auth.html','monkey-auth','width=620,height=650')">Open auth popup</button>
        <button onclick="navigator.geolocation.getCurrentPosition(()=>{},()=>{})">Request location</button>
        <a href="/download-test">Test download</a>
        <script>let n=+(localStorage.launches||0)+1;localStorage.launches=n;launch.textContent=`Profile launches: ${n}`;</script>
        """, "text/html"));
}
app.MapGet("/auth.html", () => Results.Content("<!doctype html><title>Auth test</title><style>body{font:24px Segoe UI;padding:40px}</style><h1>Temporary auth window</h1><button onclick='window.close()'>Complete and close</button>", "text/html"));
app.MapGet("/download-test", () => Results.File(System.Text.Encoding.UTF8.GetBytes("Monkey TV download test"), "text/plain", "monkey-test.txt"));
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
