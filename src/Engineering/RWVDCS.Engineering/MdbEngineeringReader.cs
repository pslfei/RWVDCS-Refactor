using System.Data;
using System.Data.OleDb;
using System.Globalization;

namespace RWVDCS.Engineering;

/// <summary>
/// Access 工程库直读器（替代老系统 NHibernate + Jet4 的装载路径）。
///
/// 装载语义逐条对齐老系统（文件:行号 见 分析报告/05）：
///  - 行序：老系统 HQL 无 ORDER BY，Jet 按"物理行序"（自然序）返回，且本工程库的
///    自然序与 ID 序不一致（Cfg_VarSystem 有 1038 处逆序）。行序决定粘滞默认值、
///    命令创建/执行顺序、参数 DataType 粘滞传播，必须用自然序（无 ORDER BY），
///    经实测 ACE 与 Jet 对同一 mdb 的全表扫描行序一致。
///  - 点默认值解析的"粘滞局部变量"行为（Operation.cs InitPointByDatabase 179-231）原样保留：
///    fDefault/bDefault/Max/Min/Force 是跨行局部变量，空串时沿用上一行的值。这是老系统事实语义。
///  - 块管脚细节构建对齐 InitFCByDatabase（Operation.cs 323-601），含 DataType 粘滞变量行为。
///  - 中间点（pin-point）注册与源块 OUT 追加对齐 Dpu.InitOperationStart(InitCommand)
///    （Dpu.cs 1488-1613），APSM 整块跳过。
/// </summary>
public static class MdbEngineeringReader
{
    /// <summary>老系统在 InitFCByDatabase 中跳过的 HollySys 硬件块。</summary>
    private static readonly string[] SkippedFcNames = ["H_PI", "H_RTD", "H_TC", "H_ELC"];

