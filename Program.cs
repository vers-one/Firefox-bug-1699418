using System.Net;
using System.Net.Mime;
using System.Text;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Primitives;

WebApplication app = WebApplication.Create();
Dictionary<IPAddress, List<LogEntry>> logs = new();

string js = @"<script>console.log(""["" + Date.now() + ""] test"");</script>"; 

string indexPage = @$"<!doctype html><html><head><title>Test for Bug 1699418</title></head><body>
{js}
<h2>Test for <a href=""https://bugzilla.mozilla.org/show_bug.cgi?id=1699418"">Bug 1699418</a></h2>
<div><b>Step 1.</b> Click the ""Set cookie and redirect"" button. This will set the ""mycookie"" cookie to ""myvalue"" and will redirect you to the log page.</div>
<br>
<form method=""post""><button type=""submit"">Set cookie and redirect</button></form>
<br>
<form action=""delete"" method=""post""><button type=""submit"">Delete cookie</button></form>
<br>
<form action=""clear"" method=""post""><button type=""submit"">Clear log</button></form>
</body></html>";

void AddLogEntry(HttpContext context, bool isLogPage = false)
{
    IPAddress ip = context.Connection.RemoteIpAddress;
    if (!logs.TryGetValue(ip, out List<LogEntry> log))
    {
        log = new List<LogEntry>();
        logs.Add(ip, log);
    }
    StringBuilder headers = new();
    headers.AppendLine($"{context.Request.Method} {context.Request.GetDisplayUrl()}<br>");
    bool hasCookie = false;
    foreach (KeyValuePair<string, StringValues> header in context.Request.Headers)
    {
        if (header.Key.StartsWith(":"))
        {
            continue;
        }
        if (header.Key == Enum.GetName(HttpRequestHeader.Cookie))
        {
            hasCookie = true;
        }
        headers.AppendLine($"{header.Key}: {header.Value}<br>");
    }
    log.Add(new()
    {
        RequestDateTime = DateTime.UtcNow,
        Headers = headers.ToString(),
        IsLogPage = isLogPage,
        HasCookie = hasCookie
    });
}

app.MapGet("/", async context =>
{
    AddLogEntry(context);
    context.Response.ContentType = MediaTypeNames.Text.Html;
    await context.Response.WriteAsync(indexPage);
});

app.MapPost("/", async context =>
{
    AddLogEntry(context);
    context.Response.Cookies.Append("mycookie", "myvalue");
    context.Response.Redirect("/log");
    await context.Response.CompleteAsync();
});

app.MapGet("/log", async context =>
{
    AddLogEntry(context, isLogPage: true);
    context.Response.ContentType = MediaTypeNames.Text.Html;
    StringBuilder response = new();
    response.Append(@$"<!doctype html><html><head><title>Test for Bug 1699418</title></head><body>
{js}
<h2>Test for <a href=""https://bugzilla.mozilla.org/show_bug.cgi?id=1699418"">Bug 1699418</a></h2>
<div><b>Step 2.</b> Open Developer Toolbox, switch to the Debugger tab, and open the log page in the left tree (if it's not open already).</div>
<div><b>Step 3.</b> Close Developer Toolbox and refresh the page to see the updated request log.</div>
<div><b>Step 4.</b> Notice that the debugger made a GET request to the log page with no cookies.</div>
<br>
<div>This could be the same as <a href=""https://bugzilla.mozilla.org/show_bug.cgi?id=1161278"">Bug 1161278</a> reported earlier.</div>
<br>
<div style=""font-family: monospace"">");
    List<LogEntry> log = logs[context.Connection.RemoteIpAddress];
    for (int i = 0; i < log.Count; i++)
    {
        LogEntry logEntry = log[i];
        string color = logEntry.HasCookie || !logEntry.IsLogPage ? "ccc" : "fcc";
        response.Append(@$"<div style=""background-color: #{color}; font-weight: bold"">Request #{i + 1} at {logEntry.RequestDateTime:O}</div>");
        response.Append(logEntry.Headers);
        response.Append("<br>");
    }
    response.Append(@"</div>
<div><a href=""/"">Back to the home page<a></div>
</body></html>");
    await context.Response.WriteAsync(response.ToString());
});

app.MapPost("/delete", async context =>
{
    AddLogEntry(context);
    context.Response.Cookies.Delete("mycookie");
    context.Response.Redirect("/");
    await context.Response.CompleteAsync();
});

app.MapPost("/clear", async context =>
{
    IPAddress ip = context.Connection.RemoteIpAddress;
    if (logs.TryGetValue(ip, out List<LogEntry> log))
    {
        log.Clear();
    }
    context.Response.Redirect("/");
    await context.Response.CompleteAsync();
});

app.Run();

class LogEntry
{
    public DateTime RequestDateTime { get; set; }
    public string Headers { get; set; }
    public bool IsLogPage { get; set; }
    public bool HasCookie { get; set; }
}
