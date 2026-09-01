using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using RWVDCS.Engineering;
using RWVDCS.Runtime;

namespace RWVDCS.Api;

/// <summary>宿主选项。</summary>
public sealed class RuntimeHostOptions
{
    /// <summary>功能块程序集（BlockCatalog 来源）。</summary>
    public required Assembly BlocksAssembly { get; init; }

    /// <summary>数据目录（工况/快照仓库、版本档案、历史站会话）。</summary>
    public required string DataDirectory { get; init; }

    /// <summary>热更源码目录（按功能码名找 FC_*.cs）。</summary>
    public string? BlocksSourceDir { get; init; }

    /// <summary>启用内嵌历史站。</summary>
    public bool EnableHistory { get; init; }

    /// <summary>Arena MMF 后备目录（null = 纯内存）。</summary>
    public string? ArenaDirectory { get; init; }

    /// <summary>功能块管脚值持久化实现；默认写当前 Access MDB，测试可注入事务替身。</summary>
    public IFcPinValueStore? FcPinValueStore { get; init; }
}

public sealed record FcPinValueUpdateResult(
    string DpuName,
    string AlgName,
    int CldFCBlockId,
    int DatabaseRecordId,
    string FcName,
    string PinName,
    string PinType,
    string MdbPath,
    string DatabaseTable,
    string DatabaseColumn,
    string? PointName,
    string OldDatabaseValue,
    string NewDatabaseValue,
    string? PersistedDatabaseValue,
    bool DatabaseVerified,
    object? OldRuntimeValue,
    object? NewRuntimeValue,
    string Fingerprint);

/// <summary>工程版本档案条目（versions.json）。</summary>
public sealed class VersionEntry
{
    public int Version { get; set; }
    public string Fingerprint { get; set; } = "";
    public string Source { get; set; } = "";   // load / download / condition
    public string MdbPath { get; set; } = "";
    public DateTime TimeUtc { get; set; }
    public string? Comment { get; set; }
}

/// <summary>待提交的下装计划（prepare 产物，commit 消费）。</summary>
public sealed class DownloadPlan
{
    public string PlanId { get; } = Guid.NewGuid().ToString("N")[..8];
    public required string MdbPath { get; init; }
    public required string NewFingerprint { get; init; }
    public required string OldFingerprint { get; init; }
    public required ModelDiffReport Diff { get; init; }
    public required EngineeringModel PristineModel { get; init; }
    public DateTime PreparedAtUtc { get; } = DateTime.UtcNow;

    /// <summary>静态预检发现的致命问题（悬空 pin 引用等；非空时 commit 将被拒绝）。</summary>
    public List<string> Errors { get; init; } = [];
}

/// <summary>
/// 当前代 Runtime 的请求级生命周期租约。租约释放前，宿主不会销毁其关联的 Arena。
/// </summary>
public sealed class RuntimeReadLease : IDisposable
{
    private RuntimeHost? _owner;

    internal RuntimeReadLease(RuntimeHost owner, DcsRuntime runtime)
    {
        _owner = owner;
        Runtime = runtime;
    }

    public DcsRuntime Runtime { get; }

    public void Dispose()
    {
        RuntimeHost? owner = Interlocked.Exchange(ref _owner, null);
        owner?.ReleaseRuntimeLease();
    }
}

/// <summary>
/// 运行时宿主编排层：项目装载、运行控制、工况/快照、在线下装、热更、
/// 交叉引用、日志——Web API 与 REPL 共用的唯一入口。
/// </summary>
/// <remarks>
/// 并发模型：结构性操作（装载/下装/工况加载）持 <see cref="_structureLock"/> 且经
/// 调度器周期边界执行；普通点值/强制写走运行时原子小写入，功能码管脚持久化更新则在
/// 结构锁内经周期边界协调 MDB、Runtime 与工程模型。
/// </remarks>
public sealed class RuntimeHost : IDisposable
{
    private readonly RuntimeHostOptions _options;
    private readonly BlockCatalog _catalog;
    private readonly IFcPinValueStore _fcPinValueStore;
    private readonly SemaphoreSlim _structureLock = new(1, 1);
    private readonly object _runtimeLeaseGate = new();
    private readonly object _pinValueUpdateGate = new();
    private readonly List<VersionEntry> _versions = [];
    private int _activeRuntimeLeases;
    private bool _runtimeRetiring;
    private bool _disposed;
    private DownloadPlan? _pendingPlan;
    private IReadOnlyDictionary<string, IReadOnlyDictionary<string, PointModel>> _pointMetadataByDpu =
        new Dictionary<string, IReadOnlyDictionary<string, PointModel>>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, IReadOnlyDictionary<string, BlockModel>> _blockMetadataByDpu =
        new Dictionary<string, IReadOnlyDictionary<string, BlockModel>>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<int, ControllerModel> _controllerMetadataById =
        new Dictionary<int, ControllerModel>();

    public LogBuffer Log { get; } = new();
    public ConditionStore Store { get; }

    // ---- 当前代运行时（结构性操作时整组替换）
    public DcsRuntime? Runtime { get; private set; }
    public ScanScheduler? Scheduler { get; private set; }
    public XrefIndex? Xref { get; private set; }
    public HistoryRecorder? History { get; private set; }
    public BlockHotSwapper? Swapper { get; private set; }

    /// <summary>纯净工程模型（未经装配改写；diff/指纹的参照）。</summary>
    public EngineeringModel? PristineModel { get; private set; }

    public string? MdbPath { get; private set; }
    public string Fingerprint { get; private set; } = "";
    public int ProjectVersion { get; private set; }
    public DateTime? LoadedAtUtc { get; private set; }
    public RuntimeBuildReport? BuildReport { get; private set; }

