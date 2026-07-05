// PoC 基准（12b）：验证方案的关键性能主张，规模取老系统上限场景（百万点）。
//   1. Arena 字段读写 vs 裸数组（验证"index 寻址开销可忽略"）
//   2. 快照 save/load（验证"整片镜像落盘 ≈ memcpy+磁盘"，对比老式逐点序列化）
//   3. Roslyn 编译 + ALC 热更（验证"现调现改"的交互延迟）
// 用 Stopwatch 中位数口径快速出数；严谨微基准（BenchmarkDotNet）留给 M2 回归门禁。
using System.Diagnostics;
using System.Text;
using RWVDCS.Core.PointStore;
using RWVDCS.Core.Types;
using RWVDCS.Hosting;

const int PointCount = 1_000_000;
const uint LaBufferOffset = 24; // LA.buffer 字段偏移（布局测试守卫）

string tempDir = Path.Combine(Path.GetTempPath(), "rwvdcs-bench");
Directory.CreateDirectory(tempDir);

Console.WriteLine($"RWVDCS.Next PoC 基准  |  点数 = {PointCount:N0} (LA, 28B/点)  |  {DateTime.Now:HH:mm:ss}");
Console.WriteLine(new string('-', 78));

// ---------- 构建 1M 点 Arena ----------
var swBuild = Stopwatch.StartNew();
var builder = new ArenaBuilder();
var proto = new LA(QualityTypes.Good, false, false, false, false, false,
    float.MaxValue, float.MinValue, 0f, 0, 0f);
for (int i = 0; i < PointCount; i++)
    builder.AddSlot($"P{i:D6}", WellKnownTypeIds.LA, in proto);
using var arena = PointArena.Create(builder);
swBuild.Stop();
Report("构建 Arena（登记+镜像初始化）", swBuild.Elapsed, once: true);

// ---------- 1. 读写吞吐 ----------
var baseline = new LA[PointCount];
for (int i = 0; i < PointCount; i++) baseline[i] = proto;

Bench("裸数组 LA[] 写 buffer ×1M（理论上限）", 5, () =>
{
    for (int i = 0; i < PointCount; i++)
        baseline[i].Value = i * 0.5f;
});

Bench("Arena WriteField<float> ×1M（FSID 路径）", 5, () =>
{
    for (int i = 0; i < PointCount; i++)
        arena.WriteField(i, LaBufferOffset, i * 0.5f);
});

Bench("Arena GetRef<LA>().Value ×1M（含报警副作用）", 5, () =>
{
    for (int i = 0; i < PointCount; i++)
        arena.GetRef<LA>(i).Value = i * 0.5f;
});

float sink = 0f;
Bench("Arena ReadField<float> ×1M", 5, () =>
{
    float acc = 0f;
    for (int i = 0; i < PointCount; i++)
        acc += arena.ReadField<float>(i, LaBufferOffset);
    sink = acc;
});

// ---------- 2. 快照 ----------
string snapshotPath = Path.Combine(tempDir, "bench.ckpt");
Bench("快照保存 SaveSnapshot（整片镜像→磁盘）", 3, () => arena.SaveSnapshot(snapshotPath));
long snapshotSize = new FileInfo(snapshotPath).Length;
Console.WriteLine($"    快照文件大小 = {snapshotSize / 1024.0 / 1024.0:F1} MB");

Bench("快照恢复 LoadSnapshotInPlace（就地覆盖数据区）", 3, () => arena.LoadSnapshotInPlace(snapshotPath));

Bench("快照冷启动 LoadFrom（重建全部结构+名字表）", 3, () =>
{
    using var restored = PointArena.LoadFrom(snapshotPath);
});

// ---------- 老式逐点序列化对照（复刻老 RTD Save 的循环形态）----------
string legacyPath = Path.Combine(tempDir, "legacy.bin");
Bench("老式逐点序列化保存（BinaryWriter 逐点 名字+SID+28B）", 3, () =>
{
    using var fs = new FileStream(legacyPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
    using var bw = new BinaryWriter(fs, Encoding.UTF8);
    for (int i = 0; i < PointCount; i++)
    {
        bw.Write(arena.GetName(i)!);
        bw.Write(i);
        bw.Write(arena.GetSlotSpan(i));
    }
    fs.Flush(flushToDisk: true);
});

Bench("老式逐点反序列化加载（BinaryReader 逐点回填）", 3, () =>
{
    using var fs = new FileStream(legacyPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
    using var br = new BinaryReader(fs, Encoding.UTF8);
    Span<byte> buf = stackalloc byte[28];
    for (int i = 0; i < PointCount; i++)
    {
        _ = br.ReadString();
        int sid = br.ReadInt32();
        br.BaseStream.ReadExactly(buf);
        buf.CopyTo(arena.GetSlotSpan(sid));
    }
});

// ---------- 3. 热更链路 ----------
const string KernelSourceText = """
    using RWVDCS.Core.Execution;
    using RWVDCS.Core.PointStore;
    using RWVDCS.Core.Types;

    namespace FB.Generated;

    public sealed class BenchKernel : IScanKernel
    {
        public void Scan(PointArena arena)
        {
            for (int i = 0; i < 1000; i++)
                arena.GetRef<LA>(i).Value = i * 2f;
        }
    }
    """;

var swFirst = Stopwatch.StartNew();
var first = KernelCompiler.Compile("bench-cold", [new KernelSource("k.cs", KernelSourceText)]);
swFirst.Stop();
if (!first.Success) throw new InvalidOperationException(string.Join('\n', first.Errors));
Report("Roslyn 首次编译（含引用集初始化，冷）", swFirst.Elapsed, once: true);

Bench("Roslyn 增量编译（热，现调现改的等待时间）", 5, () =>
{
    var r = KernelCompiler.Compile("bench-warm", [new KernelSource("k.cs", KernelSourceText)]);
    if (!r.Success) throw new InvalidOperationException("compile failed");
});

using var host = new KernelHost();
Bench("ALC 装载新代+原子切换（热更生效延迟）", 5, () =>
{
    host.LoadGeneration(first.AssemblyImage!, first.PdbImage);
});

Console.WriteLine(new string('-', 78));
Console.WriteLine($"sink={sink:E2}（防死码消除）");
try { Directory.Delete(tempDir, recursive: true); } catch { }

// ---------- 工具 ----------
void Bench(string name, int iterations, Action action)
{
    action(); // 预热（JIT + 页面缺页）
    var times = new double[iterations];
    for (int it = 0; it < iterations; it++)
    {
        var sw = Stopwatch.StartNew();
        action();
        sw.Stop();
        times[it] = sw.Elapsed.TotalMilliseconds;
    }
    Array.Sort(times);
    double median = times[iterations / 2];
    Report(name, TimeSpan.FromMilliseconds(median), once: false, min: times[0], max: times[^1]);
}

void Report(string name, TimeSpan elapsed, bool once, double min = 0, double max = 0)
{
    double ms = elapsed.TotalMilliseconds;
    string throughput = ms > 0 ? $"{PointCount / ms / 1000.0:F1}M 点/秒" : "-";
    string range = once ? "（单次）" : $"[{min:F1}..{max:F1}]";
    Console.WriteLine($"{name,-46} {ms,9:F1} ms {range,-18} {throughput}");
}
