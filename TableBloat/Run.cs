#:package Microsoft.Data.SqlClient@5.2.2
/*
    Run.cs -- Bulk-insert table-bloat reproducer (driver).

    Requires the .NET 10 SDK. Run it with:

        dotnet run Run.cs                 # localhost, 50000 rows/scenario
        dotnet run Run.cs -- --rows 20000
        dotnet run Run.cs -- --server .\SQLEXPRESS

    Recreates the TableBloatTest database (setup.sql), then loads a fixed
    number of rows into dbo.BloatTest via SqlBulkCopy three ways, printing
    reserved vs. used space (from sys.dm_db_partition_stats) after each:

        1. BatchSize = 1            -> the bloat (each batch = its own INSERT BULK,
                                      grabbing fresh 64-KB extents it never fills)
        2. BatchSize = 1000        -> remediation A: a sane batch size
        3. BatchSize = 1 + TF 692  -> remediation B: disable fast inserts

    Single-threaded. SIMPLE recovery. Scenario 3 needs sysadmin (DBCC TRACEON).
*/

using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Data.SqlClient;

string server   = Arg("--server") ?? "localhost";
const string db = "TableBloatTest";
long rowCount   = long.TryParse(Arg("--rows"), NumberStyles.Any, CultureInfo.InvariantCulture, out var n) ? n : 50_000;

string dir      = ScriptDir();
string masterCs = $"Data Source={server};Initial Catalog=master;Integrated Security=True;Encrypt=False;TrustServerCertificate=True";
string dbCs     = $"Data Source={server};Initial Catalog={db};Integrated Security=True;Encrypt=False;TrustServerCertificate=True";

Console.WriteLine("=== Bulk-insert table-bloat reproducer ===");
Console.WriteLine($"Server={server}  DB={db}  Rows/scenario={rowCount:N0}\n");

Console.WriteLine("Running setup.sql ...");
RunSqlFile(masterCs, Path.Combine(dir, "setup.sql"));

var scenarios = new (string Name, int Batch, bool Tf692)[]
{
    ("SqlBulkCopy  batch=1            (small inserts)", 1,    false),
    ("SqlBulkCopy  batch=1000         (remediation)", 1000, false),
    ("SqlBulkCopy  batch=1 + TF 692   (remediation)", 1,    true),
};

foreach (var s in scenarios)
{
    Console.WriteLine($"\n--- {s.Name}");
    Exec(dbCs, "TRUNCATE TABLE dbo.BloatTest;");

    var sw = Stopwatch.StartNew();
    BulkLoad(dbCs, s.Batch, rowCount, s.Tf692);
    sw.Stop();

    var (reservedMb, usedMb) = MeasureSpace(dbCs);
    Console.WriteLine($"    reserved = {reservedMb,8:N1} MB     used = {usedMb,7:N1} MB     ({sw.Elapsed.TotalSeconds:N0}s)");
}


// ---------------------------------------------------------------------------

void BulkLoad(string cs, int batchSize, long rows, bool tf692)
{
    using var cn = new SqlConnection(cs);
    cn.Open();

    if (tf692)
    {
        using var t = cn.CreateCommand();
        t.CommandText = "DBCC TRACEON (692) WITH NO_INFOMSGS";   // session-scoped; clears on close
        t.ExecuteNonQuery();
    }

    var dt = new DataTable();
    dt.Columns.Add("CreatedOn", typeof(DateTime));
    dt.Columns.Add("Amount",    typeof(decimal));
    dt.Columns.Add("Quantity",  typeof(int));
    dt.Columns.Add("Status",    typeof(byte));
    dt.Columns.Add("RefGuid",   typeof(Guid));
    dt.Columns.Add("Payload",   typeof(string));

    var guid = Guid.NewGuid();
    string payload = new string('X', 350);
    for (long i = 0; i < rows; i++)
        dt.Rows.Add(DateTime.Now, 12345.67m, 42, (byte)7, guid, payload);

    using var bc = new SqlBulkCopy(cn)
    {
        DestinationTableName = "dbo.BloatTest",
        BatchSize            = batchSize,
        BulkCopyTimeout      = 0,
    };
    foreach (var col in new[] { "CreatedOn", "Amount", "Quantity", "Status", "RefGuid", "Payload" })
        bc.ColumnMappings.Add(col, col);

    bc.WriteToServer(dt);
}

(decimal reservedMb, decimal usedMb) MeasureSpace(string cs)
{
    using var cn = new SqlConnection(cs);
    cn.Open();
    using (var c = cn.CreateCommand())
    {
        c.CommandText = "CHECKPOINT; DBCC UPDATEUSAGE (0, 'dbo.BloatTest') WITH NO_INFOMSGS;";
        c.CommandTimeout = 0;
        c.ExecuteNonQuery();
    }
    using var q = cn.CreateCommand();
    q.CommandText = @"
        SELECT SUM(reserved_page_count) * 8 / 1024.0 AS ReservedMB,
               SUM(used_page_count)     * 8 / 1024.0 AS UsedMB
        FROM sys.dm_db_partition_stats
        WHERE object_id = OBJECT_ID('dbo.BloatTest') AND index_id IN (0, 1);";
    using var rd = q.ExecuteReader();
    rd.Read();
    return ((decimal)rd["ReservedMB"], (decimal)rd["UsedMB"]);
}

void Exec(string cs, string sql)
{
    using var cn = new SqlConnection(cs);
    cn.Open();
    using var c = cn.CreateCommand();
    c.CommandText = sql;
    c.CommandTimeout = 0;
    c.ExecuteNonQuery();
}

void RunSqlFile(string cs, string path)
{
    // one persistent connection so USE <db> and later batches share context
    using var cn = new SqlConnection(cs);
    cn.Open();
    var text = File.ReadAllText(path);
    foreach (var batch in System.Text.RegularExpressions.Regex.Split(text, @"(?im)^\s*GO\s*$"))
    {
        if (string.IsNullOrWhiteSpace(batch)) continue;
        using var c = cn.CreateCommand();
        c.CommandText = batch;
        c.CommandTimeout = 0;
        c.ExecuteNonQuery();
    }
}

string? Arg(string name)
{
    var a = Environment.GetCommandLineArgs();
    for (int i = 0; i < a.Length - 1; i++)
        if (a[i] == name) return a[i + 1];
    return null;
}

static string ScriptDir([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
