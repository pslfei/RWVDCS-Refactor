using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace RWVDCS.LegacyRunner
{
    /// <summary>
    /// 老系统对账驱动器：进程内加载老 DCS（DCS.dll 等），加载同一工程 mdb，
    /// 以确定性的串行顺序单步运行，并导出与新系统 Host --dump 完全同格式的点值 TSV。
    ///
    /// 注意：
    ///  - 必须以 x86 运行（老系统用 Jet OLEDB 4.0 32 位驱动）；
    ///  - 必须从 stage-legacy.ps1 生成的运行目录启动（含 Plug\ 插件与 NHibernate 等依赖）；
    ///  - 老系统生产模式下各 DPU 并行扫描（跨 DPU 读写存在竞态），此处刻意改为
    ///    按 ControllerID 串行 Step，与新系统的确定性调度一致，才能逐周期对账。
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            if (args.Length == 0)
            {
                Console.WriteLine("用法: LegacyRunner <工程.mdb> [--steps N] [--dump 前缀] [--dump-every K] [--quiet]");
                Console.WriteLine("导出文件: <前缀>.c<周期号>.tsv（c0 = FirstRun 之后）");
                return 2;
            }

            string mdbPath = args[0];
            int steps = 0, dumpEvery = 0;
            string dumpPrefix = null;
            bool quiet = false, noFirstRun = false;
            string loadWrk = null, saveWrkDir = null, saveWrkName = "migrate", exportState = null;
            var inspectBlocks = new List<string>();
            var inspectPoints = new List<string>();
            var traceBlocks = new List<string>();

            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--steps": steps = int.Parse(args[++i]); break;
                    case "--dump": dumpPrefix = args[++i]; break;
                    case "--dump-every": dumpEvery = int.Parse(args[++i]); break;
                    case "--quiet": quiet = true; break;
                    case "--no-firstrun": noFirstRun = true; break;
                    case "--load-wrk": loadWrk = args[++i]; break;         // 从老 .wrk 工况加载（替代 mdb 初始化）
                    case "--save-wrk": saveWrkDir = args[++i]; break;      // 运行结束后保存老格式 .wrk（生成迁移测试样本用）
                    case "--wrk-name": saveWrkName = args[++i]; break;
                    case "--export-state": exportState = args[++i]; break; // 导出迁移桥接文件（全部点子状态 + 块状态）
                    case "--probe": // 格式 DPU名:块名，在加载各阶段打印该块 live 管脚值
                    {
                        var pieces = args[++i].Split(new[] { ':' }, 2);
                        ProbeDpu = pieces[0];
                        ProbeBlock = pieces[1];
                        break;
                    }
                    case "--inspect": inspectBlocks.Add(args[++i]); break;
                    case "--inspect-point": inspectPoints.Add(args[++i]); break;
                    case "--trace": traceBlocks.Add(args[++i]); break;      // 每周期打印块 live 管脚 + 私有状态（单行）
                    default:
                        Console.Error.WriteLine("未知参数: " + args[i]);
                        return 2;
                }
            }

            if (!File.Exists(mdbPath))
            {
                Console.Error.WriteLine("工程库不存在: " + mdbPath);
                return 2;
            }

            if (!quiet)
                DCS.Dcs.MessageHandler = new DCSCommon.MessageEvent(m => Console.WriteLine("[dcs] " + m));

            var sw = Stopwatch.StartNew();
            var dcs = new DCS.Dcs();
            dcs.CycleTime = 0.1; // 与生产 SessionManager 启动参数一致
            dcs.SetDataBaseConenctString(mdbPath);

            bool ok;
            if (loadWrk != null)
            {
                // 老系统原生工况加载路径（LoadFile 内部自行 Stop + RTD.Start + 反序列化）
                ok = dcs.LoadFile(loadWrk, true);
            }
            else
            {
                // 不用 Dcs.LoadDB：它把各 DPU 的 InitCommand 扔线程池后不等待就调 FirstRun，
                // LoadDB 返回时刻的系统状态是非确定的（无法对账）。此处逐步串行重放同样的流程。
                ok = DeterministicLoad(dcs, mdbPath, !noFirstRun);
            }
            sw.Stop();
            Console.WriteLine("[legacy] " + (loadWrk != null ? "工况加载 " : "确定性加载 ") + (ok ? "成功" : "失败") + "，" + sw.ElapsedMilliseconds.ToString("N0") + " ms");
            if (!ok)
                return 1;

            var dpus = GetDpusOrderedByControllerId(dcs);
            Console.WriteLine("[legacy] DPU 数: " + dpus.Count);

            if (dumpPrefix != null)
                Dump(dpus, dumpPrefix, 0);
            Inspect(dpus, inspectBlocks, inspectPoints, 0);
            Trace(dpus, traceBlocks, 0);

            for (int cycle = 1; cycle <= steps; cycle++)
            {
                // 确定性串行调度（对齐新系统 DcsRuntime.Step）
                foreach (var d in dpus)
                    d.Step();

                if (dumpPrefix != null && (dumpEvery > 0 && cycle % dumpEvery == 0 || cycle == steps))
                    Dump(dpus, dumpPrefix, cycle);
                if (cycle == steps)
                    Inspect(dpus, inspectBlocks, inspectPoints, cycle);
                Trace(dpus, traceBlocks, cycle);
            }

            if (saveWrkDir != null)
            {
                sw.Restart();
                bool saved = dcs.SaveDsc(saveWrkDir, saveWrkName);
                sw.Stop();
                Console.WriteLine("[legacy] 保存 .wrk " + (saved ? "成功" : "失败") + " → " + Path.Combine(saveWrkDir, saveWrkName) + "（" + sw.ElapsedMilliseconds.ToString("N0") + " ms）");
                if (!saved)
                    return 1;
            }

            if (exportState != null)
            {
                sw.Restart();
                ExportState(dpus, exportState);
                sw.Stop();
                Console.WriteLine("[legacy] 桥接文件导出完成（" + sw.ElapsedMilliseconds.ToString("N0") + " ms）");
            }

            Console.WriteLine("[legacy] 完成");
            return 0;
        }

        // ---- 加载阶段探针（--probe DPU:块名）----
        private static string ProbeDpu, ProbeBlock;
        private static List<DCS.Dpu> ProbeList;

        private static void Probe(string stage)
        {
            if (ProbeBlock == null || ProbeList == null)
                return;
            Console.WriteLine("[probe] ---- " + stage + " ----");
            InspectBlock(ProbeList, ProbeBlock, -1);
        }

        /// <summary>
        /// Dcs.LoadDB 的确定性重放：同样的初始化步骤，但 InitCommand 与 FirstRun
        /// 全部按 ControllerID 串行执行（原版 InitCommand 走线程池且不等待，存在竞态）。
        /// 仅使用老系统的公开 API + 少量反射读私有字段，不修改老系统本身。
        /// </summary>
        private static bool DeterministicLoad(DCS.Dcs dcs, string mdbPath, bool runFirstRun)
        {
            var t = typeof(DCS.Dcs);
            var rtdMaster = (DCSCommon.IRTD)t.GetField("m_rtdMaster", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dcs);
            var dbType = (TDK.Core.DAL.Utility.DatabaseTypes)t.GetField("m_DatabaseType", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dcs);
            var connStr = (string)t.GetField("m_DataBaseConnectString", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dcs);
            var dpuList = (System.Collections.IDictionary)t.GetField("m_dpuList", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dcs);

            dcs.Pause();
            rtdMaster.Start();

            if (!TDK.Core.DAL.DcsOperation.Restart(dbType, connStr))
            {
                Console.Error.WriteLine("[legacy] DcsOperation.Restart 失败");
                return false;
            }

            var controllers = TDK.Core.DAL.DcsOperation.GetControllers();
            if (controllers == null)
            {
                Console.Error.WriteLine("[legacy] GetControllers 返回空");
                return false;
            }
            TDK.Core.DAL.DcsOperation.PrefetchAllByControllers();

            // 与 LoadDB 相同的建 DPU + InitPoint 循环（本身就是串行的）
            var ordered = controllers.Cast<TDK.Core.DAL.Model.Prj_Controller>()
                                     .Where(c => c != null)
                                     .OrderBy(c => c.ID)
                                     .ToList();
            var dpus = new List<DCS.Dpu>();
            foreach (var controller in ordered)
            {
                var dpu = new DCS.Dpu(mdbPath, controller.ControllerName, controller.Version, controller.ID, rtdMaster.TypeManage);
                dpu.Dcs = dcs;
                if (dpu.RTD != null)
                {
                    dpu.RTD.Master = rtdMaster;
                    dpu.RTD.Start();
                }
                dpuList.Add(controller.ControllerName, dpu);
                dpus.Add(dpu);

                if (!dpu.InitOperationStart(DCSCommon.InitOpType.InitPoint))
                {
                    Console.Error.WriteLine("[legacy] " + controller.ControllerName + " InitPoint 失败");
                    return false;
                }
            }
            ProbeList = dpus;

            TDK.Core.DAL.Utility.FunctionCodeMasterManager.Initialize();

            // 原版：50 个 Task 并发 InitCommand 且不 Wait → 改为串行
            foreach (var dpu in dpus)
            {
                if (!dpu.InitOperationStart(DCSCommon.InitOpType.InitCommand))
                {
                    Console.Error.WriteLine("[legacy] " + dpu.Name + " InitCommand 失败");
                    return false;
                }
                if (ProbeBlock != null && dpu.Name == ProbeDpu)
                    Probe("InitCommand(" + dpu.Name + ") 完成后");
            }
            Probe("全部 InitCommand 完成后");

            TDK.Core.DAL.DcsOperation.Clear();

            // 原版 Dcs.FirstRun()：串行遍历 m_dpuList + RefreshNotifier（顺序与建 DPU 一致）
            if (runFirstRun)
            {
                foreach (var dpu in dpus)
                {
                    dpu.FirstRun();
                    if (ProbeBlock != null && dpu.Name == ProbeDpu)
                        Probe("FirstRun(" + dpu.Name + ") 完成后");
                }
            }
            rtdMaster.RefreshNotifier();

            try { TDK.Core.DAL.DcsOperation.ClearPrefetch(); } catch { }
            return true;
        }

        private static List<DCS.Dpu> GetDpusOrderedByControllerId(DCS.Dcs dcs)
        {
            var f = typeof(DCS.Dcs).GetField("m_dpuList", BindingFlags.Instance | BindingFlags.NonPublic);
            var dict = (System.Collections.IDictionary)f.GetValue(dcs);
            var list = new List<DCS.Dpu>();
            foreach (System.Collections.DictionaryEntry e in dict)
            {
                var dpu = e.Value as DCS.Dpu;
                if (dpu != null)
                    list.Add(dpu);
            }
            return list.OrderBy(d => d.ControllerID).ToList();
        }

        // =================================================================
        // 点值导出：格式与新系统 Host 完全一致（DPU\t点名\t类别\t值，整行 Ordinal 排序）
        // =================================================================
        private static readonly Dictionary<Type, FieldInfo> BufferFields = new Dictionary<Type, FieldInfo>();

        private static void Dump(List<DCS.Dpu> dpus, string prefix, int cycle)
        {
            var sw = Stopwatch.StartNew();
            var lines = new List<string>(300000);

            foreach (var dpu in dpus)
            {
                var rtd = dpu.RTD as DCSRTD.RTD;
                if (rtd == null)
                    continue;

                var pmField = typeof(DCSRTD.RTD).GetField("pointManage", BindingFlags.Instance | BindingFlags.NonPublic);
                object pm = pmField.GetValue(rtd);
                var ntField = pm.GetType().GetField("nameTable", BindingFlags.Instance | BindingFlags.NonPublic);
                var nameTable = (Dictionary<string, int>)ntField.GetValue(pm);

                // 快照名表，避免遍历时被改（此时系统处于 Pause，理论上稳定）
                foreach (var kv in nameTable.ToList())
                {
                    object obj;
                    try
                    {
                        obj = rtd[kv.Value];
                    }
                    catch
                    {
                        continue;
                    }
                    if (obj == null)
                        continue;

                    var t = obj.GetType();
                    string kind = t.Name;
                    if (kind != "LA" && kind != "LD" && kind != "LP" && kind != "LP32")
                        continue; // 块实例/其他类型不导出

                    FieldInfo bufField;
                    if (!BufferFields.TryGetValue(t, out bufField))
                    {
                        bufField = t.GetField("buffer", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                        BufferFields[t] = bufField;
                    }
                    if (bufField == null)
                        continue;

                    object val = bufField.GetValue(obj);
                    lines.Add(dpu.Name + "\t" + kv.Key + "\t" + kind + "\t" + FormatValue(val));
                }
            }

            lines.Sort(StringComparer.Ordinal);
            string file = prefix + ".c" + cycle + ".tsv";
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)) ?? ".");
            File.WriteAllLines(file, lines, new UTF8Encoding(false));
            sw.Stop();
            Console.WriteLine("[legacy] 导出周期 " + cycle + "：" + lines.Count.ToString("N0") + " 点 → " + file + "（" + sw.ElapsedMilliseconds.ToString("N0") + " ms）");
        }

        private static string FormatValue(object value)
        {
            if (value == null)
                return "<null>";
            if (value is float)
            {
                float f = (float)value;
                // .NET Framework 把 -0f 格式化成 "0"，.NET 10 输出 "-0"；按位识别负零对齐
                if (f == 0f && BitConverter.ToInt32(BitConverter.GetBytes(f), 0) != 0)
                    return "-0";
                return f.ToString("R", CultureInfo.InvariantCulture);
            }
            if (value is bool)
                return (bool)value ? "1" : "0";
            if (value is ushort)
                return ((ushort)value).ToString(CultureInfo.InvariantCulture);
            if (value is uint)
                return ((uint)value).ToString(CultureInfo.InvariantCulture);
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        }

        // =================================================================
        // 迁移桥接导出：老工况（内存态）→ 名字寻址的全量状态文件
        // 格式（TSV，UTF-8 无 BOM，
        //   V	1
        //   D	dpu名	cycle秒	cycleCount
        //   P	dpu名	点名	类别	k=v;k=v;...        点的全部子字段
        //   B	dpu名	块名	FC名	字段名	规格        块的全部状态字段
        // 规格: PIN:k=v;... | VAL:标量 | ARR:v1,v2,... | STR:URI转义 | NUL:）
        // 新系统 Host --import-legacy 按"名字→值"应用后另存为新格式工况。
        // =================================================================
        private static void ExportState(List<DCS.Dpu> dpus, string file)
        {
            var lines = new List<string>(600000);
            lines.Add("V\t1");

            foreach (var dpu in dpus)
            {
                lines.Add("D\t" + dpu.Name + "\t" +
                          dpu.Cycle.ToString("R", CultureInfo.InvariantCulture) + "\t" +
                          dpu.CycleCount.ToString(CultureInfo.InvariantCulture));

                var rtd = dpu.RTD as DCSRTD.RTD;
                if (rtd == null)
                    continue;

                // ---- 点全量子状态
                var pmField = typeof(DCSRTD.RTD).GetField("pointManage", BindingFlags.Instance | BindingFlags.NonPublic);
                object pm = pmField.GetValue(rtd);
                var ntField = pm.GetType().GetField("nameTable", BindingFlags.Instance | BindingFlags.NonPublic);
                var nameTable = (Dictionary<string, int>)ntField.GetValue(pm);

                foreach (var kv in nameTable.ToList())
                {
                    object obj;
                    try { obj = rtd[kv.Value]; } catch { continue; }
                    if (obj == null)
                        continue;
                    var t = obj.GetType();
                    if (t.Name != "LA" && t.Name != "LD" && t.Name != "LP" && t.Name != "LP32")
                        continue;
                    lines.Add("P\t" + dpu.Name + "\t" + kv.Key + "\t" + t.Name + "\t" + SerializeSubFields(obj));
                }

                // ---- 块全量状态
                var cmdsField = typeof(DCS.Dpu).GetField("commands", BindingFlags.Instance | BindingFlags.NonPublic);
                var cmds = cmdsField.GetValue(dpu) as System.Collections.IEnumerable;
                if (cmds == null)
                    continue;
                var fcField = typeof(DCSBase.Command).GetField("fc", BindingFlags.Instance | BindingFlags.NonPublic);

                foreach (object cmdObj in cmds)
                {
                    var cmd = cmdObj as DCSBase.Command;
                    if (cmd == null)
                        continue;
                    object fc = fcField.GetValue(cmd);
                    if (fc == null)
                        continue;

                    foreach (var fi in EnumerateStateFields(fc.GetType()))
                    {
                        string spec = SerializeField(fi, fc);
                        if (spec != null)
                            lines.Add("B\t" + dpu.Name + "\t" + cmd.Name + "\t" + cmd.FCName + "\t" + fi.Name + "\t" + spec);
                    }
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)) ?? ".");
            File.WriteAllLines(file, lines, new UTF8Encoding(false));
            Console.WriteLine("[legacy] 桥接导出 " + lines.Count.ToString("N0") + " 行 → " + file);
        }

        /// <summary>基类先、声明序遍历实例字段（与新系统 BlockStateSchema 的遍历序一致）。</summary>
        private static IEnumerable<FieldInfo> EnumerateStateFields(Type type)
        {
            var chain = new List<Type>();
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
                chain.Add(t);
            chain.Reverse();
            foreach (var t in chain)
                foreach (var fi in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (fi.IsLiteral)
                        continue;
                    yield return fi;
                }
        }

        /// <summary>序列化单个块字段为迁移规格串；不可迁移的引用类型返回 null。</summary>
        private static string SerializeField(FieldInfo fi, object fc)
        {
            var ft = fi.FieldType;
            object val = fi.GetValue(fc);

            if (ft.Name == "LA" || ft.Name == "LD" || ft.Name == "LP" || ft.Name == "LP32")
                return val == null ? null : "PIN:" + SerializeSubFields(val);

            if (ft == typeof(string))
                return val == null ? "NUL:" : "STR:" + Uri.EscapeDataString((string)val);

            if (ft.IsArray)
            {
                var arr = val as Array;
                if (arr == null)
                    return "NUL:";
                var elem = ft.GetElementType();
                if (!IsScalar(elem))
                    return null;
                var parts = new string[arr.Length];
                for (int i = 0; i < arr.Length; i++)
                    parts[i] = ScalarToString(arr.GetValue(i));
                return "ARR:" + string.Join(",", parts);
            }

            if (ft.IsEnum)
                return "VAL:" + Convert.ToInt64(val).ToString(CultureInfo.InvariantCulture);

            if (IsScalar(ft))
                return "VAL:" + ScalarToString(val);

            return null; // ICommand 引用等非状态字段
        }

        /// <summary>点/管脚结构体的全部实例字段 → k=v;k=v（float 用 R 格式，bool 用 0/1，枚举用整数）。</summary>
        private static string SerializeSubFields(object structObj)
        {
            var sb = new StringBuilder(96);
            var t = structObj.GetType();
            foreach (var fi in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (sb.Length > 0)
                    sb.Append(';');
                sb.Append(fi.Name).Append('=').Append(ScalarToString(fi.GetValue(structObj)));
            }
            return sb.ToString();
        }

        private static bool IsScalar(Type t)
        {
            if (t.IsEnum)
                return true;
            switch (Type.GetTypeCode(t))
            {
                case TypeCode.Boolean:
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.Char:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                case TypeCode.Single:
                case TypeCode.Double:
                    return true;
                default:
                    return false;
            }
        }

        private static string ScalarToString(object v)
        {
            if (v == null)
                return "";
            if (v is float)
                return ((float)v).ToString("R", CultureInfo.InvariantCulture);
            if (v is double)
                return ((double)v).ToString("R", CultureInfo.InvariantCulture);
            if (v is bool)
                return (bool)v ? "1" : "0";
            if (v.GetType().IsEnum)
                return Convert.ToInt64(v).ToString(CultureInfo.InvariantCulture);
            if (v is char)
                return ((int)(char)v).ToString(CultureInfo.InvariantCulture);
            return Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
        }

        // =================================================================
        // 深度检视：块命令的 live 管脚 / wire / 输出同步表，及任意点的当前值
        // =================================================================
        private static void Inspect(List<DCS.Dpu> dpus, List<string> blocks, List<string> points, int cycle)
        {
            foreach (string b in blocks)
                InspectBlock(dpus, b, cycle);
            foreach (string p in points)
                InspectPoint(dpus, p, cycle);
        }

        /// <summary>
        /// 单行紧凑跟踪：块的全部 LA/LD 管脚 buffer + 全部私有标量字段（bool/float/int/uint）。
        /// 用于逐周期比对新老系统的块内部演化。
        /// </summary>
        private static void Trace(List<DCS.Dpu> dpus, List<string> blocks, int cycle)
        {
            foreach (string blockName in blocks)
            {
                foreach (var dpu in dpus)
                {
                    var cmdsField = typeof(DCS.Dpu).GetField("commands", BindingFlags.Instance | BindingFlags.NonPublic);
                    var cmds = cmdsField.GetValue(dpu) as System.Collections.IEnumerable;
                    if (cmds == null)
                        continue;
                    foreach (object cmdObj in cmds)
                    {
                        var cmd = cmdObj as DCSBase.Command;
                        if (cmd == null || !string.Equals(cmd.Name, blockName, StringComparison.OrdinalIgnoreCase))
                            continue;
                        var fcField = typeof(DCSBase.Command).GetField("fc", BindingFlags.Instance | BindingFlags.NonPublic);
                        object fc = fcField.GetValue(cmd);
                        var sb = new StringBuilder();
                        sb.Append("[trace c").Append(cycle).Append("] ").Append(cmd.Name).Append(':');
                        if (fc != null)
                        {
                            foreach (var fi in fc.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
                            {
                                var ft = fi.FieldType;
                                if (ft.Name != "LA" && ft.Name != "LD")
                                    continue;
                                object pinObj = fi.GetValue(fc);
                                var bufField = ft.GetField("buffer", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                                if (bufField != null && pinObj != null)
                                    sb.Append(' ').Append(fi.Name).Append('=').Append(FormatValue(bufField.GetValue(pinObj)));
                            }
                            foreach (var fi in fc.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
                            {
                                var ft = fi.FieldType;
                                if (ft != typeof(bool) && ft != typeof(float) && ft != typeof(double) && ft != typeof(int) && ft != typeof(uint))
                                    continue;
                                sb.Append(' ').Append(fi.Name).Append('=').Append(FormatValue(fi.GetValue(fc)));
                            }
                        }
                        else
                        {
                            sb.Append(" fc=<null>");
                        }
                        Console.WriteLine(sb.ToString());
                        goto nextBlock;
                    }
                }
            nextBlock: ;
            }
        }

        private static void InspectPoint(List<DCS.Dpu> dpus, string pointName, int cycle)
        {
            foreach (var dpu in dpus)
            {
                var rtd = dpu.RTD as DCSRTD.RTD;
                if (rtd == null)
                    continue;
                int sid = rtd[pointName];
                if (sid < 0)
                    continue;
                object obj;
                try { obj = rtd[sid]; } catch { continue; }
                if (obj == null)
                    continue;
                var t = obj.GetType();
                var bufField = t.GetField("buffer", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                string val = bufField != null ? FormatValue(bufField.GetValue(obj)) : "(" + t.Name + ")";
                Console.WriteLine("[inspect c" + cycle + "] point " + pointName + " @" + dpu.Name + " sid=" + sid + " type=" + t.Name + " buffer=" + val);
            }
        }

        private static void InspectBlock(List<DCS.Dpu> dpus, string blockName, int cycle)
        {
            foreach (var dpu in dpus)
            {
                var cmdsField = typeof(DCS.Dpu).GetField("commands", BindingFlags.Instance | BindingFlags.NonPublic);
                var cmds = cmdsField.GetValue(dpu) as System.Collections.IEnumerable;
                if (cmds == null)
                    continue;

                foreach (object cmdObj in cmds)
                {
                    var cmd = cmdObj as DCSBase.Command;
                    if (cmd == null || !string.Equals(cmd.Name, blockName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    Console.WriteLine("[inspect c" + cycle + "] block " + cmd.Name + " (" + cmd.FCName + ") @" + dpu.Name);

                    // live fc 管脚值
                    var fcField = typeof(DCSBase.Command).GetField("fc", BindingFlags.Instance | BindingFlags.NonPublic);
                    object fc = fcField.GetValue(cmd);
                    Console.WriteLine("  fc=" + (fc == null ? "<null>" : fc.GetType().FullName));
                    if (fc != null)
                    {
                        foreach (var fi in fc.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
                        {
                            var ft = fi.FieldType;
                            if (ft.Name != "LA" && ft.Name != "LD" && ft.Name != "LP" && ft.Name != "LP32")
                                continue;
                            object pinObj = fi.GetValue(fc);
                            var bufField = ft.GetField("buffer", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                            string val = bufField != null && pinObj != null ? FormatValue(bufField.GetValue(pinObj)) : "<null>";
                            Console.WriteLine("  pin " + fi.Name + " [" + ft.Name + "] live.buffer=" + val);
                        }
                    }

                    // wires
                    DumpWireList(cmd, "jointWires");
                    DumpWireList(cmd, "referenceWires");

                    // _outputPointSync
                    var opsField = typeof(DCSBase.Command).GetField("_outputPointSync", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (opsField != null)
                    {
                        var list = opsField.GetValue(cmd) as System.Collections.IEnumerable;
                        if (list != null)
                        {
                            foreach (object e in list)
                            {
                                var et = e.GetType();
                                object fi2 = et.GetField("fi", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(e);
                                object pn = et.GetField("pointName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(e);
                                object rev = et.GetField("reversed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(e);
                                object psid = et.GetField("pointSid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(e);
                                Console.WriteLine("  outSync pin=" + (fi2 as FieldInfo)?.Name + " -> " + pn + " rev=" + rev + " pointSid=" + psid);
                            }
                        }
                    }
                    return;
                }
            }
            Console.WriteLine("[inspect c" + cycle + "] block " + blockName + " 未找到");
        }

        private static void DumpWireList(DCSBase.Command cmd, string fieldName)
        {
            var f = typeof(DCSBase.Command).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (f == null)
            {
                Console.WriteLine("  (" + fieldName + " 字段不存在)");
                return;
            }
            var wires = f.GetValue(cmd) as System.Collections.IEnumerable;
            if (wires == null)
                return;
            foreach (object wObj in wires)
            {
                var w = wObj as DCSBase.Wire;
                if (w == null)
                    continue;
                Console.WriteLine("  " + fieldName + ": type=" + w.Type + " attr=" + w.Attribute + " point=" + w.PointName +
                                  " pin=" + w.PinName + " rev=" + w.Reversed +
                                  " pointAddr=(sid=" + w.PointAddress.Sid + ",off=" + w.PointAddress.Offset + ",len=" + w.PointAddress.Length + ")" +
                                  " pinAddr=(sid=" + w.PinAddress.Sid + ",off=" + w.PinAddress.Offset + ",len=" + w.PinAddress.Length + ")");
            }
        }
    }
}
