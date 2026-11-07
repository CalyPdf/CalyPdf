#:package Microsoft.Diagnostics.Tracing.TraceEvent@3.1.19
// One-off: who allocates the large byte[]? Reads GCAllocationTick events (sampled roughly every 100 KB) and
// groups them by call stack. Usage: dotnet run analyze-alloc.cs -- <trace.nettrace> [<type name substring>] [<frames>]
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;

string nettrace = args[0];
string typeFilter = args.Length > 1 ? args[1] : "System.Byte[]";
int frames = args.Length > 2 ? int.Parse(args[2]) : 10;

string etlx = TraceLog.CreateFromEventPipeDataFile(nettrace);
using var log = new TraceLog(etlx);

var byType = new Dictionary<string, (long Bytes, int Ticks)>();
var byStack = new Dictionary<string, (long Bytes, int Ticks)>();
long total = 0, matching = 0, matchingLarge = 0;

foreach (TraceEvent ev in log.Events)
{
    if (ev is not GCAllocationTickTraceData t)
    {
        continue;
    }

    long amount = t.AllocationAmount64 > 0 ? t.AllocationAmount64 : t.AllocationAmount;
    total += amount;

    string kind = $"{t.TypeName} [{t.AllocationKind}]";
    byType[kind] = byType.TryGetValue(kind, out var a) ? (a.Bytes + amount, a.Ticks + 1) : (amount, 1);

    if (!t.TypeName.Contains(typeFilter, StringComparison.Ordinal))
    {
        continue;
    }

    matching += amount;
    if (t.AllocationKind == GCAllocationKind.Large)
    {
        matchingLarge += amount;
    }

    var names = new List<string>();
    for (var idx = ev.CallStackIndex(); idx != CallStackIndex.Invalid && names.Count < frames; idx = log.CallStacks.Caller(idx))
    {
        string name = log.CallStacks.CodeAddresses.Methods[log.CallStacks.CodeAddresses.MethodIndex(log.CallStacks.CodeAddressIndex(idx))]?.FullMethodName
                      ?? "?";
        names.Add(name.Length > 110 ? name[..110] : name);
    }

    string key = (t.AllocationKind == GCAllocationKind.Large ? "[LOH] " : "[SOH] ") + string.Join("\n      <- ", names);
    byStack[key] = byStack.TryGetValue(key, out var b) ? (b.Bytes + amount, b.Ticks + 1) : (amount, 1);
}

Console.WriteLine($"Allocation ticks cover {total / 1048576.0:F0} MiB in total; {typeFilter}: {matching / 1048576.0:F0} MiB ({matchingLarge / 1048576.0:F0} MiB on the LOH).");
Console.WriteLine("\n== top types by sampled bytes");
foreach (var kv in byType.OrderByDescending(k => k.Value.Bytes).Take(12))
{
    Console.WriteLine($"{kv.Value.Bytes / 1048576.0,8:F1} MiB  {kv.Value.Ticks,6} ticks  {kv.Key}");
}

Console.WriteLine($"\n== top call stacks for {typeFilter}");
foreach (var kv in byStack.OrderByDescending(k => k.Value.Bytes).Take(14))
{
    Console.WriteLine($"{kv.Value.Bytes / 1048576.0,8:F1} MiB  {kv.Value.Ticks,6} ticks\n      {kv.Key}\n");
}