    public bool ProjectLoaded => Runtime != null;

    /// <summary>
    /// 获取当前代 Runtime 的请求级租约。工程换代和宿主关闭会等待所有租约释放后，
    /// 才销毁旧 Runtime/Arena。
    /// </summary>
    public RuntimeReadLease AcquireRuntimeLease()
        => TryAcquireRuntimeLease() ?? throw new InvalidOperationException("尚未装载工程");

    /// <summary>尝试获取当前代 Runtime 租约；尚未装载工程或宿主已关闭时返回 null。</summary>
    public RuntimeReadLease? TryAcquireRuntimeLease()
    {
        lock (_runtimeLeaseGate)
        {
            while (_runtimeRetiring && !_disposed)
                Monitor.Wait(_runtimeLeaseGate);

            if (_disposed || Runtime == null)
                return null;

            _activeRuntimeLeases++;
            return new RuntimeReadLease(this, Runtime);
        }
    }

    internal void ReleaseRuntimeLease()
    {
        lock (_runtimeLeaseGate)
        {
            if (_activeRuntimeLeases <= 0)
                throw new InvalidOperationException("Runtime 生命周期租约计数失衡");

            _activeRuntimeLeases--;
            if (_activeRuntimeLeases == 0)
                Monitor.PulseAll(_runtimeLeaseGate);
        }
    }

    /// <summary>按 DPU/点名 O(1) 查询当前代的只读工程元数据。</summary>
    public bool TryGetPointModel(string dpuName, string pointName, out PointModel point)
    {
        var index = _pointMetadataByDpu;
        if (index.TryGetValue(dpuName, out var points) && points.TryGetValue(pointName, out point!))
            return true;
        point = null!;
        return false;
    }

    /// <summary>按 DPU/块名 O(1) 查询当前代的只读工程元数据。</summary>
    public bool TryGetBlockModel(string dpuName, string blockName, out BlockModel block)
    {
        var index = _blockMetadataByDpu;
        if (index.TryGetValue(dpuName, out var blocks) && blocks.TryGetValue(blockName, out block!))
            return true;
        block = null!;
        return false;
    }

    /// <summary>
    /// 按 DPU/实例名在线修改 Constant 或未连接测点的 Input 管脚值：
    /// MDB 事务、Runtime 字段和工程模型协同更新。
    /// </summary>
    public FcPinValueUpdateResult UpdateFcPinValue(
        string dpuName,
        string algName,
        string pinName,
        string pValue)
    {
        dpuName = RequireName(dpuName, nameof(dpuName));
        algName = RequireName(algName, nameof(algName));
        pinName = RequireName(pinName, nameof(pinName));
        ArgumentNullException.ThrowIfNull(pValue);

        _structureLock.Wait();
        try
        {
            using RuntimeReadLease runtimeLease = AcquireRuntimeLease();
            lock (_pinValueUpdateGate)
                return UpdateFcPinValueCore(runtimeLease.Runtime, dpuName, algName, pinName, pValue);
        }
        finally
        {
            _structureLock.Release();
        }
    }

