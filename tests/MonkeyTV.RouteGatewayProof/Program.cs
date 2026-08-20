using System.Net.NetworkInformation;

if (args.Contains("--proxy", StringComparer.OrdinalIgnoreCase))
{
    var finishName = args.FirstOrDefault(value => value.StartsWith("--finish-event=", StringComparison.OrdinalIgnoreCase))?["--finish-event=".Length..]
        ?? throw new ArgumentException("--finish-event is required in proxy mode.");
    await using var proxy = new GatewayProxy();
    proxy.Start();
    Console.WriteLine($"GATEWAY_PORT={proxy.Port}");
    Console.Out.Flush();
    using var finish = new EventWaitHandle(false, EventResetMode.ManualReset, finishName);
    await Task.Run(() => finish.WaitOne());
    Console.WriteLine($"GATEWAY_ATTEMPTS={proxy.AttemptCount}");
    return 0;
}

var target = args.FirstOrDefault() ?? "https://api.ipify.org";
using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
try
{
    var body = await client.GetStringAsync(target);
    Console.WriteLine($"SUCCESS {body.Trim()}");
    foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces().Where(value => value.OperationalStatus == OperationalStatus.Up))
        Console.WriteLine($"INTERFACE {adapter.Id} {adapter.Name} {adapter.NetworkInterfaceType}");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FAIL {ex.GetType().Name}: {ex.Message}");
    return 1;
}
