using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MonkeyTV.VpnProxyProof;

internal sealed class WfpContainmentSpike : IDisposable
{
    private const uint RpcCAuthnWinnt = 10, DynamicSession = 1, FwpUint8 = 1, FwpByteBlobType = 12;
    private const uint FwpActionBlock = 0x1001, FwpEFilterNotFound = 0x80320003, FwpESublayerNotFound = 0x80320007;
    private static readonly Guid AleAuthConnectV4 = new("c38d57d1-05a7-4c33-904f-7fbceee60e82");
    private static readonly Guid AleAuthConnectV6 = new("4a72393b-319f-44bc-84c3-ba54dcb3b6b4");
    private static readonly Guid AleAppId = new("d78e1e87-8644-4ea5-9437-d809ecefc971");
    private static readonly Guid IpRemoteAddress = new("b235ae9a-1d64-49b8-a44c-5ff3d9095045");
    private readonly IntPtr _engine;
    private readonly Guid _sublayerKey, _filterKey;
    private readonly Action<string> _log;
    private bool _disposed;

    private WfpContainmentSpike(IntPtr engine, Guid sublayerKey, Guid filterKey, Action<string> log) =>
        (_engine, _sublayerKey, _filterKey, _log) = (engine, sublayerKey, filterKey, log);

    public static WfpContainmentSpike InstallMinimalIPv4Block(string executablePath, Action<string> log, bool completePolicy = false, bool ipv6Only = false)
    {
        IntPtr engine = IntPtr.Zero, appId = IntPtr.Zero;
        var transactionOpen = false;
        var sublayerKey = Guid.NewGuid();
        var filterKey = Guid.NewGuid();
        try
        {
            var session = new FwpmSession0
            {
                displayData = new() { name = "Monkey TV v2 disposable WFP session", description = "Feasibility spike only" },
                flags = DynamicSession,
                processId = (uint)Environment.ProcessId
            };
            Check("FwpmEngineOpen0", FwpmEngineOpen0(null, RpcCAuthnWinnt, IntPtr.Zero, ref session, out engine), log);
            Check("FwpmTransactionBegin0", FwpmTransactionBegin0(engine, 0), log);
            transactionOpen = true;

            var sublayer = new FwpmSublayer0
            {
                subLayerKey = sublayerKey,
                displayData = new() { name = "Monkey TV v2 minimal dynamic sublayer", description = "Removed with dynamic session" },
                weight = 0x8000
            };
            Check("FwpmSubLayerAdd0", FwpmSubLayerAdd0(engine, ref sublayer, IntPtr.Zero), log);
            Check("FwpmGetAppIdFromFileName0", FwpmGetAppIdFromFileName0(executablePath, out appId), log);
            var blob = Marshal.PtrToStructure<FwpByteBlob>(appId);
            log($"[WFP] app ID blob: {blob.size} bytes for {executablePath}");

            AddAppFilter(engine, ipv6Only ? AleAuthConnectV6 : AleAuthConnectV4, sublayerKey, filterKey, appId, FwpActionBlock, 8,
                ipv6Only ? "block all RouteGateway IPv6" : "minimal Fixed WebView2 IPv4 block", null, log);
            if (completePolicy)
            {
                AddAppFilter(engine, AleAuthConnectV4, sublayerKey, Guid.NewGuid(), appId, 0x1002, 15,
                    "allow IPv4 loopback gateway", FwpConditionValue0.UInt32(0x7f000001), log);
                var loopbackV6 = Marshal.AllocHGlobal(16);
                try
                {
                    var bytes = new byte[16]; bytes[15] = 1; Marshal.Copy(bytes, 0, loopbackV6, 16);
                    AddAppFilter(engine, AleAuthConnectV6, sublayerKey, Guid.NewGuid(), appId, 0x1002, 15,
                        "allow IPv6 loopback gateway", FwpConditionValue0.ByteArray16(loopbackV6), log);
                }
                finally { Marshal.FreeHGlobal(loopbackV6); }
                AddAppFilter(engine, AleAuthConnectV6, sublayerKey, Guid.NewGuid(), appId, FwpActionBlock, 8,
                    "block all direct IPv6", null, log);
            }

            Check("FwpmTransactionCommit0", FwpmTransactionCommit0(engine), log);
            transactionOpen = false;
            VerifyPresent(engine, filterKey, log);
            return new(engine, sublayerKey, filterKey, log);
        }
        catch
        {
            if (transactionOpen && engine != IntPtr.Zero) Log("FwpmTransactionAbort0", FwpmTransactionAbort0(engine), log);
            if (engine != IntPtr.Zero) Log("FwpmEngineClose0", FwpmEngineClose0(engine), log);
            throw;
        }
        finally
        {
            if (appId != IntPtr.Zero) { FwpmFreeMemory0(ref appId); log("[WFP] FwpmFreeMemory0(app ID) => success"); }
        }
    }