    private FcPinValueUpdateResult UpdateFcPinValueCore(
        DcsRuntime runtime,
        string dpuName,
        string algName,
        string pinName,
        string pValue)
    {
        if (!TryGetBlockModel(dpuName, algName, out BlockModel block))
            throw new KeyNotFoundException($"未找到功能块实例：{dpuName}/{algName}");
        if (block.ID <= 0)
            throw new InvalidDataException($"功能块实例 {dpuName}/{algName} 缺少 Cld_FCBlock.ID");

        DpuRuntime dpu = runtime.FindDpu(dpuName)
            ?? throw new KeyNotFoundException($"当前 Runtime 中不存在 DPU：{dpuName}");
        BlockCommand command = dpu.FindCommand(algName)
            ?? throw new KeyNotFoundException($"当前 Runtime 中不存在功能块：{dpuName}/{algName}");

        FieldInfo field = command.Fc.GetType().GetField(
            pinName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase)
            ?? throw new KeyNotFoundException($"功能块 {algName} 不存在字段：{pinName}");
        PinTypes pinType = field.GetCustomAttribute<PinTypeAttribute>()?.PinType ?? PinTypes.None;
        if (pinType is not (PinTypes.Constant or PinTypes.Input) || field.IsInitOnly)
            throw new InvalidOperationException(
                $"字段 {field.Name} 的管脚类型为 {pinType}，仅允许修改 Constant 或未连接测点的 Input");

        PinDetailModel modelPin = block.FindPin(field.Name)
            ?? throw new KeyNotFoundException($"工程模型中不存在管脚：{dpuName}/{algName}.{field.Name}");
        if (pinType == PinTypes.Input && !string.IsNullOrWhiteSpace(modelPin.PointName))
            throw new InvalidOperationException(
                $"输入管脚 {dpuName}/{algName}.{field.Name} 已连接测点 {modelPin.PointName}，不能修改 InitialValue");
        string mdbPath = MdbPath
            ?? throw new InvalidOperationException("当前工程没有可写入的 MDB 路径");

        object? oldRuntimeFieldValue = field.GetValue(command.Fc);
        object? oldRuntimeValue;
        object newRuntimeValue;
        if (pinType == PinTypes.Constant)
        {
            oldRuntimeValue = ClonePinValue(oldRuntimeFieldValue);
            newRuntimeValue = ParseConstantValue(field.FieldType, pValue);
        }
        else
        {
            if (oldRuntimeFieldValue is not IValuable valuable)
                throw new NotSupportedException(
                    $"Input 字段 {field.Name} 的类型 {field.FieldType.FullName} 未实现 IValuable");
            oldRuntimeValue = ClonePinValue(valuable.Value);
            newRuntimeValue = ParseInputValue(field.FieldType, pValue);
        }

        object? oldModelValue = modelPin.DefaultValue;
        bool oldHasDefaultValue = modelPin.HasDefaultValue;
        object? oldSwapperValue = null;
        bool oldSwapperHasDefaultValue = false;
        bool swapperUpdated = false;

        using IFcPinValueUpdate databaseUpdate = pinType == PinTypes.Constant
            ? _fcPinValueStore.BeginConstantUpdate(mdbPath, block.ID, field.Name, pValue)
            : _fcPinValueStore.BeginInputUpdate(mdbPath, block.ID, field.Name, pValue);
        try
        {
            void ApplyNewValue()
            {
                SetRuntimePinValue(command.Fc, field, pinType, newRuntimeValue);
                modelPin.DefaultValue = ClonePinValue(newRuntimeValue);
                modelPin.HasDefaultValue = true;
                if (Swapper != null)
                {
                    swapperUpdated = Swapper.TrySetPinDefault(
                        dpuName,
                        algName,
                        field.Name,
                        newRuntimeValue,
                        hasDefaultValue: true,
                        out oldSwapperValue,
                        out oldSwapperHasDefaultValue);
                    if (!swapperUpdated)
                        throw new InvalidOperationException($"热更模型中不存在管脚：{dpuName}/{algName}.{field.Name}");
                }
            }

            if (Scheduler != null)
                Scheduler.RunAtCycleBoundary(ApplyNewValue);
            else
                ApplyNewValue();
            databaseUpdate.Commit();
        }
        catch (Exception updateError)
        {
            bool restoreOldState = !databaseUpdate.CommitSucceeded || databaseUpdate.DatabaseRestored;
            if (!restoreOldState)
            {
                // 数据库已提交且补偿失败时不能再盲目恢复 Runtime，否则会明确制造两边不一致。
                // 保留新 Runtime/模型，记录绝对 MDB 目标并让接口返回失败，等待人工核验数据库状态。
                if (PristineModel != null)
                    Fingerprint = ProjectFingerprint.Compute(PristineModel);
                Log.Error("管脚值",
                    $"MDB 提交后状态无法确认且补偿失败，保留运行时新值：{databaseUpdate.MdbPath}，"
                    + $"{databaseUpdate.DatabaseTable}.{databaseUpdate.DatabaseColumn}，"
                    + $"ID={databaseUpdate.RecordId}；{updateError.Message}");
                throw;
            }

            try
            {
                void RestoreOldValue()
                {
                    field.SetValue(command.Fc, oldRuntimeFieldValue);
                    modelPin.DefaultValue = oldModelValue;
                    modelPin.HasDefaultValue = oldHasDefaultValue;
                    if (swapperUpdated)
                    {
                        Swapper!.TrySetPinDefault(
                            dpuName,
                            algName,
                            field.Name,
                            oldSwapperValue,
                            oldSwapperHasDefaultValue,
                            out _,
                            out _);
                    }
                }

                if (Scheduler != null)
                    Scheduler.RunAtCycleBoundary(RestoreOldValue);
                else
                    RestoreOldValue();
            }
            catch (Exception restoreError)
            {
                throw new AggregateException(
                    "管脚值更新失败，且运行时旧值恢复失败",
                    updateError,
                    restoreError);
            }
            throw;
        }

        if (PristineModel != null)
            Fingerprint = ProjectFingerprint.Compute(PristineModel);
        object? appliedValue = GetRuntimePinValue(command.Fc, field, pinType);
        Log.Info("管脚值", $"{dpuName}/{algName}.{field.Name} [{pinType}]: "
                     + $"{FormatPinValue(oldRuntimeValue)} → {FormatPinValue(appliedValue)} "
                     + $"({databaseUpdate.MdbPath}；"
                     + $"{databaseUpdate.DatabaseTable}.{databaseUpdate.DatabaseColumn}；"
                     + $"ID={databaseUpdate.RecordId}；回读={databaseUpdate.PersistedValue}；"
                     + $"verified={databaseUpdate.DatabaseVerified}；Cld_FCBlock_ID={block.ID})");

        return new FcPinValueUpdateResult(
            dpuName,
            algName,
            block.ID,
            databaseUpdate.RecordId,
            block.FcName,
            field.Name,
            pinType.ToString(),
            databaseUpdate.MdbPath,
            databaseUpdate.DatabaseTable,
            databaseUpdate.DatabaseColumn,
            databaseUpdate.PointName,
            databaseUpdate.OldValue,
            pValue,
            databaseUpdate.PersistedValue,
            databaseUpdate.DatabaseVerified,
            ClonePinValue(oldRuntimeValue),
            ClonePinValue(appliedValue),
            Fingerprint);
    }

    /// <summary>按控制器数据库 ID 查询当前代的控制器地址。</summary>
    public bool TryGetControllerAddress(int controllerId, out string address)
    {
        var index = _controllerMetadataById;
        if (index.TryGetValue(controllerId, out var controller))
        {
            address = controller.Address;
            return true;
        }
        address = string.Empty;
        return false;
    }

    /// <summary>Runtime 即将被替换；实时兼容层据此停止向旧 Arena 发起读写。</summary>
    public event Action? RuntimeChanging;

    public event Action? RuntimeSwapped;

    public RuntimeHost(RuntimeHostOptions options)
    {
        _options = options;
        _catalog = new BlockCatalog(options.BlocksAssembly);
        _fcPinValueStore = options.FcPinValueStore ?? new MdbFcPinValueStore();
        Directory.CreateDirectory(options.DataDirectory);
        Store = new ConditionStore(options.DataDirectory);
        LoadVersionRegistry();
    }

