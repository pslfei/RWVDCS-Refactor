using System.Globalization;
using System.Text;

namespace RWVDCS.Tools.ParityCompare;

/// <summary>
/// 对账工具：比较老系统（LegacyRunner）与新系统（Host --dump）导出的点值 TSV。
/// 行格式：DPU\t点名\t类别\t值。键 = DPU+点名；LA 值按 float 位一致比较（可选容差）。
///
/// 分类规则：
///  - 跨 DPU 副本：老系统 RTD 会在非属主 DPU 建远程点的本地占位副本；新系统按属主唯一
///    存储。"仅老系统"的行若点名存在于新系统其他 DPU，归类为副本（信息项，不算差异）。
///  - 非确定点：--old2 给第二次老系统运行的 dump，两次值不同的键（RAND/DATE 及其下游）
///    从值比较中排除（信息项）。
///  - 容差：--eps 绝对容差 / --rel 相对容差，默认按位一致。
/// </summary>
internal static class Program
{
    private sealed record Row(string Dpu, string Name, string Kind, string Value);

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length < 2)
        {
            Console.WriteLine("用法: paritycmp <老系统.tsv> <新系统.tsv> [--old2 老系统第二次.tsv] [--eps E] [--rel R] [--samples N] [--out 报告.txt]");
            return 2;
        }

        string oldFile = args[0], newFile = args[1];
        string? old2File = null;
        float eps = 0f, rel = 0f;
        int maxSamples = 30;
        string? outFile = null;
        for (int i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--old2": old2File = args[++i]; break;
                case "--eps": eps = float.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--rel": rel = float.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--samples": maxSamples = int.Parse(args[++i]); break;
                case "--out": outFile = args[++i]; break;
            }
        }

        var oldRows = Load(oldFile);
        var newRows = Load(newFile);

        // 非确定键集合：两次老系统运行值不同的键
        var nondet = new HashSet<string>(StringComparer.Ordinal);
        if (old2File != null)
        {
            foreach (var (key, o2) in Load(old2File))
            {
                if (oldRows.TryGetValue(key, out var o1) && !string.Equals(o1.Value, o2.Value, StringComparison.Ordinal))
                    nondet.Add(key);
            }
        }

        // 新系统点名 → DPU（用于副本判定）
        var newNameToDpu = new Dictionary<string, string>(newRows.Count, StringComparer.Ordinal);
        foreach (var r in newRows.Values)
            newNameToDpu.TryAdd(r.Name, r.Dpu);

        var report = new StringBuilder();
        void Emit(string line)
        {
            Console.WriteLine(line);
            report.AppendLine(line);
        }

        Emit($"老系统: {oldRows.Count:N0} 点（{oldFile}）");
        Emit($"新系统: {newRows.Count:N0} 点（{newFile}）");
        if (old2File != null)
            Emit($"非确定点（两次老系统运行不一致）: {nondet.Count:N0}（{old2File}）");

        // ---- 点集差异
        var replicaOld = new List<Row>();          // 跨 DPU 副本（信息项）
        var onlyOld = new List<Row>();             // 真缺失
        var onlyNew = new List<Row>();
        var kindMismatch = new List<(Row Old, Row New)>();
        var valueDiff = new List<(Row Old, Row New)>();
        int matched = 0, valueEqual = 0, nondetSkipped = 0;

        foreach (var (key, o) in oldRows)
        {
            if (!newRows.TryGetValue(key, out var n))
            {
                if (newNameToDpu.TryGetValue(o.Name, out var ownerDpu) && ownerDpu != o.Dpu)
                    replicaOld.Add(o);
                else
                    onlyOld.Add(o);
                continue;
            }
            matched++;
            if (!string.Equals(o.Kind, n.Kind, StringComparison.Ordinal))
            {
                kindMismatch.Add((o, n));
                continue;
            }
            if (nondet.Contains(key))
            {
                nondetSkipped++;
                continue;
            }
            if (ValueEquals(o.Kind, o.Value, n.Value, eps, rel))
                valueEqual++;
            else
                valueDiff.Add((o, n));
        }
        foreach (var (key, n) in newRows)
        {
            if (!oldRows.ContainsKey(key))
                onlyNew.Add(n);
        }

        Emit($"共同点: {matched:N0}，值一致: {valueEqual:N0}，值不一致: {valueDiff.Count:N0}，非确定点跳过: {nondetSkipped:N0}");
        Emit($"跨DPU副本（仅老系统，信息项）: {replicaOld.Count:N0}");
        Emit($"真缺失（仅老系统）: {onlyOld.Count:N0}，仅新系统: {onlyNew.Count:N0}，类型不一致: {kindMismatch.Count:N0}");

        if (onlyOld.Count > 0)
        {
            Emit("");
            Emit($"== 仅老系统的点（真缺失，按类别统计）==");
            foreach (var g in onlyOld.GroupBy(r => r.Kind).OrderByDescending(g => g.Count()))
                Emit($"  {g.Key}: {g.Count():N0}");
            Emit($"  样本:");
            foreach (var r in onlyOld.Take(maxSamples))
                Emit($"    [{r.Dpu}] {r.Name} [{r.Kind}] = {r.Value}");
        }

        if (onlyNew.Count > 0)
        {
            Emit("");
            Emit($"== 仅新系统的点（按类别统计）==");
            foreach (var g in onlyNew.GroupBy(r => r.Kind).OrderByDescending(g => g.Count()))
                Emit($"  {g.Key}: {g.Count():N0}");
            Emit($"  样本:");
            foreach (var r in onlyNew.Take(maxSamples))
                Emit($"    [{r.Dpu}] {r.Name} [{r.Kind}] = {r.Value}");
        }

        if (kindMismatch.Count > 0)
        {
            Emit("");
            Emit("== 类型不一致 ==");
            foreach (var (o, n) in kindMismatch.Take(maxSamples))
                Emit($"    [{o.Dpu}] {o.Name}: 老={o.Kind} 新={n.Kind}");
        }

        if (valueDiff.Count > 0)
        {
            Emit("");
            Emit("== 值不一致（按 DPU 统计 Top 15）==");
            foreach (var g in valueDiff.GroupBy(d => d.Old.Dpu).OrderByDescending(g => g.Count()).Take(15))
                Emit($"  {g.Key}: {g.Count():N0}");
            Emit("== 值不一致（按类别统计）==");
            foreach (var g in valueDiff.GroupBy(d => d.Old.Kind).OrderByDescending(g => g.Count()))
                Emit($"  {g.Key}: {g.Count():N0}");

            // LA 差异的量级分布（区分浮点噪声与逻辑差异）
            var laDiffs = valueDiff.Where(d => d.Old.Kind == "LA").ToList();
            if (laDiffs.Count > 0)
            {
                int ulpSmall = 0, relSmall = 0, big = 0;
                foreach (var (o, n) in laDiffs)
                {
                    if (float.TryParse(o.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float fo) &&
                        float.TryParse(n.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float fn))
                    {
                        float scale = Math.Max(Math.Abs(fo), Math.Abs(fn));
                        float d = Math.Abs(fo - fn);
                        if (scale > 0 && d / scale <= 1e-5f) ulpSmall++;
                        else if (scale > 0 && d / scale <= 1e-3f) relSmall++;
                        else big++;
                    }
                    else big++;
                }
                Emit($"== LA 差异量级 ==");
                Emit($"  相对误差 ≤1e-5（浮点噪声级）: {ulpSmall:N0}");
                Emit($"  相对误差 1e-5~1e-3（累积放大）: {relSmall:N0}");
                Emit($"  相对误差 >1e-3（疑似逻辑差异）: {big:N0}");
            }

            Emit($"  样本:");
            foreach (var (o, n) in valueDiff.Take(maxSamples))
                Emit($"    [{o.Dpu}] {o.Name} [{o.Kind}] 老={o.Value} 新={n.Value}");
        }

        bool identical = onlyOld.Count == 0 && onlyNew.Count == 0 && kindMismatch.Count == 0 && valueDiff.Count == 0;
        Emit("");
        Emit(identical ? "结果: 一致（副本/非确定点已按规则排除）" : "结果: 存在差异");

        if (outFile != null)
        {
            File.WriteAllText(outFile, report.ToString(), new UTF8Encoding(false));
            Console.WriteLine($"报告已写入 {outFile}");
        }

        return identical ? 0 : 1;
    }

    private static Dictionary<string, Row> Load(string file)
    {
        var dict = new Dictionary<string, Row>(300000, StringComparer.Ordinal);
        foreach (var line in File.ReadLines(file))
        {
            if (line.Length == 0)
                continue;
            var parts = line.Split('\t');
            if (parts.Length < 4)
                continue;
            var row = new Row(parts[0], parts[1], parts[2], parts[3]);
            dict[parts[0] + "\t" + parts[1]] = row;
        }
        return dict;
    }

    private static bool ValueEquals(string kind, string a, string b, float eps, float rel)
    {
        if (string.Equals(a, b, StringComparison.Ordinal))
            return true;

        if (kind == "LA")
        {
            // 双方 dump 都是 roundtrip 格式：解析回 float 后按位比较（NaN 视为相等）
            if (float.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out float fa) &&
                float.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out float fb))
            {
                if (fa == fb) // 位一致或 ±0（IEEE 数值相等）
                    return true;
                if (float.IsNaN(fa) && float.IsNaN(fb))
                    return true;
                if (eps > 0 && Math.Abs(fa - fb) <= eps)
                    return true;
                if (rel > 0)
                {
                    float scale = Math.Max(Math.Abs(fa), Math.Abs(fb));
                    if (scale > 0 && Math.Abs(fa - fb) / scale <= rel)
                        return true;
                }
            }
            return false;
        }

        return false;
    }
}
