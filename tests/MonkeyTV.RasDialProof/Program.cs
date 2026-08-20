using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

if (args.Length is < 1 or > 2) { Console.Error.WriteLine("Usage: MonkeyTV.RasDialProof <entry-name> [--cycle-prefix=<name>]"); return 2; }
var entryName = args[0];
var cyclePrefix = args.Skip(1).FirstOrDefault(value => value.StartsWith("--cycle-prefix=", StringComparison.OrdinalIgnoreCase))?["--cycle-prefix=".Length..];
const uint ErrorBufferTooSmall = 603;
Console.WriteLine($"Process={Environment.ProcessPath}");
Console.WriteLine($"Architecture={RuntimeInformation.ProcessArchitecture}; PointerSize={IntPtr.Size}");
Console.WriteLine($"RASDIALPARAMS.Size={Marshal.SizeOf<RasDialParams>()}; dwCallbackId.Offset={Marshal.OffsetOf<RasDialParams>(nameof(RasDialParams.dwCallbackId))}; dwIfIndex.Offset={Marshal.OffsetOf<RasDialParams>(nameof(RasDialParams.dwIfIndex))}");
Console.WriteLine($"RASENTRYNAME.Size={Marshal.SizeOf<RasEntryName>()}");

uint enumBytes = 0;
var enumResult = Native.RasEnumEntries(null, null, IntPtr.Zero, ref enumBytes, out var enumCount);
Log("RasEnumEntriesW(size)", enumResult, $"bytes={enumBytes}; count={enumCount}");
if (enumResult is not 0 and not ErrorBufferTooSmall) return 3;
var entries = Marshal.AllocHGlobal((int)enumBytes);
try
{
    Marshal.WriteInt32(entries, Marshal.SizeOf<RasEntryName>());
    enumResult = Native.RasEnumEntries(null, null, entries, ref enumBytes, out enumCount);
    Log("RasEnumEntriesW(data)", enumResult, $"bytes={enumBytes}; count={enumCount}");
    if (enumResult != 0) return 4;
    var stride = Marshal.SizeOf<RasEntryName>();
    var found = false;
    for (var index = 0; index < enumCount; index++)
    {
        var entry = Marshal.PtrToStructure<RasEntryName>(entries + index * stride);
        if (!string.Equals(entry.szEntryName, entryName, StringComparison.OrdinalIgnoreCase)) continue;
        found = true;
        Console.WriteLine($"ENTRY name={entry.szEntryName}; flags=0x{entry.dwFlags:X8}; phonebook={entry.szPhonebookPath}");
    }
    if (!found) { Console.Error.WriteLine("ENTRY_NOT_FOUND"); return 5; }
}
finally { Marshal.FreeHGlobal(entries); }

uint entryBytes = 0, deviceBytes = 0;
var propertiesResult = Native.RasGetEntryProperties(null, entryName, IntPtr.Zero, ref entryBytes, IntPtr.Zero, ref deviceBytes);
Log("RasGetEntryPropertiesW(size)", propertiesResult, $"entryBytes={entryBytes}; deviceBytes={deviceBytes}");
if (propertiesResult is not 0 and not ErrorBufferTooSmall) return 6;

var credentialInfo = new CredUiInfo { cbSize = Marshal.SizeOf<CredUiInfo>(), pszCaptionText = "Monkey TV IKEv2 feasibility spike", pszMessageText = "Enter Nord service credentials. Nothing is saved." };
var username = new StringBuilder(257);
var password = new StringBuilder(257);
var save = false;
var promptResult = Native.CredUIPromptForCredentials(ref credentialInfo, "MonkeyTV-v2-Nord-IKEv2", IntPtr.Zero, 0, username, username.Capacity, password, password.Capacity, ref save, 0x0004008A);
Log("CredUIPromptForCredentialsW", promptResult, "password-not-logged; persist=false");
if (promptResult != 0) return 7;

