#:property AllowUnsafeBlocks=true
// Address-space breakdown of a running process (Windows): committed and working-set bytes by region
// type (image / mapped / private), the image modules with the most resident pages, the largest mapped
// regions, and private allocations grouped by size. Window-sized GPU surfaces show up as a cluster of
// equal-sized private (ANGLE on an iGPU) or mapped (WGL) allocations.
//
// usage: dotnet run vmstat.cs -- <pid>
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

if (args.Length != 1 || !int.TryParse(args[0], out int pid))
{
    Console.Error.WriteLine("usage: dotnet run vmstat.cs -- <pid>");
    return 2;
}

const uint PROCESS_QUERY_INFORMATION = 0x0400, PROCESS_VM_READ = 0x0010;
const uint MEM_COMMIT = 0x1000, MEM_IMAGE = 0x1000000, MEM_MAPPED = 0x40000;
const long Page = 4096;

nint h = Native.OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, pid);
if (h == 0) { Console.Error.WriteLine("OpenProcess failed"); return 1; }

// type -> (committed, ws, wsPrivate)
var byType = new Dictionary<string, long[]> { ["image"] = new long[3], ["mapped"] = new long[3], ["private"] = new long[3] };
var byModule = new Dictionary<string, long[]>();
var mappedRegions = new List<(nint baseAddr, long size, long ws)>();
var privateAllocs = new Dictionary<nint, long[]>(); // allocation base -> committed, ws

nint addr = 0;
var mbi = new Native.MEMORY_BASIC_INFORMATION();
int mbiSize = Marshal.SizeOf<Native.MEMORY_BASIC_INFORMATION>();
var sbName = new StringBuilder(1024);

while (Native.VirtualQueryEx(h, addr, out mbi, (nuint)mbiSize) != 0)
{
    long size = (long)mbi.RegionSize;
    if (mbi.State == MEM_COMMIT)
    {
        string type = mbi.Type switch { MEM_IMAGE => "image", MEM_MAPPED => "mapped", _ => "private" };
        long pages = size / Page;
        var ex = new Native.PSAPI_WORKING_SET_EX_INFORMATION[pages];
        for (long i = 0; i < pages; i++) ex[i].VirtualAddress = mbi.BaseAddress + (nint)(i * Page);
        long ws = 0, wsPriv = 0;
        unsafe
        {
            fixed (Native.PSAPI_WORKING_SET_EX_INFORMATION* p = ex)
            {
                if (Native.QueryWorkingSetEx(h, (nint)p, (int)(pages * sizeof(Native.PSAPI_WORKING_SET_EX_INFORMATION))))
                {
                    for (long i = 0; i < pages; i++)
                    {
                        ulong a = (ulong)ex[i].VirtualAttributes;
                        if ((a & 1) != 0)
                        {
                            ws += Page;
                            if (((a >> 15) & 1) == 0) wsPriv += Page; // Shared bit
                        }
                    }
                }
            }
        }

        var t = byType[type];
        t[0] += size; t[1] += ws; t[2] += wsPriv;

        if (type == "image")
        {
            sbName.Clear();
            string name = Native.GetMappedFileNameW(h, mbi.AllocationBase, sbName, sbName.Capacity) > 0
                ? Path.GetFileName(sbName.ToString()) : "?";
            if (!byModule.TryGetValue(name, out var m)) byModule[name] = m = new long[3];
            m[0] += size; m[1] += ws; m[2] += wsPriv;
        }
        else if (type == "mapped")
        {
            mappedRegions.Add((mbi.BaseAddress, size, ws));
        }
        else
        {
            if (!privateAllocs.TryGetValue(mbi.AllocationBase, out var pa)) privateAllocs[mbi.AllocationBase] = pa = new long[2];
            pa[0] += size; pa[1] += ws;
        }
    }

    addr = mbi.BaseAddress + (nint)size;
    if (addr == 0) break;
}

static string Mb(long b) => (b / 1048576.0).ToString("F1");
using var proc = Process.GetProcessById(pid);
Console.WriteLine($"pid {pid}  WS {Mb(proc.WorkingSet64)} MB  private {Mb(proc.PrivateMemorySize64)} MB");
Console.WriteLine("type,committed_mb,ws_mb,ws_private_mb");
foreach (var (k, v) in byType) Console.WriteLine($"{k},{Mb(v[0])},{Mb(v[1])},{Mb(v[2])}");
Console.WriteLine("top modules by ws: module,committed_mb,ws_mb,ws_private_mb");
foreach (var (k, v) in byModule.OrderByDescending(kv => kv.Value[1]).Take(15)) Console.WriteLine($"  {k},{Mb(v[0])},{Mb(v[1])},{Mb(v[2])}");
Console.WriteLine($"mapped regions: {mappedRegions.Count}, largest by ws (size_mb/ws_mb):");
foreach (var r in mappedRegions.OrderByDescending(r => r.ws).Take(8)) Console.WriteLine($"  0x{r.baseAddr:X} {Mb(r.size)}/{Mb(r.ws)}");
Console.WriteLine($"private allocations: {privateAllocs.Count}; largest committed (committed_mb/ws_mb):");
foreach (var (b, v) in privateAllocs.OrderByDescending(kv => kv.Value[0]).Take(20)) Console.WriteLine($"  0x{b:X} {Mb(v[0])}/{Mb(v[1])}");
Console.WriteLine("private committed by allocation size bucket: bucket,count,committed_mb,ws_mb");
foreach (var g in privateAllocs.Values.GroupBy(v => v[0] switch
         {
             < 1L << 20 => "<1MB", < 4L << 20 => "1-4MB", < 16L << 20 => "4-16MB", < 23L << 20 => "16-23MB",
             < 26L << 20 => "23-26MB", < 64L << 20 => "26-64MB", _ => ">=64MB"
         }).OrderBy(g => g.Key))
    Console.WriteLine($"  {g.Key},{g.Count()},{Mb(g.Sum(v => v[0]))},{Mb(g.Sum(v => v[1]))}");
return 0;

static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORY_BASIC_INFORMATION
    {
        public nint BaseAddress; public nint AllocationBase; public uint AllocationProtect; public ushort PartitionId;
        public nuint RegionSize; public uint State; public uint Protect; public uint Type;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PSAPI_WORKING_SET_EX_INFORMATION { public nint VirtualAddress; public nuint VirtualAttributes; }

    [DllImport("kernel32.dll")] public static extern nint OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] public static extern nuint VirtualQueryEx(nint h, nint addr, out MEMORY_BASIC_INFORMATION mbi, nuint len);
    [DllImport("psapi.dll")] public static extern bool QueryWorkingSetEx(nint h, nint pv, int cb);
    [DllImport("psapi.dll", CharSet = CharSet.Unicode)] public static extern int GetMappedFileNameW(nint h, nint addr, StringBuilder name, int size);
}