    private static void AddAppFilter(IntPtr engine, Guid layer, Guid sublayer, Guid filterKey, IntPtr appId,
        uint action, byte weight, string name, FwpConditionValue0? remoteAddress, Action<string> log)
    {
        var conditions = new List<FwpmFilterCondition0>
        {
            new() { fieldKey = AleAppId, matchType = 0, conditionValue = FwpConditionValue0.ByteBlob(appId) }
        };
        if (remoteAddress is { } address)
            conditions.Add(new() { fieldKey = IpRemoteAddress, matchType = 0, conditionValue = address });
        var size = Marshal.SizeOf<FwpmFilterCondition0>();
        var memory = Marshal.AllocHGlobal(size * conditions.Count);
        try
        {
            for (var i = 0; i < conditions.Count; i++) Marshal.StructureToPtr(conditions[i], memory + i * size, false);
            var filter = new FwpmFilter0
            {
                filterKey = filterKey,
                displayData = new() { name = "Monkey TV v2 " + name, description = "Disposable WFP containment proof" },
                layerKey = layer, subLayerKey = sublayer, weight = FwpValue0.UInt8(weight),
                numFilterConditions = (uint)conditions.Count, filterCondition = memory,
                action = new() { type = action }
            };
            Check($"FwpmFilterAdd0({name})", FwpmFilterAdd0(engine, ref filter, IntPtr.Zero, out var id), log);
            log($"[WFP] filter ID: {id}; key: {filterKey:D}");
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Log("FwpmEngineClose0", FwpmEngineClose0(_engine), _log);
        VerifyAbsent(_filterKey, _sublayerKey, _log);
    }

    private static void VerifyPresent(IntPtr engine, Guid key, Action<string> log)
    {
        IntPtr value = IntPtr.Zero;
        Check("FwpmFilterGetByKey0(verify present)", FwpmFilterGetByKey0(engine, ref key, out value), log);
        if (value != IntPtr.Zero) FwpmFreeMemory0(ref value);
        log("[WFP] PASS: committed filter is visible.");
    }

    private static void VerifyAbsent(Guid filterKey, Guid sublayerKey, Action<string> log)
    {
        var session = new FwpmSession0 { displayData = new() { name = "Monkey TV v2 cleanup verifier" }, processId = (uint)Environment.ProcessId };
        IntPtr engine = IntPtr.Zero;
        try
        {
            Check("FwpmEngineOpen0(cleanup verify)", FwpmEngineOpen0(null, RpcCAuthnWinnt, IntPtr.Zero, ref session, out engine), log);
            var filterResult = FwpmFilterGetByKey0(engine, ref filterKey, out var filter);
            Log("FwpmFilterGetByKey0(cleanup verify)", filterResult, log);
            if (filter != IntPtr.Zero) FwpmFreeMemory0(ref filter);
            var sublayerResult = FwpmSubLayerGetByKey0(engine, ref sublayerKey, out var sublayer);
            Log("FwpmSubLayerGetByKey0(cleanup verify)", sublayerResult, log);
            if (sublayer != IntPtr.Zero) FwpmFreeMemory0(ref sublayer);
            if (filterResult != FwpEFilterNotFound || sublayerResult != FwpESublayerNotFound)
                throw new InvalidOperationException("Dynamic WFP cleanup verification failed.");
            log("[WFP] PASS: dynamic filter and sublayer are absent after engine close.");
        }
        finally { if (engine != IntPtr.Zero) Log("FwpmEngineClose0(cleanup verify)", FwpmEngineClose0(engine), log); }
    }

    private static void Check(string name, uint result, Action<string> log)
    {
        Log(name, result, log);
        if (result != 0) throw new InvalidOperationException($"{name} failed: 0x{result:X8} ({Translate(result)}).");
    }
    private static void Log(string name, uint result, Action<string> log) => log($"[WFP] {name} => 0x{result:X8} ({Translate(result)})");
    private static string Translate(uint result)
    {
        if (result == 0) return "ERROR_SUCCESS";
        if (result <= ushort.MaxValue) return new Win32Exception((int)result).Message;
        try { return Marshal.GetExceptionForHR(unchecked((int)result))?.Message ?? "untranslated WFP HRESULT"; }
        catch { return "untranslated WFP HRESULT"; }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct FwpmDisplayData0 { public string? name; public string? description; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct FwpmSession0 { public Guid sessionKey; public FwpmDisplayData0 displayData; public uint flags, txnWaitTimeoutInMS, processId; public IntPtr sid; public string? username; [MarshalAs(UnmanagedType.Bool)] public bool kernelMode; }
    [StructLayout(LayoutKind.Sequential)] private struct FwpByteBlob { public uint size; public IntPtr data; }
    [StructLayout(LayoutKind.Sequential)] private struct FwpmSublayer0 { public Guid subLayerKey; public FwpmDisplayData0 displayData; public uint flags; public IntPtr providerKey; public FwpByteBlob providerData; public ushort weight; }
    [StructLayout(LayoutKind.Explicit, Size = 16)] private struct FwpValue0 { [FieldOffset(0)] public uint type; [FieldOffset(8)] public byte uint8; public static FwpValue0 UInt8(byte value) => new() { type = FwpUint8, uint8 = value }; }
    [StructLayout(LayoutKind.Explicit, Size = 16)] private struct FwpConditionValue0 { [FieldOffset(0)] public uint type; [FieldOffset(8)] public IntPtr pointer; [FieldOffset(8)] public uint uint32; public static FwpConditionValue0 ByteBlob(IntPtr value) => new() { type = FwpByteBlobType, pointer = value }; public static FwpConditionValue0 ByteArray16(IntPtr value) => new() { type = 11, pointer = value }; public static FwpConditionValue0 UInt32(uint value) => new() { type = 3, uint32 = value }; }
    [StructLayout(LayoutKind.Sequential)] private struct FwpmFilterCondition0 { public Guid fieldKey; public uint matchType; public FwpConditionValue0 conditionValue; }
    [StructLayout(LayoutKind.Sequential)] private struct FwpmAction0 { public uint type; public Guid calloutKey; }
    [StructLayout(LayoutKind.Sequential)] private struct FwpmFilter0 { public Guid filterKey; public FwpmDisplayData0 displayData; public uint flags; public IntPtr providerKey; public FwpByteBlob providerData; public Guid layerKey, subLayerKey; public FwpValue0 weight; public uint numFilterConditions; public IntPtr filterCondition; public FwpmAction0 action; public ulong rawContext; public IntPtr reserved; public ulong filterId; public FwpValue0 effectiveWeight; }

    [DllImport("fwpuclnt.dll", ExactSpelling = true)] private static extern uint FwpmEngineOpen0(string? serverName, uint authnService, IntPtr authIdentity, ref FwpmSession0 session, out IntPtr engineHandle);
    [DllImport("fwpuclnt.dll", ExactSpelling = true)] private static extern uint FwpmEngineClose0(IntPtr engineHandle);
    [DllImport("fwpuclnt.dll", ExactSpelling = true)] private static extern uint FwpmTransactionBegin0(IntPtr engineHandle, uint flags);
    [DllImport("fwpuclnt.dll", ExactSpelling = true)] private static extern uint FwpmTransactionCommit0(IntPtr engineHandle);
    [DllImport("fwpuclnt.dll", ExactSpelling = true)] private static extern uint FwpmTransactionAbort0(IntPtr engineHandle);
    [DllImport("fwpuclnt.dll", ExactSpelling = true)] private static extern uint FwpmSubLayerAdd0(IntPtr engineHandle, ref FwpmSublayer0 sublayer, IntPtr sd);
    [DllImport("fwpuclnt.dll", ExactSpelling = true)] private static extern uint FwpmSubLayerGetByKey0(IntPtr engineHandle, ref Guid key, out IntPtr sublayer);
    [DllImport("fwpuclnt.dll", ExactSpelling = true, CharSet = CharSet.Unicode)] private static extern uint FwpmGetAppIdFromFileName0(string fileName, out IntPtr appId);
    [DllImport("fwpuclnt.dll", ExactSpelling = true)] private static extern uint FwpmFilterAdd0(IntPtr engineHandle, ref FwpmFilter0 filter, IntPtr sd, out ulong id);
    [DllImport("fwpuclnt.dll", ExactSpelling = true)] private static extern uint FwpmFilterGetByKey0(IntPtr engineHandle, ref Guid key, out IntPtr filter);
    [DllImport("fwpuclnt.dll", ExactSpelling = true)] private static extern void FwpmFreeMemory0(ref IntPtr p);
}