    public static EngineeringModel Load(string mdbPath)
    {
        if (!File.Exists(mdbPath))
            throw new FileNotFoundException("工程库不存在", mdbPath);

        using var conn = new OleDbConnection(
            $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={mdbPath};Persist Security Info=False;Mode=Read;");
        conn.Open();

        var meta = LoadMetaCatalog(conn);
        var controllers = new List<ControllerModel>();

        // 与老系统 GetControllers 一致：全表、自然序（该表自然序恰与 ID 序相同，
        // 但 LoadDB 里按 ControllerID 升序逐个建 DPU，此处显式排序以匹配装配序）
        var controllerRows = Query(conn,
            "SELECT ID, ControllerAddress, ControllerName FROM Prj_Controller ORDER BY ID");

        // 预取五表（对齐 PrefetchAllByControllers 的全表拉取 + 按控制器分组）。
        // 无 ORDER BY：必须保持 mdb 自然行序（见类注释，行序影响粘滞语义与命令顺序）
        var varsByCtrl = GroupBy(Query(conn,
            "SELECT ID, Name, DataType, DefaultValue, MinimumScale, MaximunScale, ForceValue, Unit, Description, " +
            "LowAlarmLimit1Value, LowAlarmLimit2Value, LowAlarmLimit3Value, " +
            "HighAlarmLimit1Value, HighAlarmLimit2Value, HighAlarmLimit3Value, Prj_Controller_ID FROM Cfg_VarSystem"),
            r => (int)r["Prj_Controller_ID"]);
        var blocksByCtrl = GroupBy(Query(conn,
            "SELECT ID, Name, AlgName, FunctionName, Description, Prj_Controller_ID FROM Cld_FCBlock"),
            r => (int)r["Prj_Controller_ID"]);
        var inputsByCtrl = GroupBy(Query(conn,
            "SELECT ID, PinName, PointName, InitialValue, Negate, Cld_FCBlock_ID, Prj_Controller_ID FROM Cld_FCInput"),
            r => (int)r["Prj_Controller_ID"]);
        var outputsByCtrl = GroupBy(Query(conn,
            "SELECT ID, PinName, PointName, InitialValue, Negate, Cld_FCBlock_ID, Prj_Controller_ID FROM Cld_FCOutput"),
            r => (int)r["Prj_Controller_ID"]);
        var paramsByCtrl = GroupBy(Query(conn,
            "SELECT ID, Name, PValue, Cld_FCBlock_ID, Prj_Controller_ID FROM Cld_FCParameter"),
            r => (int)r["Prj_Controller_ID"]);

        foreach (var c in controllerRows)
        {
            int cid = (int)c["ID"];
            var points = BuildPoints(varsByCtrl.GetValueOrDefault(cid, []));
            var blocks = BuildBlocks(
                blocksByCtrl.GetValueOrDefault(cid, []),
                inputsByCtrl.GetValueOrDefault(cid, []),
                outputsByCtrl.GetValueOrDefault(cid, []),
                paramsByCtrl.GetValueOrDefault(cid, []),
                meta);

            controllers.Add(new ControllerModel
            {
                Id = cid,
                Address = Str(c["ControllerAddress"]),
                Name = Str(c["ControllerName"]),
                Points = points,
                Blocks = blocks,
            });
        }

        return new EngineeringModel { ProjectPath = mdbPath, Controllers = controllers };
    }

    // ---------------------------------------------------------------
    // 点表（对齐 InitPointByDatabase，含跨行粘滞局部变量）
    // ---------------------------------------------------------------
    private static List<PointModel> BuildPoints(List<Dictionary<string, object>> rows)
    {
        var points = new List<PointModel>(rows.Count);

        // 老系统把这些局部变量声明在循环外：空串时沿用上一行的解析结果（粘滞语义，忠实保留）
        float fDefaultValue = 0.0f;
        bool bDefaultValue = false;
        float maxValue = 0.0f;
        float minValue = 0.0f;
        float forceValue = 0.0f;

        foreach (var row in rows)
        {
            string name = Str(row["Name"]);
            string dataType = Str(row["DataType"]);

            string dValue = Str(row["DefaultValue"]);
            if (!string.IsNullOrEmpty(dValue))
            {
                if (dataType == "LA")
                    fDefaultValue = float.Parse(dValue, CultureInfo.InvariantCulture);
                else if (dataType == "LD")
                    bDefaultValue = dValue == "1"; // 老系统：仅 "1" 视为 true
            }

            string maxScale = Str(row["MaximunScale"]);
            if (!string.IsNullOrEmpty(maxScale))
                maxValue = float.Parse(maxScale, CultureInfo.InvariantCulture);

            string minScale = Str(row["MinimumScale"]);
            if (!string.IsNullOrEmpty(minScale))
                minValue = float.Parse(minScale, CultureInfo.InvariantCulture);

            string force = Str(row["ForceValue"]);
            if (!string.IsNullOrEmpty(force))
                forceValue = float.Parse(force, CultureInfo.InvariantCulture);
            _ = forceValue; // 老系统解析后并未写入点（Dpu.cs 1447-1477 只写 buffer/max/min）

            // 报警限值是逐点工程元数据，不采用默认值/量程的跨行粘滞语义。
            // 空值或旧工程中的非法值按 0 处理，与原系统报警配置的默认意图一致。
            double lowAlarm1 = ReadAlarmLimit(row, "LowAlarmLimit1Value");
            double lowAlarm2 = ReadAlarmLimit(row, "LowAlarmLimit2Value");
            double lowAlarm3 = ReadAlarmLimit(row, "LowAlarmLimit3Value");
            double highAlarm1 = ReadAlarmLimit(row, "HighAlarmLimit1Value");
            double highAlarm2 = ReadAlarmLimit(row, "HighAlarmLimit2Value");
            double highAlarm3 = ReadAlarmLimit(row, "HighAlarmLimit3Value");

            PointModel? p = dataType switch
            {
                "LA" => new PointModel
                {
                    Name = name, DataType = dataType, DefaultValue = fDefaultValue,
                    MaxValue = maxValue, MinValue = minValue,
                    LowAlarmLimit1Value = lowAlarm1, LowAlarmLimit2Value = lowAlarm2, LowAlarmLimit3Value = lowAlarm3,
                    HighAlarmLimit1Value = highAlarm1, HighAlarmLimit2Value = highAlarm2, HighAlarmLimit3Value = highAlarm3,
                    Unit = Str(row["Unit"]), Description = Str(row["Description"]),
                },
                "LD" => new PointModel
                {
                    Name = name, DataType = dataType, DefaultValue = bDefaultValue,
                    LowAlarmLimit1Value = lowAlarm1, LowAlarmLimit2Value = lowAlarm2, LowAlarmLimit3Value = lowAlarm3,
                    HighAlarmLimit1Value = highAlarm1, HighAlarmLimit2Value = highAlarm2, HighAlarmLimit3Value = highAlarm3,
                    Unit = Str(row["Unit"]), Description = Str(row["Description"]),
                },
                "LP" => new PointModel
                {
                    Name = name, DataType = dataType, DefaultValue = (short)0,
                    LowAlarmLimit1Value = lowAlarm1, LowAlarmLimit2Value = lowAlarm2, LowAlarmLimit3Value = lowAlarm3,
                    HighAlarmLimit1Value = highAlarm1, HighAlarmLimit2Value = highAlarm2, HighAlarmLimit3Value = highAlarm3,
                    Unit = Str(row["Unit"]), Description = Str(row["Description"]),
                },
                "LP32" => new PointModel
                {
                    Name = name, DataType = dataType, DefaultValue = 0L,
                    LowAlarmLimit1Value = lowAlarm1, LowAlarmLimit2Value = lowAlarm2, LowAlarmLimit3Value = lowAlarm3,
                    HighAlarmLimit1Value = highAlarm1, HighAlarmLimit2Value = highAlarm2, HighAlarmLimit3Value = highAlarm3,
                    Unit = Str(row["Unit"]), Description = Str(row["Description"]),
                },
                _ => null, // 其他类型老系统同样忽略
            };

            if (p != null)
                points.Add(p);
        }

        return points;
    }

    // ---------------------------------------------------------------
    // 块表（对齐 InitFCByDatabase）
    // ---------------------------------------------------------------
    private static List<BlockModel> BuildBlocks(
        List<Dictionary<string, object>> blockRows,
        List<Dictionary<string, object>> inputRows,
        List<Dictionary<string, object>> outputRows,
        List<Dictionary<string, object>> paramRows,
        MetaCatalog meta)
    {
        // 点默认值查询表：InitFCByDatabase 用 pointDict（本控制器点）做 Convert.ToSingle
        // 注意老系统 pointDict 在 InitPointByDatabase 中构建——这里由调用方传入会更纯，
        // 但老系统同样是"本控制器点表"，直接内联重建，键区分大小写与老系统一致。

        var inputsByBlock = GroupBy(inputRows, r => (int)r["Cld_FCBlock_ID"]);
        var outputsByBlock = GroupBy(outputRows, r => (int)r["Cld_FCBlock_ID"]);
        var paramsByBlock = GroupBy(paramRows, r => (int)r["Cld_FCBlock_ID"]);

        var blocks = new List<BlockModel>(blockRows.Count);
        var paramState = new ParamParseState();

        foreach (var row in blockRows)
        {
            string fcName = Str(row["FunctionName"]);
            string algName = Str(row["AlgName"]);

            if (string.IsNullOrEmpty(fcName))
                continue;
            if (Array.IndexOf(SkippedFcNames, fcName) >= 0)
                continue;

            int blockId = (int)row["ID"];
            var pins = new List<PinDetailModel>();
            var pinNames = new HashSet<string>(StringComparer.Ordinal); // 老系统 Dictionary.Add 语义：区分大小写、重复即抛

            void AddPin(PinDetailModel? pin)
            {
                if (pin is null)
                    return; // 数组超长等"静默不添加"路径
                if (!pinNames.Add(pin.PinName))
                    throw new InvalidOperationException($"块 {algName} 管脚 {pin.PinName} 重复定义（老系统此处抛异常）");
                pins.Add(pin);
            }

            // -------- 输入管脚 --------
            foreach (var input in inputsByBlock.GetValueOrDefault(blockId, []))
            {
                string pinName = Str(input["PinName"]);
                string pointName = Str(input["PointName"]);
                string initialValue = Str(input["InitialValue"]);
                AddPin(BuildIoPin(pinName, pointName, initialValue));
            }

            // -------- 输出管脚 --------
            foreach (var output in outputsByBlock.GetValueOrDefault(blockId, []))
            {
                string pinName = Str(output["PinName"]);
                string pointName = Str(output["PointName"]);
                string initialValue = Str(output["InitialValue"]);
                AddPin(BuildIoPin(pinName, pointName, initialValue));
            }

            // -------- 规格参数（Constant）--------
            // 老系统 DataType 局部变量跨参数、跨块粘滞（Operation.cs 338 声明于块循环外，
            // 每控制器一个 InitOperation 实例），忠实保留：粘滞范围 = 单个控制器
            foreach (var param in paramsByBlock.GetValueOrDefault(blockId, []))
            {
                string pValue = Str(param["PValue"]);
                string pinName = Str(param["Name"]);
                AddPin(BuildParamPin(fcName, pinName, pValue, meta, paramState));
            }

            AddPin(new PinDetailModel
            {
                PinName = "Description",
                PointName = "",
                HasDefaultValue = true,
                DefaultValue = Str(row["Description"]),
            });

            blocks.Add(new BlockModel
            {
                Name = algName,
                FcName = fcName,
                Description = Str(row["Description"]),
                Pins = pins,
            });
        }

        return blocks;
    }

    /// <summary>
    /// 输入/输出行 → 管脚细节。老系统语义（Operation.cs 367-484）：
    /// PointName 非空 → PinDetails(PointName, 点默认值 float)，此时忽略 InitialValue；
    /// 否则 InitialValue 非空 → true/false→1/0，数值 TryParse，作为默认值；
    /// 否则无默认值。
    /// 注意：老系统"点默认值"用 Convert.ToSingle(pointDict[名].DefaultValue)，点不存在/转换失败取 0f。
    /// 这里推迟到装配阶段再解析（因为跨 DPU 点在本控制器 pointDict 必然缺失，统一 0f），
    /// 为完全对齐，我们保留一个标记 DefaultFromPoint。
    /// </summary>
    private static PinDetailModel BuildIoPin(string pinName, string pointName, string initialValue)
    {
        if (!string.IsNullOrEmpty(pointName))
        {
            var (cleanName, reversed) = AnalysePointName(pointName);
            // 老系统用"原始 PointName"（含 ~ / ,）查 pointDict（Operation.cs 380/447），
            // 取反点因带 ~ 必然查不到 → 默认 0f。这里保留原始名以复刻该行为。
            return new PinDetailModel
            {
                PinName = pinName,
                PointName = cleanName,
                Reversed = reversed,
                HasDefaultValue = true,
                DefaultValue = new DefaultFromPoint(pointName),
            };
        }

        if (!string.IsNullOrEmpty(initialValue))
        {
            float val;
            if (initialValue.Equals("true", StringComparison.OrdinalIgnoreCase))
                val = 1;
            else if (initialValue.Equals("false", StringComparison.OrdinalIgnoreCase))
                val = 0;
            else
                float.TryParse(initialValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out val);
            return new PinDetailModel
            {
                PinName = pinName,
                HasDefaultValue = true,
                DefaultValue = val,
            };
        }

        return new PinDetailModel { PinName = pinName, HasDefaultValue = false };
    }

    /// <summary>跨参数/跨块粘滞的 DataType 状态（对齐老系统 Operation.cs 338 行局部变量的事实语义）。</summary>
    private sealed class ParamParseState
    {
        public string DataType = string.Empty;
    }

    private static PinDetailModel? BuildParamPin(string fcName, string pinName, string pValue, MetaCatalog meta, ParamParseState state)
    {
        // HollySys 内部块参数（本工程无此数据，仍保留路径）
        if (pValue.Contains(":="))
        {
            return new PinDetailModel
            {
                PinName = pinName, PointName = "", HasDefaultValue = true, DefaultValue = pValue,
            };
        }

        if (string.IsNullOrEmpty(pValue))
            return new PinDetailModel { PinName = pinName, HasDefaultValue = false };

        float val = 0;

        if (pValue.Equals("true", StringComparison.OrdinalIgnoreCase))
            val = 1;
        else if (pValue.Equals("false", StringComparison.OrdinalIgnoreCase))
            val = 0;
        else
        {
            if (meta.TryGetDataType(fcName, pinName, out var metaType))
            {
                state.DataType = metaType;
                if (!metaType.StartsWith("string*", StringComparison.Ordinal))
                    float.TryParse(pValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out val);
            }
            // meta 缺失：老系统仅记录日志，DataType 沿用上一次的值（粘滞）
        }

        string dataType = state.DataType;
        if (dataType.StartsWith("string*", StringComparison.Ordinal))
        {
            return new PinDetailModel
            {
                PinName = pinName, PointName = "", HasDefaultValue = true, DefaultValue = pValue,
            };
        }

        if (pValue.Contains(','))
        {
            // 数组参数：长度上限取 DataType "xxx*N" 的 N；超长则整个管脚被丢弃
            // （老系统仅 Debug 输出、不 pins.Add —— Operation.cs 547-551），返回 null 表示"不添加"
            string[] parts = pValue.Split(',');
            int arrLength = int.Parse(dataType.Split('*')[1], CultureInfo.InvariantCulture);
            if (parts.Length > arrLength)
                return null;
            var arr = new float[parts.Length];
            for (int i = 0; i < arr.Length; i++)
                arr[i] = float.Parse(parts[i], CultureInfo.InvariantCulture);
            return new PinDetailModel { PinName = pinName, HasDefaultValue = true, DefaultValue = arr };
        }

        return new PinDetailModel { PinName = pinName, HasDefaultValue = true, DefaultValue = val };
    }

    /// <summary>
    /// 对齐老系统 PinDetails.AnalysePointName（Function.cs 343-362）：
    /// 含 ',' → 不取反、原样保留（含内部 '~'，由 Command 构造逐段再解析）；
    /// 否则含 '~' → 取反并剥离全部 '~'。
    /// </summary>
    internal static (string PointName, bool Reversed) AnalysePointName(string pointName)
    {
        if (pointName.Contains(','))
            return (pointName, false);
        if (pointName.Contains('~'))
            return (pointName.Replace("~", ""), true);
        return (pointName, false);
    }

    // ---------------------------------------------------------------
    // Meta_FCMaster / Meta_FCDetail（参数 DataType 目录）
    // ---------------------------------------------------------------
    private sealed class MetaCatalog
    {
        // FunctionName（首个 master 生效）→ PinName → DataType
        private readonly Dictionary<string, Dictionary<string, string>> _map = new(StringComparer.Ordinal);

        public void Add(string functionName, string pinName, string dataType)
        {
            if (!_map.TryGetValue(functionName, out var pinMap))
            {
                pinMap = new Dictionary<string, string>(StringComparer.Ordinal);
                _map[functionName] = pinMap;
            }
            pinMap.TryAdd(pinName, dataType);
        }

        public bool TryGetDataType(string functionName, string pinName, out string dataType)
        {
            dataType = string.Empty;
            return _map.TryGetValue(functionName, out var pinMap)
                   && pinMap.TryGetValue(pinName, out dataType!);
        }
    }

    private static MetaCatalog LoadMetaCatalog(OleDbConnection conn)
    {
        // 老系统 FunctionCodeMasterManager.Initialize：全表 master（自然序），
        // FunctionName 首见的 master 生效（后续同名整个忽略），其 Detail_List 建 PinName→DataType 字典
        // （PinName 在 master 内唯一，行序不影响该字典，仍保持自然序以对齐）。
        var catalog = new MetaCatalog();

        var masters = Query(conn, "SELECT ID, FunctionName FROM Meta_FCMaster");
        var detailsByMaster = GroupBy(Query(conn,
            "SELECT ID, PinName, DataType, Meta_FCMaster_ID FROM Meta_FCDetail"),
            r => (int)r["Meta_FCMaster_ID"]);

        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var m in masters)
        {
            string fn = Str(m["FunctionName"]);
            if (!seenNames.Add(fn))
                continue;
            int masterId = (int)m["ID"];
            foreach (var d in detailsByMaster.GetValueOrDefault(masterId, []))
                catalog.Add(fn, Str(d["PinName"]), Str(d["DataType"]));
        }
        return catalog;
    }

