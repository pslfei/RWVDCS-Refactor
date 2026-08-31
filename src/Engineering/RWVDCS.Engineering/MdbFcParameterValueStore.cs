using System.Data;
using System.Data.OleDb;

namespace RWVDCS.Engineering;

/// <summary>MDB 已提交后回读验证失败，但补偿事务已恢复旧值。</summary>
public sealed class FcPinPersistenceException(string message, Exception innerException)
    : IOException(message, innerException);

/// <summary>一笔尚未提交的功能码管脚数据库更新。</summary>
public interface IFcPinValueUpdate : IDisposable
{
    int RecordId { get; }
    string OldValue { get; }
    string? PointName { get; }
    string MdbPath { get; }
    string DatabaseTable { get; }
    string DatabaseColumn { get; }
    string RequestedValue { get; }
    string? PersistedValue { get; }
    bool DatabaseVerified { get; }
    bool CommitSucceeded { get; }
    bool DatabaseRestored { get; }
    void Commit();
}

/// <summary>
/// 功能块实例管脚值的持久化边界。Constant 写 Cld_FCParameter.PValue，
/// 未连接测点的 Input 写 Cld_FCInput.InitialValue。
/// </summary>
public interface IFcPinValueStore
{
    IFcPinValueUpdate BeginConstantUpdate(
        string mdbPath,
        int cldFcBlockId,
        string pinName,
        string newValue);

    IFcPinValueUpdate BeginInputUpdate(
        string mdbPath,
        int cldFcBlockId,
        string pinName,
        string newValue);
}

/// <summary>Access MDB 中功能码 Constant/Input 管脚值的参数化事务更新实现。</summary>
public sealed class MdbFcPinValueStore : IFcPinValueStore
{
    public IFcPinValueUpdate BeginConstantUpdate(
        string mdbPath,
        int cldFcBlockId,
        string pinName,
        string newValue)
        => BeginUpdate(
            mdbPath,
            cldFcBlockId,
            pinName,
            newValue,
            tableName: "Cld_FCParameter",
            pinNameColumn: "[Name]",
            valueColumn: "PValue",
            readPointName: false);

    public IFcPinValueUpdate BeginInputUpdate(
        string mdbPath,
        int cldFcBlockId,
        string pinName,
        string newValue)
        => BeginUpdate(
            mdbPath,
            cldFcBlockId,
            pinName,
            newValue,
            tableName: "Cld_FCInput",
            pinNameColumn: "PinName",
            valueColumn: "InitialValue",
            readPointName: true);