    private static string RequireName(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("参数不能为空", parameterName);
        return value.Trim();
    }

    private static object ParseConstantValue(Type type, string value)
    {
        if (type == typeof(string))
            return value;
        if (type == typeof(bool))
        {
            if (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase))
                return true;
            if (value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase))
                return false;
            throw new FormatException($"{value} 无法转换为 Boolean");
        }
        if (type.IsEnum)
            return Enum.Parse(type, value, ignoreCase: true);
        if (type.IsArray)
        {
            Type elementType = type.GetElementType()
                ?? throw new NotSupportedException($"不支持的数组参数类型：{type.FullName}");
            string[] parts = value.Length == 0 ? [] : value.Split(',');
            Array result = Array.CreateInstance(elementType, parts.Length);
            for (int i = 0; i < parts.Length; i++)
                result.SetValue(ParseConstantValue(elementType, parts[i].Trim()), i);
            return result;
        }
        if (type == typeof(char))
        {
            if (value.Length != 1)
                throw new FormatException($"{value} 无法转换为 Char");
            return value[0];
        }
        if (type.IsPrimitive || type == typeof(decimal))
            return Convert.ChangeType(value, type, CultureInfo.InvariantCulture)
                ?? throw new FormatException($"{value} 无法转换为 {type.Name}");
        throw new NotSupportedException($"不支持在线修改的参数类型：{type.FullName}");
    }

    private static object ParseInputValue(Type fieldType, string value)
        => fieldType == typeof(LA)
            ? ParseConstantValue(typeof(float), value)
            : fieldType == typeof(LD)
                ? ParseConstantValue(typeof(bool), value)
                : fieldType == typeof(LP)
                    ? ParseConstantValue(typeof(ushort), value)
                    : fieldType == typeof(LP32)
                        ? ParseConstantValue(typeof(uint), value)
                        : throw new NotSupportedException(
                            $"不支持在线修改的 Input 管脚类型：{fieldType.FullName}");

    private static void SetRuntimePinValue(
        Function function,
        FieldInfo field,
        PinTypes pinType,
        object value)
    {
        if (pinType == PinTypes.Constant)
        {
            field.SetValue(function, value);
            return;
        }

        object boxedPin = field.GetValue(function)
            ?? throw new InvalidOperationException($"Input 字段 {field.Name} 的值为空");
        if (boxedPin is not IValuable valuable)
            throw new NotSupportedException(
                $"Input 字段 {field.Name} 的类型 {field.FieldType.FullName} 未实现 IValuable");

        // IValuable 指向同一个装箱结构；写 Value 后再把完整结构写回字段，
        // 从而只改变过程值并保留 Quality/IsForced/IsAlarm 等其他成员。
        valuable.Value = value;
        field.SetValue(function, boxedPin);
    }

    private static object? GetRuntimePinValue(Function function, FieldInfo field, PinTypes pinType)
    {
        object? fieldValue = field.GetValue(function);
        return pinType == PinTypes.Input && fieldValue is IValuable valuable
            ? valuable.Value
            : fieldValue;
    }

    private static object? ClonePinValue(object? value)
        => value is Array array ? array.Clone() : value;

    private static string FormatPinValue(object? value)
        => value is Array array
            ? string.Join(",", array.Cast<object?>().Select(FormatPinValue))
            : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null";

    // =================================================================
    // 项目装载
    // =================================================================

    /// <summary>装载工程库（重建整套运行时；已有工程时先停旧）。</summary>
    public void LoadProject(string mdbPath, bool firstRun = true, string versionSource = "load")
    {
        if (!File.Exists(mdbPath))
            throw new FileNotFoundException("工程库不存在", mdbPath);

        _structureLock.Wait();
        try
        {
            var sw = Stopwatch.StartNew();
            var pristine = MdbEngineeringReader.Load(mdbPath);
            string fingerprint = ProjectFingerprint.Compute(pristine);
            var buildModel = pristine.Clone();

            var runtime = RuntimeBuilder.Build(buildModel, _catalog,
                new RuntimeBuildOptions { ArenaDirectory = _options.ArenaDirectory });

            if (firstRun)
                runtime.FirstRun();
            sw.Stop();

            SwapRuntime(runtime, buildModel, pristine, mdbPath, fingerprint, ScanState.Stopped);
            RegisterVersion(fingerprint, versionSource, mdbPath, null);

            var rpt = runtime.Report;
            Log.Info("工程", $"已装载 {Path.GetFileName(mdbPath)}：{runtime.Dpus.Count} DPU / " +
                             $"{rpt.PointCount:N0} 点 + {rpt.IntermediatePointCount:N0} 中间点 / {rpt.CommandCount:N0} 块，" +
                             $"指纹 {fingerprint}，v{ProjectVersion}，{sw.ElapsedMilliseconds:N0} ms" +
                             (firstRun ? "（含 FirstRun）" : ""));
            foreach (var err in rpt.Errors.Take(5))
                Log.Warn("工程", err);
        }
        finally
        {
            _structureLock.Release();
        }
    }

    /// <summary>整组替换当前代运行时设施（调用方持结构锁）。</summary>
    private void SwapRuntime(DcsRuntime newRuntime, EngineeringModel buildModel, EngineeringModel pristine,
        string mdbPath, string fingerprint, ScanState restoreState)
    {
        RuntimeChanging?.Invoke();

        EnterRuntimeRetirement();
        try
        {
            if (_disposed)
            {
                newRuntime.Dispose();
                throw new ObjectDisposedException(nameof(RuntimeHost));
            }

            // 进入退休屏障后，不再有请求持有旧 PointSlotRef；此时停止扫描并释放旧 Arena。
            Scheduler?.Stop();
            History?.Dispose();
            var oldRuntime = Runtime;

            Runtime = newRuntime;
            PristineModel = pristine;
            _pointMetadataByDpu = BuildPointMetadataIndex(pristine);
            _blockMetadataByDpu = BuildBlockMetadataIndex(pristine);
            _controllerMetadataById = BuildControllerMetadataIndex(pristine);
            MdbPath = mdbPath;
            Fingerprint = fingerprint;
            LoadedAtUtc = DateTime.UtcNow;
            BuildReport = newRuntime.Report;

            Scheduler = new ScanScheduler(newRuntime);
            Xref = new XrefIndex(newRuntime);
            Swapper = new BlockHotSwapper(newRuntime, buildModel);
            History = _options.EnableHistory
                ? new HistoryRecorder(newRuntime, new HistoryOptions
                {
                    Directory = Path.Combine(_options.DataDirectory, "history"),
                })
                : null;
            if (History != null)
                Scheduler.AfterDpuStep = History.OnDpuStep;

            oldRuntime?.Dispose();

            if (restoreState == ScanState.Running)
                Scheduler.Start();
            else if (restoreState == ScanState.Paused)
                Scheduler.Pause(); // 保留暂停态：下装/换代后单步继续有效
        }
        finally
        {
            ExitRuntimeRetirement();
        }

        RuntimeSwapped?.Invoke();
    }

    private void EnterRuntimeRetirement()
    {
        Monitor.Enter(_runtimeLeaseGate);
        _runtimeRetiring = true;
        while (_activeRuntimeLeases > 0)
            Monitor.Wait(_runtimeLeaseGate);
    }

    private void ExitRuntimeRetirement()
    {
        _runtimeRetiring = false;
        Monitor.PulseAll(_runtimeLeaseGate);
        Monitor.Exit(_runtimeLeaseGate);
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, PointModel>> BuildPointMetadataIndex(
        EngineeringModel model)
    {
        var byDpu = new Dictionary<string, IReadOnlyDictionary<string, PointModel>>(
            model.Controllers.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var controller in model.Controllers)
        {
            var points = new Dictionary<string, PointModel>(StringComparer.OrdinalIgnoreCase);
            foreach (var point in controller.Points)
                points.TryAdd(point.Name, point); // 与 RuntimeBuilder 的重名首见生效一致
            byDpu.TryAdd(controller.Name, points);
        }

        return byDpu;
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, BlockModel>> BuildBlockMetadataIndex(
        EngineeringModel model)
    {
        var byDpu = new Dictionary<string, IReadOnlyDictionary<string, BlockModel>>(
            model.Controllers.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var controller in model.Controllers)
        {
            var blocks = new Dictionary<string, BlockModel>(StringComparer.OrdinalIgnoreCase);
            foreach (var block in controller.Blocks)
                blocks.TryAdd(block.Name, block); // 与 RuntimeBuilder 的重名首见生效一致
            byDpu.TryAdd(controller.Name, blocks);
        }

        return byDpu;
    }

    private static IReadOnlyDictionary<int, ControllerModel> BuildControllerMetadataIndex(EngineeringModel model)
    {
        var byId = new Dictionary<int, ControllerModel>(model.Controllers.Count);
        foreach (var controller in model.Controllers)
            byId.TryAdd(controller.Id, controller);
        return byId;
    }

    private DcsRuntime RequireRuntime() => Runtime ?? throw new InvalidOperationException("尚未装载工程");
    private ScanScheduler RequireScheduler() => Scheduler ?? throw new InvalidOperationException("尚未装载工程");

    // =================================================================
    // 运行控制
    // =================================================================

    public ScanState RunState => Scheduler?.State ?? ScanState.Stopped;

    public void Start()
    {
        using var lease = AcquireRuntimeLease();
        RequireScheduler().Start();
        Log.Info("运行", "连续运行开始");
    }

    public void Pause()
    {
        using var lease = AcquireRuntimeLease();
        RequireScheduler().Pause();
        Log.Info("运行", $"已暂停（{CycleSummary(lease.Runtime)}）");
    }

    /// <summary>完全停止：扫描线程退出（周期计数保留，可再 Start）。</summary>
    public void Stop()
    {
        using var lease = AcquireRuntimeLease();
        RequireScheduler().Stop();
        Log.Info("运行", $"已完全停止（{CycleSummary(lease.Runtime)}）");
    }

    public void Step(int cycles = 1)
    {
        using var lease = AcquireRuntimeLease();
        var scheduler = RequireScheduler();
        if (scheduler.State == ScanState.Running)
            throw new InvalidOperationException("连续运行中不能单步，请先暂停");
        scheduler.StepOnce(cycles);
        Log.Info("运行", $"单步 {cycles} 周期（{CycleSummary(lease.Runtime)}）");
    }

    public void SetCycle(string? dpuName, float seconds)
    {
        using var lease = AcquireRuntimeLease();
        var runtime = lease.Runtime;
        RequireScheduler().RunAtCycleBoundary(() =>
        {
            foreach (var dpu in runtime.Dpus)
            {
                if (dpuName == null || string.Equals(dpu.Name, dpuName, StringComparison.OrdinalIgnoreCase))
                    dpu.Cycle = seconds;
            }
        });
        Log.Info("运行", dpuName == null
            ? $"全部 DPU 扫描周期 → {seconds * 1000:F0} ms"
            : $"DPU {dpuName} 扫描周期 → {seconds * 1000:F0} ms");
    }

    private static string CycleSummary(DcsRuntime runtime)
    {
        return string.Join(" ", runtime.Dpus.Take(4).Select(d => $"{d.Name}=c{d.CycleCount}")) +
               (runtime.Dpus.Count > 4 ? " …" : "");
    }

    // =================================================================
    // 工况（condition）与快照（snapshot）
    // =================================================================

    public string SaveCondition(string name, string? comment)
    {
        using var lease = AcquireRuntimeLease();
        var runtime = lease.Runtime;
        string dir = "";
        RequireScheduler().RunAtCycleBoundary(() =>
        {
            dir = Store.SaveCondition(runtime, name, MdbPath!, Fingerprint, ProjectVersion, comment);
        });
        Log.Info("工况", $"已保存工况 [{name}]（指纹 {Fingerprint}，v{ProjectVersion}）");
        return dir;
    }

    /// <summary>
    /// 加载工况：先装工况内嵌的工程库副本（保证工程一致），再回放全量镜像。
    /// 工程演化多少代都能完整重现——这是工况与快照的本质区别。
    /// </summary>
    public void LoadCondition(string name)
    {
        var manifest = Store.ReadConditionManifest(name);
        string conditionDir = Store.ConditionDir(name);
        string embeddedMdb = Path.Combine(conditionDir, manifest.MdbFile);
        if (!File.Exists(embeddedMdb))
            throw new FileNotFoundException("工况内嵌工程库缺失", embeddedMdb);

        _structureLock.Wait();
        try
        {
            var prevState = RunState;
            var sw = Stopwatch.StartNew();

            var pristine = MdbEngineeringReader.Load(embeddedMdb);
            string fingerprint = ProjectFingerprint.Compute(pristine);
            var buildModel = pristine.Clone();
            var runtime = RuntimeBuilder.Build(buildModel, _catalog,
                new RuntimeBuildOptions { ArenaDirectory = _options.ArenaDirectory });

            // 同一 mdb 的构建是确定性的 ⇒ SchemaHash 必然一致，v1 全量镜像可直接回放
            runtime.LoadSnapshot(conditionDir);
            sw.Stop();

            SwapRuntime(runtime, buildModel, pristine, embeddedMdb, fingerprint, prevState);
            RegisterVersion(fingerprint, "condition", embeddedMdb, $"加载工况 {name}");

            Log.Info("工况", $"已加载工况 [{name}]（保存于 {manifest.SavedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}，" +
                             $"指纹 {fingerprint}，兼容迁移 {runtime.LastConditionCompatibilityMigrationCount:N0} 块，"
                             + $"{sw.ElapsedMilliseconds:N0} ms）");
        }
        finally
        {
            _structureLock.Release();
        }
    }

    public SnapshotV2Manifest SaveSnapshot(string name, string? comment)
    {
        using var lease = AcquireRuntimeLease();
        var runtime = lease.Runtime;
        SnapshotV2Manifest manifest = null!;
        var sw = Stopwatch.StartNew();
        RequireScheduler().RunAtCycleBoundary(() =>
        {
            manifest = Store.SaveSnapshot(runtime, name, Fingerprint, ProjectVersion, comment);
        });
        sw.Stop();
        int changed = manifest.Dpus.Sum(d => d.ChangedSlots);
        int total = manifest.Dpus.Sum(d => d.TotalSlots);
        Log.Info("快照", $"已保存快照 [{name}]：{changed:N0}/{total:N0} 槽有变化（{sw.ElapsedMilliseconds:N0} ms）");
        return manifest;
    }

    public SnapshotLoadReport LoadSnapshot(string name)
    {
        using var lease = AcquireRuntimeLease();
        var runtime = lease.Runtime;
        SnapshotLoadReport report = null!;
        var sw = Stopwatch.StartNew();
        RequireScheduler().RunAtCycleBoundary(() =>
        {
            report = Store.LoadSnapshot(runtime, name, Fingerprint);
        });
        sw.Stop();
        Log.Info("快照", $"已加载快照 [{name}]：{report}（{sw.ElapsedMilliseconds:N0} ms）");
        foreach (var m in report.Messages.Take(5))
            Log.Warn("快照", m);
        return report;
    }

    // =================================================================
    // 在线下装（两阶段：prepare 出差异报告 → commit 原子切换）
    // =================================================================

    /// <summary>预检：读新工程库、算指纹、与当前工程做差异，产出下装计划。</summary>
    public DownloadPlan PrepareDownload(string mdbPath)
    {
        RequireRuntime();
        if (!File.Exists(mdbPath))
            throw new FileNotFoundException("工程库不存在", mdbPath);

        var newPristine = MdbEngineeringReader.Load(mdbPath);
        string newFingerprint = ProjectFingerprint.Compute(newPristine);
        var diff = ModelDiff.Compare(PristineModel!, newPristine);
        var errors = CheckDanglingPinRefs(newPristine);

        var plan = new DownloadPlan
        {
            MdbPath = mdbPath,
            NewFingerprint = newFingerprint,
            OldFingerprint = Fingerprint,
            Diff = diff,
            PristineModel = newPristine,
            Errors = errors,
        };
        _pendingPlan = plan;

        Log.Info("下装", $"预检完成 [{plan.PlanId}]：{Path.GetFileName(mdbPath)} 指纹 {newFingerprint}" +
                         (newFingerprint == Fingerprint ? "（与当前一致）" : "") +
                         $"，差异 {diff.Entries.Count} 条" +
                         $"（点 +{diff.PointsAdded}/-{diff.PointsRemoved}/~{diff.PointsChanged}，" +
                         $"块 +{diff.BlocksAdded}/-{diff.BlocksRemoved}/类型变更 {diff.BlocksTypeChanged}，" +
                         $"接线变更 {diff.BlocksWiringChanged}，参数变更 {diff.BlocksParamChanged}）" +
                         (errors.Count > 0 ? $"；发现 {errors.Count} 个致命引用问题，commit 将被拒绝" : ""));
        foreach (var e in errors.Take(5))
            Log.Error("下装", e);
        return plan;
    }

    /// <summary>
    /// 提交下装：可选自动备份工况 → 新工程重建 → 状态按名迁移 → 周期边界原子切换。
    /// 运行中的系统在切换期间暂停一个重建窗口（秒级），随后自动恢复原运行状态。
    /// </summary>
    public DownloadResult CommitDownload(string planId, bool backupCondition = true)
    {
        var plan = _pendingPlan;
        if (plan == null || plan.PlanId != planId)
            throw new InvalidOperationException("下装计划不存在或已过期，请重新 prepare");
        if (plan.Errors.Count > 0)
            throw new InvalidOperationException(
                $"下装计划有 {plan.Errors.Count} 个致命引用问题，拒绝提交：{string.Join("；", plan.Errors.Take(3))}");

        _structureLock.Wait();
        try
        {
            var oldRuntime = RequireRuntime();
            var prevState = RunState;

            if (backupCondition)
            {
                string backupName = $"下装前备份-{DateTime.Now:yyyyMMdd-HHmmss}";
                RequireScheduler().RunAtCycleBoundary(() =>
                {
                    Store.SaveCondition(oldRuntime, backupName, MdbPath!, Fingerprint, ProjectVersion, $"下装 {plan.PlanId} 前自动备份");
                });
                Log.Info("下装", $"已自动备份当前工况 [{backupName}]");
            }

            var sw = Stopwatch.StartNew();

            // 1. 新工程重建（旧系统继续在旁边跑——重建不动旧运行时）
            var buildModel = plan.PristineModel.Clone();
            var newRuntime = RuntimeBuilder.Build(buildModel, _catalog,
                new RuntimeBuildOptions { ArenaDirectory = _options.ArenaDirectory });

            // 1.5 构建验证：新工程装配若引入了致命退化（整个 DPU 命令清零），拒绝切换。
            //     对齐成熟 DCS 的 verify-then-download——验证不过，旧运行时原样继续跑。
            var fatal = ValidateBuildForDownload(oldRuntime, newRuntime);
            if (fatal.Count > 0)
            {
                newRuntime.Dispose();
                string detail = string.Join("；", fatal.Take(5));
                Log.Error("下装", $"下装被阻断 [{plan.PlanId}]：新工程装配验证失败——{detail}");
                throw new InvalidOperationException($"下装验证失败，已保持当前工程继续运行：{detail}");
            }

            // 2. 周期边界：停旧 → 状态迁移 → 切换
            DownloadResult result = null!;
            RequireScheduler().RunAtCycleBoundary(() =>
            {
                result = OnlineDownloader.Transfer(oldRuntime, newRuntime);
            });

            SwapRuntime(newRuntime, buildModel, plan.PristineModel, plan.MdbPath, plan.NewFingerprint, prevState);
            RegisterVersion(plan.NewFingerprint, "download", plan.MdbPath, $"在线下装 {plan.PlanId}");
            sw.Stop();

            _pendingPlan = null;

            Log.Info("下装", $"下装完成 [{plan.PlanId}] v{ProjectVersion} 指纹 {Fingerprint}：" +
                             $"点保留 {result.PointsPreserved:N0}（新增 {result.PointsNew:N0}/删除 {result.PointsDropped:N0}/类型变更 {result.PointsTypeChanged}），" +
                             $"块保留 {result.BlocksPreserved:N0}（新增 {result.BlocksNew}/删除 {result.BlocksDropped}/类型变更 {result.BlocksTypeChanged}），" +
                             $"字段转移 {result.FieldsTransferred:N0}，强制携带 {result.ForcesCarried}，" +
                             $"总耗时 {sw.ElapsedMilliseconds:N0} ms（迁移 {result.TransferMs:F0} ms）" +
                             (prevState switch
                             {
                                 ScanState.Running => "，已自动恢复运行",
                                 ScanState.Paused => "，已恢复暂停态（可单步）",
                                 _ => "",
                             }));
            foreach (var m in result.Messages.Take(5))
                Log.Warn("下装", m);
            return result;
        }
        finally
        {
            _structureLock.Release();
        }
    }

    public DownloadPlan? PendingPlan => _pendingPlan;

    /// <summary>
    /// 静态预检：pin-point 引用（"块名.管脚名"式 PointName）的源块必须存在。
    /// 悬空引用会让 RuntimeBuilder 判整个 DPU 装配失败（对齐老系统抛异常语义），
    /// 因此在 prepare 阶段就把它暴露出来，避免 commit 到一半才发现。
    /// </summary>
    private static List<string> CheckDanglingPinRefs(EngineeringModel model)
    {
        var errors = new List<string>();
        foreach (var c in model.Controllers)
        {
            var blockNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var b in c.Blocks)
                blockNames.Add(b.Name);

            foreach (var b in c.Blocks)
            {
                if (b.FcName == "APSM")
                    continue;
                foreach (var pin in b.Pins)
                {
                    string pn = pin.PointName ?? "";
                    if (pn.Length == 0 || !LegacySemantics.IsPinPointName(pn))
                        continue;
                    int dot = pn.LastIndexOf('.');
                    if (dot <= 0)
                        continue;
                    string src = pn[..dot];
                    if (!blockNames.Contains(src))
                        errors.Add($"[{c.Name}] 块 {b.Name} 管脚 {pin.PinName} 引用了不存在的源块 {src}（PointName={pn}）");
                }
            }
        }
        return errors;
    }

    /// <summary>
    /// 下装构建验证：找出会导致运行能力灾难性退化的装配问题。
    /// 目前规则：旧 DPU 有命令而新 DPU 命令清零（多由悬空 pin-point 引用导致整 DPU 装配失败）。
    /// 返回致命问题列表（空 = 通过）。
    /// </summary>
    private static List<string> ValidateBuildForDownload(DcsRuntime oldRuntime, DcsRuntime newRuntime)
    {
        var fatal = new List<string>();
        foreach (var oldDpu in oldRuntime.Dpus)
        {
            if (oldDpu.Commands.Count == 0)
                continue; // 旧的本来就空，不算退化
            var newDpu = newRuntime.FindDpu(oldDpu.Name);
            if (newDpu == null)
                continue; // 控制器整体删除是合法工程变更（diff 已见 ControllerRemoved）
            if (newDpu.Commands.Count == 0)
            {
                string reason = newRuntime.Report.Errors
                    .FirstOrDefault(e => e.Contains($"[{oldDpu.Name}]")) ?? "原因未知";
                fatal.Add($"{oldDpu.Name} 命令装配清零（旧 {oldDpu.Commands.Count} 块）：{reason}");
            }
        }
        return fatal;
    }

    // =================================================================
    // 热更（现调现改功能块）
    // =================================================================

    /// <summary>按功能码名或源文件热更换代（Roslyn 编译 → 周期边界原子替换）。</summary>
    public HotSwapReport HotLoad(string[] fcNamesOrFiles)
    {
        string blocksSrc = _options.BlocksSourceDir ?? throw new InvalidOperationException("未配置热更源码目录");

        var files = new List<string>();
        foreach (string a in fcNamesOrFiles)
        {
            if (a.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                files.Add(Path.GetFullPath(a));
                continue;
            }
            string decl = Path.Combine(blocksSrc, $"FC_{a}.cs");
            string run = Path.Combine(blocksSrc, $"FC_{a}_RUN.cs");
            if (File.Exists(decl)) files.Add(decl);
            if (File.Exists(run)) files.Add(run);
            if (!File.Exists(decl) && !File.Exists(run))
                throw new FileNotFoundException($"块源码目录中找不到 FC_{a}.cs / FC_{a}_RUN.cs（{blocksSrc}）");
        }

        var sources = files.Select(f => new Hosting.KernelSource(f, File.ReadAllText(f))).ToList();
        var extraRefs = new[]
        {
            Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(_options.BlocksAssembly.Location),
            Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(BlockHotSwapper).Assembly.Location),
            Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(EngineeringModel).Assembly.Location),
        };
        var compile = Hosting.KernelCompiler.Compile(
            $"blocks-hot-{DateTime.Now:HHmmss}", sources, debug: true, extraReferences: extraRefs);
        if (!compile.Success)
        {
            string errors = string.Join("\n", compile.Errors.Take(10));
            Log.Error("热更", $"编译失败：\n{errors}");
            throw new InvalidOperationException($"编译失败：{compile.Errors.FirstOrDefault()}");
        }

        using var lease = AcquireRuntimeLease();
        var swapper = Swapper ?? throw new InvalidOperationException("尚未装载工程");
        HotSwapReport report = null!;
        RequireScheduler().RunAtCycleBoundary(() => report = swapper.Apply(compile.AssemblyImage!, compile.PdbImage));

        foreach (var m in report.Messages)
            Log.Warn("热更", m);
        if (report.Success)
            Log.Info("热更", $"第 {report.Generation} 代生效：[{string.Join(", ", report.SwappedFcNames)}] " +
                             $"替换 {report.CommandsSwapped} 块，转移 {report.FieldsTransferred:N0} 字段");
        return report;
    }

    // =================================================================
    // 版本档案（versions.json）
    // =================================================================

    public IReadOnlyList<VersionEntry> Versions => _versions;

    private string VersionsFile => Path.Combine(_options.DataDirectory, "versions.json");

    private void LoadVersionRegistry()
    {
        if (!File.Exists(VersionsFile))
            return;
        try
        {
            var loaded = JsonSerializer.Deserialize<List<VersionEntry>>(File.ReadAllText(VersionsFile));
            if (loaded != null)
                _versions.AddRange(loaded);
            ProjectVersion = _versions.Count > 0 ? _versions[^1].Version : 0;
        }
        catch (Exception ex)
        {
            Log.Warn("版本", $"版本档案读取失败（忽略）：{ex.Message}");
        }
    }

    private void RegisterVersion(string fingerprint, string source, string mdbPath, string? comment)
    {
        // 指纹未变时不增版本（重复装载同一工程）
        if (_versions.Count > 0 && _versions[^1].Fingerprint == fingerprint)
        {
            ProjectVersion = _versions[^1].Version;
            return;
        }

        var entry = new VersionEntry
        {
            Version = (_versions.Count > 0 ? _versions[^1].Version : 0) + 1,
            Fingerprint = fingerprint,
            Source = source,
            MdbPath = mdbPath,
            TimeUtc = DateTime.UtcNow,
            Comment = comment,
        };
        _versions.Add(entry);
        ProjectVersion = entry.Version;

        try
        {
            File.WriteAllText(VersionsFile, JsonSerializer.Serialize(_versions, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Log.Warn("版本", $"版本档案写入失败：{ex.Message}");
        }
    }

    public void Dispose()
    {
        lock (_runtimeLeaseGate)
        {
            if (_disposed)
                return;
        }

        RuntimeChanging?.Invoke();
        EnterRuntimeRetirement();
        try
        {
            if (_disposed)
                return;

            _disposed = true;
            Scheduler?.Stop();
            History?.Dispose();
            Runtime?.Dispose();
            Runtime = null;
            Scheduler = null;
            History = null;
            Xref = null;
            Swapper = null;
        }
        finally
        {
            ExitRuntimeRetirement();
        }
        RuntimeSwapped?.Invoke();
    }
}