    // ---------------------------------------------------------------
    // 基础查询工具
    // ---------------------------------------------------------------
    private static List<Dictionary<string, object>> Query(OleDbConnection conn, string sql)
    {
        using var cmd = new OleDbCommand(sql, conn);
        using var reader = cmd.ExecuteReader();
        var names = new string[reader.FieldCount];
        for (int i = 0; i < reader.FieldCount; i++)
            names[i] = reader.GetName(i);

        var rows = new List<Dictionary<string, object>>();
        while (reader.Read())
        {
            var row = new Dictionary<string, object>(names.Length, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < names.Length; i++)
                row[names[i]] = reader.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }

    private static Dictionary<int, List<Dictionary<string, object>>> GroupBy(
        List<Dictionary<string, object>> rows, Func<Dictionary<string, object>, int> keySelector)
    {
        var dict = new Dictionary<int, List<Dictionary<string, object>>>();
        foreach (var row in rows)
        {
            int key = keySelector(row);
            if (!dict.TryGetValue(key, out var bucket))
            {
                bucket = [];
                dict[key] = bucket;
            }
            bucket.Add(row);
        }
        return dict;
    }

    private static string Str(object? v) => v is DBNull or null ? "" : (string)v;

    private static double ReadAlarmLimit(Dictionary<string, object> row, string column)
    {
        if (!row.TryGetValue(column, out object? raw) || raw is null or DBNull)
            return 0d;

        if (raw is string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0d;
            return double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture, out double parsed) ? parsed : 0d;
        }

        try
        {
            return Convert.ToDouble(raw, CultureInfo.InvariantCulture);
        }
        catch
        {
            return 0d;
        }
    }
}

/// <summary>
/// IO 管脚的默认值占位：老系统在 InitFCByDatabase 里就地解析
/// Convert.ToSingle(pointDict[PointName].DefaultValue)，点缺失/失败取 0f。
/// 新系统把点表解析成模型后再在装配时统一解析，保持同一结果。
/// </summary>
public sealed record DefaultFromPoint(string PointName);
