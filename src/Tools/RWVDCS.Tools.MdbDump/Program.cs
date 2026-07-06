// MdbDump：Access 工程库探查工具。
// 用法：
//   MdbDump <mdb路径> tables                     列出所有表 + 行数
//   MdbDump <mdb路径> schema <表名>              列出表结构（列名/类型）
//   MdbDump <mdb路径> sample <表名> [行数]        采样若干行
//   MdbDump <mdb路径> sql "<select语句>" [行数]   任意查询
//   MdbDump <mdb路径> exec "<非查询语句>"         执行 INSERT/UPDATE/DELETE（测试库制备用）
//   MdbDump <mdb路径> profile                    工程画像（控制器/点/块分布）
using System.Data;
using System.Data.OleDb;
using System.Text;

if (args.Length < 2)
{
    Console.Error.WriteLine("用法: MdbDump <mdb路径> tables|schema|sample|sql|exec|profile [参数]");
    return 1;
}

Console.OutputEncoding = Encoding.UTF8;
string mdbPath = args[0];
string verb = args[1].ToLowerInvariant();

string mode = verb == "exec" ? "Share Deny None" : "Read";
string connStr = $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={mdbPath};Persist Security Info=False;Mode={mode};";
using var conn = new OleDbConnection(connStr);
conn.Open();

switch (verb)
{
    case "tables":
    {
        var tables = ListTables(conn);
        foreach (var t in tables)
        {
            long count = -1;
            try
            {
                using var cmd = new OleDbCommand($"SELECT COUNT(*) FROM [{t}]", conn);
                count = Convert.ToInt64(cmd.ExecuteScalar());
            }
            catch { /* 忽略无法计数的表 */ }
            Console.WriteLine($"{t}\t{count}");
        }
        break;
    }
    case "schema":
    {
        string table = args[2];
        var schema = conn.GetOleDbSchemaTable(OleDbSchemaGuid.Columns, new object?[] { null, null, table, null })!;
        var rows = schema.Rows.Cast<DataRow>().OrderBy(r => (long)Convert.ToInt64(r["ORDINAL_POSITION"]));
        foreach (DataRow r in rows)
        {
            var oleType = (OleDbType)Convert.ToInt32(r["DATA_TYPE"]);
            Console.WriteLine($"{r["ORDINAL_POSITION"],3}  {r["COLUMN_NAME"],-30} {oleType}");
        }
        break;
    }
    case "sample":
    {
        string table = args[2];
        int n = args.Length > 3 ? int.Parse(args[3]) : 5;
        DumpQuery(conn, $"SELECT * FROM [{table}]", n);
        break;
    }
    case "sql":
    {
        string sql = args[2];
        int n = args.Length > 3 ? int.Parse(args[3]) : 50;
        DumpQuery(conn, sql, n);
        break;
    }
    case "exec":
    {
        using var cmd = new OleDbCommand(args[2], conn);
        int affected = cmd.ExecuteNonQuery();
        Console.WriteLine($"OK，影响 {affected} 行");
        break;
    }
    case "profile":
    {
        Profile(conn);
        break;
    }
    default:
        Console.Error.WriteLine($"未知命令: {verb}");
        return 1;
}

return 0;

static List<string> ListTables(OleDbConnection conn)
{
    var schema = conn.GetOleDbSchemaTable(OleDbSchemaGuid.Tables, new object?[] { null, null, null, "TABLE" })!;
    return schema.Rows.Cast<DataRow>()
        .Select(r => (string)r["TABLE_NAME"])
        .Where(t => !t.StartsWith("MSys", StringComparison.OrdinalIgnoreCase))
        .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
        .ToList();
}

static void DumpQuery(OleDbConnection conn, string sql, int maxRows)
{
    using var cmd = new OleDbCommand(sql, conn);
    using var reader = cmd.ExecuteReader();
    var names = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
    Console.WriteLine(string.Join(" | ", names));
    int count = 0;
    while (reader.Read() && count < maxRows)
    {
        var vals = new string[reader.FieldCount];
        for (int i = 0; i < reader.FieldCount; i++)
        {
            object v = reader.GetValue(i);
            vals[i] = v is DBNull ? "<null>" : v.ToString() ?? "";
        }
        Console.WriteLine(string.Join(" | ", vals));
        count++;
    }
}

static void Profile(OleDbConnection conn)
{
    Console.WriteLine("== 表行数（>0 的表）==");
    foreach (var t in ListTables(conn))
    {
        try
        {
            using var cmd = new OleDbCommand($"SELECT COUNT(*) FROM [{t}]", conn);
            long c = Convert.ToInt64(cmd.ExecuteScalar());
            if (c > 0) Console.WriteLine($"{t}\t{c}");
        }
        catch { }
    }

    TryDump(conn, "\n== 控制器（Prj_Controller）==", "SELECT * FROM Prj_Controller", 50);
    TryDump(conn, "\n== 块型分布（Cld_FCBlock 按 FunctionName）==",
        "SELECT FunctionName, COUNT(*) AS N FROM Cld_FCBlock GROUP BY FunctionName ORDER BY COUNT(*) DESC", 300);
    TryDump(conn, "\n== 点类型分布（Cfg_VarSystem 按 DataType）==",
        "SELECT DataType, COUNT(*) AS N FROM Cfg_VarSystem GROUP BY DataType", 50);
}

static void TryDump(OleDbConnection conn, string title, string sql, int maxRows)
{
    Console.WriteLine(title);
    try { DumpQuery(conn, sql, maxRows); }
    catch (Exception ex) { Console.WriteLine($"(查询失败: {ex.Message})"); }
}