var dial = new RasDialParams
{
    dwSize = Marshal.SizeOf<RasDialParams>(), szEntryName = entryName,
    szPhoneNumber = "", szCallbackNumber = "", szUserName = username.ToString(),
    szPassword = password.ToString(), szDomain = "", dwSubEntry = 0, dwCallbackId = IntPtr.Zero, dwIfIndex = 0
};
password.Clear(); username.Clear();
var dialResult = Native.RasDial(IntPtr.Zero, null, ref dial, 0, IntPtr.Zero, out var connection);
Log("RasDialW", dialResult, $"handle={(connection == IntPtr.Zero ? "null" : "non-null")}");
if (dialResult != 0)
{
    if (connection != IntPtr.Zero) Log("RasHangUpW(after-failure)", Native.RasHangUp(connection), "cleanup");
    dial.szPassword = ""; dial.szUserName = "";
    return 8;
}
Console.WriteLine("AUTHENTICATION_AND_DIAL=PASS");
if (!string.IsNullOrWhiteSpace(cyclePrefix))
{
    using var connected1 = new EventWaitHandle(false, EventResetMode.ManualReset, cyclePrefix + "-Connected1");
    using var disconnect = new EventWaitHandle(false, EventResetMode.ManualReset, cyclePrefix + "-Disconnect");
    using var disconnected = new EventWaitHandle(false, EventResetMode.ManualReset, cyclePrefix + "-Disconnected");
    using var reconnect = new EventWaitHandle(false, EventResetMode.ManualReset, cyclePrefix + "-Reconnect");
    using var connected2 = new EventWaitHandle(false, EventResetMode.ManualReset, cyclePrefix + "-Connected2");
    using var reconnectFailed = new EventWaitHandle(false, EventResetMode.ManualReset, cyclePrefix + "-ReconnectFailed");
    using var finish = new EventWaitHandle(false, EventResetMode.ManualReset, cyclePrefix + "-Finish");
    connected1.Set();
    disconnect.WaitOne();
    if (connection != IntPtr.Zero) Log("RasHangUpW(tunnel-loss)", Native.RasHangUp(connection), "controlled disconnect");
    connection = IntPtr.Zero;
    disconnected.Set();
    reconnect.WaitOne();
    var reconnectResult = Native.RasDial(IntPtr.Zero, null, ref dial, 0, IntPtr.Zero, out connection);
    Log("RasDialW(reconnect)", reconnectResult, $"handle={(connection == IntPtr.Zero ? "null" : "non-null")}");
    if (reconnectResult == 0) connected2.Set(); else reconnectFailed.Set();
    finish.WaitOne();
    if (connection != IntPtr.Zero) Log("RasHangUpW(final)", Native.RasHangUp(connection), "cleanup");
    dial.szPassword = ""; dial.szUserName = "";
    return reconnectResult == 0 ? 0 : 9;
}
if (connection != IntPtr.Zero) Log("RasHangUpW", Native.RasHangUp(connection), "cleanup");
dial.szPassword = ""; dial.szUserName = "";
return 0;

static void Log(string name, uint code, string detail) => Console.WriteLine($"{name}=0x{code:X8} ({(code == 0 ? "SUCCESS" : new Win32Exception((int)code).Message)}); {detail}");

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
struct RasDialParams
{
    public int dwSize;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string szEntryName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 129)] public string szPhoneNumber;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 129)] public string szCallbackNumber;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string szUserName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string szPassword;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)] public string szDomain;
    public int dwSubEntry;
    public IntPtr dwCallbackId;
    public int dwIfIndex;
}
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
struct RasEntryName
{
    public int dwSize;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string szEntryName;
    public int dwFlags;
    // ras.h declares szPhonebookPath[MAX_PATH + 1], where MAX_PATH is 260.
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 261)] public string szPhonebookPath;
}
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
struct CredUiInfo { public int cbSize; public IntPtr hwndParent; public string pszMessageText; public string pszCaptionText; public IntPtr hbmBanner; }

static class Native
{
    [DllImport("rasapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RasEnumEntriesW")]
    internal static extern uint RasEnumEntries(string? reserved, string? phonebook, IntPtr entries, ref uint bytes, out uint count);
    [DllImport("rasapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RasGetEntryPropertiesW")]
    internal static extern uint RasGetEntryProperties(string? phonebook, string entry, IntPtr entryInfo, ref uint entryBytes, IntPtr deviceInfo, ref uint deviceBytes);
    [DllImport("rasapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RasDialW")]
    internal static extern uint RasDial(IntPtr extensions, string? phonebook, ref RasDialParams parameters, uint notifierType, IntPtr notifier, out IntPtr connection);
    [DllImport("rasapi32.dll", EntryPoint = "RasHangUpW")] internal static extern uint RasHangUp(IntPtr connection);
    [DllImport("credui.dll", CharSet = CharSet.Unicode, EntryPoint = "CredUIPromptForCredentialsW")]
    internal static extern uint CredUIPromptForCredentials(ref CredUiInfo info, string target, IntPtr reserved, uint authError, StringBuilder username, int usernameMax, StringBuilder password, int passwordMax, ref bool save, uint flags);
}