    private static IFcPinValueUpdate BeginUpdate(
        string mdbPath,
        int cldFcBlockId,
        string pinName,
        string newValue,
        string tableName,
        string pinNameColumn,
        string valueColumn,
        bool readPointName)
    {
        mdbPath = Path.GetFullPath(mdbPath);
        if (!File.Exists(mdbPath))
            throw new FileNotFoundException("当前工程库不存在", mdbPath);

        var connection = new OleDbConnection(
            $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={mdbPath};"
            + "Persist Security Info=False;Mode=Share Deny None;");
        connection.Open();
        OleDbTransaction transaction = connection.BeginTransaction();
        try
        {
            int recordId;
            string oldValue;
            object oldDatabaseValue;
            string? pointName = null;
            string selectColumns = readPointName
                ? $"ID, [{valueColumn}], PointName"
                : $"ID, [{valueColumn}]";
            using (var select = new OleDbCommand(
                       $"SELECT {selectColumns} FROM [{tableName}] "
                       + $"WHERE Cld_FCBlock_ID = ? AND {pinNameColumn} = ?",
                       connection,
                       transaction))
            {
                select.Parameters.Add("?", OleDbType.Integer).Value = cldFcBlockId;
                select.Parameters.Add("?", OleDbType.VarWChar, 255).Value = pinName;
                using OleDbDataReader reader = select.ExecuteReader();
                if (!reader.Read())
                    throw new KeyNotFoundException(
                        $"Cld_FCBlock_ID={cldFcBlockId} 在 {tableName} 中不存在管脚 {pinName}");

                recordId = reader.GetInt32(0);
                oldDatabaseValue = reader.IsDBNull(1) ? DBNull.Value : reader.GetValue(1);
                oldValue = reader.IsDBNull(1)
                    ? string.Empty
                    : Convert.ToString(reader.GetValue(1)) ?? string.Empty;
                if (readPointName)
                {
                    pointName = reader.IsDBNull(2)
                        ? string.Empty
                        : Convert.ToString(reader.GetValue(2)) ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(pointName))
                        throw new InvalidOperationException(
                            $"输入管脚 {pinName} 已连接测点 {pointName}，不能修改 InitialValue");
                }
                if (reader.Read())
                    throw new InvalidDataException(
                        $"Cld_FCBlock_ID={cldFcBlockId} 在 {tableName} 中存在重复管脚 {pinName}");
            }

            using (var update = new OleDbCommand(
                       $"UPDATE [{tableName}] SET [{valueColumn}] = ? WHERE ID = ?",
                       connection,
                       transaction))
            {
                update.Parameters.Add("?", OleDbType.VarWChar, 255).Value = newValue;
                update.Parameters.Add("?", OleDbType.Integer).Value = recordId;
                if (update.ExecuteNonQuery() != 1)
                    throw new DBConcurrencyException($"{tableName} 记录 {recordId} 更新行数不是1");
            }

            return new OleDbFcPinValueUpdate(
                connection,
                transaction,
                recordId,
                oldValue,
                oldDatabaseValue,
                pointName,
                mdbPath,
                tableName,
                valueColumn,
                newValue);
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            transaction.Dispose();
            connection.Dispose();
            throw;
        }
    }

    private sealed class OleDbFcPinValueUpdate(
        OleDbConnection connection,
        OleDbTransaction transaction,
        int recordId,
        string oldValue,
        object oldDatabaseValue,
        string? pointName,
        string mdbPath,
        string databaseTable,
        string databaseColumn,
        string requestedValue) : IFcPinValueUpdate
    {
        private bool _committed;
        private bool _disposed;
        private bool _resourcesDisposed;

        public int RecordId { get; } = recordId;
        public string OldValue { get; } = oldValue;
        public string? PointName { get; } = pointName;
        public string MdbPath { get; } = mdbPath;
        public string DatabaseTable { get; } = databaseTable;
        public string DatabaseColumn { get; } = databaseColumn;
        public string RequestedValue { get; } = requestedValue;
        public string? PersistedValue { get; private set; }
        public bool DatabaseVerified { get; private set; }
        public bool CommitSucceeded { get; private set; }
        public bool DatabaseRestored { get; private set; }

        public void Commit()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_committed)
                return;
            transaction.Commit();
            CommitSucceeded = true;
            _committed = true;

            // 先关闭执行 UPDATE 的连接，再以全新连接回读，避免同连接缓存让验证失真。
            DisposeResources();

            Exception? verificationError = null;
            try
            {
                PersistedValue = ReadPersistedValue(
                    MdbPath,
                    DatabaseTable,
                    DatabaseColumn,
                    RecordId);
                DatabaseVerified = string.Equals(
                    PersistedValue,
                    RequestedValue,
                    StringComparison.Ordinal);
                if (!DatabaseVerified)
                {
                    verificationError = new DBConcurrencyException(
                        $"MDB 提交后回读不一致：期望 {RequestedValue}，实际 {PersistedValue}");
                }
            }
            catch (Exception ex)
            {
                verificationError = ex;
            }

            if (verificationError == null)
                return;

            // 数据库已经 Commit；验证失败时做补偿事务，恢复原数据库值，
            // 使调用方随后回滚 Runtime/模型时仍保持两边一致。
            try
            {
                RestoreDatabaseValue(
                    MdbPath,
                    DatabaseTable,
                    DatabaseColumn,
                    RecordId,
                    oldDatabaseValue);
                string restoredValue = ReadPersistedValue(
                    MdbPath,
                    DatabaseTable,
                    DatabaseColumn,
                    RecordId);
                if (!string.Equals(restoredValue, OldValue, StringComparison.Ordinal))
                {
                    throw new DBConcurrencyException(
                        $"MDB 补偿回读不一致：期望恢复 {OldValue}，实际 {restoredValue}");
                }

                PersistedValue = restoredValue;
                DatabaseRestored = true;
            }
            catch (Exception restoreError)
            {
                throw new AggregateException(
                    $"MDB 已提交，但回读验证失败且旧值补偿失败：{MdbPath}，"
                    + $"{DatabaseTable}.{DatabaseColumn}，ID={RecordId}",
                    verificationError,
                    restoreError);
            }

            throw new FcPinPersistenceException(
                $"MDB 提交后回读验证失败，已恢复数据库旧值：{MdbPath}，"
                + $"{DatabaseTable}.{DatabaseColumn}，ID={RecordId}",
                verificationError);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (!_committed)
            {
                try { transaction.Rollback(); } catch { }
            }
            DisposeResources();
        }

        private void DisposeResources()
        {
            if (_resourcesDisposed)
                return;
            _resourcesDisposed = true;
            transaction.Dispose();
            connection.Dispose();
        }
    }

    private static string ReadPersistedValue(
        string mdbPath,
        string tableName,
        string valueColumn,
        int recordId)
    {
        using var connection = new OleDbConnection(
            $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={mdbPath};"
            + "Persist Security Info=False;Mode=Read;");
        connection.Open();
        using var command = new OleDbCommand(
            $"SELECT [{valueColumn}] FROM [{tableName}] WHERE ID = ?",
            connection);
        command.Parameters.Add("?", OleDbType.Integer).Value = recordId;
        object? value = command.ExecuteScalar();
        if (value == null)
            throw new DBConcurrencyException($"{tableName} 记录 {recordId} 在提交后不存在");
        return value is DBNull ? string.Empty : Convert.ToString(value) ?? string.Empty;
    }

    private static void RestoreDatabaseValue(
        string mdbPath,
        string tableName,
        string valueColumn,
        int recordId,
        object oldDatabaseValue)
    {
        using var connection = new OleDbConnection(
            $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={mdbPath};"
            + "Persist Security Info=False;Mode=Share Deny None;");
        connection.Open();
        using OleDbTransaction transaction = connection.BeginTransaction();
        try
        {
            using var command = new OleDbCommand(
                $"UPDATE [{tableName}] SET [{valueColumn}] = ? WHERE ID = ?",
                connection,
                transaction);
            command.Parameters.Add("?", OleDbType.VarWChar, 255).Value = oldDatabaseValue;
            command.Parameters.Add("?", OleDbType.Integer).Value = recordId;
            if (command.ExecuteNonQuery() != 1)
                throw new DBConcurrencyException($"{tableName} 记录 {recordId} 补偿更新行数不是1");
            transaction.Commit();
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            throw;
        }
    }
}
