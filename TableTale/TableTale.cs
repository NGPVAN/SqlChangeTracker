using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Newtonsoft.Json.Linq;

namespace TableTale
{
  public class TableTale
  {
    readonly SqlConnectionStringBuilder _connectionString;
    private readonly string _table;
    private readonly string _sql;
    private CancellationToken _token;
    private readonly Task _task;
    private readonly Action<RowChange> _onChange;
    private readonly string _schema;
    private readonly TimeSpan _interval;

    public TableTale(string connectionString, string table, Action<RowChange> onChange, CancellationToken token, string schema = "dbo", int intervalInSeconds = 1)
    {
      _connectionString = new SqlConnectionStringBuilder(connectionString);
      _interval = TimeSpan.FromSeconds(intervalInSeconds);
      _schema = schema;
      _table = table;
      _token = token;
      _onChange = onChange;
      _sql = GetChangedRowSql(_table, _schema);
      Action a = DoWork;
      _task = Task.Run(a, _token);

      EnableChangeTracking();
      CreateTableTaleTable();
    }

    public TaskStatus Status
    {
      get { return _task.Status; }
    }

    private void DoWork()
    {
      while (!_token.IsCancellationRequested)
      {
        long version = GetVersion();
        var changes = GetChangesFromVersion(version).ToList();
        bool versionChanged = false;
        foreach (var change in changes)
        {
          if (change.SYS_CHANGE_VERSION > version)
          {
            version = change.SYS_CHANGE_VERSION;
            versionChanged = true;
          }
          try
          {
            _onChange(change);
          }
          catch (Exception ex)
          {
            Trace.WriteLine(ex.ToString());
          }
        }

        if (versionChanged)
        {
          SetVersion(version);
        }

        Thread.Sleep(_interval);
      }
    }

    private long GetVersion()
    {
      using (var c = new SqlConnection(_connectionString.ConnectionString))
      {
        return c.Query<long>("select [Version] from TableTale where [table] = @Table", new { Table = _table }).First();
      }
    }

    private void SetVersion(long version)
    {
      using (var c = new SqlConnection(_connectionString.ConnectionString))
      {
        c.Execute("update TableTale set [version] = @Version, [lastRun] = getdate() where [table] = @Table", new { Version = version, Table = _table });
      }
    }

    private void EnableChangeTracking()
    {
      using (var c = new SqlConnection(_connectionString.ConnectionString))
      {
        string enableChangeTrackingSql =
          string.Format(
            "IF NOT EXISTS(SELECT 1 FROM sys.change_tracking_databases ctd join sys.databases d on ctd.database_id = d.database_id WHERE d.name = '{0}') ALTER DATABASE [{0}] SET CHANGE_TRACKING = ON (CHANGE_RETENTION = 1 DAYS, AUTO_CLEANUP = ON); " +
            "IF NOT EXISTS(SELECT 1 FROM [{0}].sys.change_tracking_tables WHERE object_id=OBJECT_ID('{1}')) ALTER TABLE [{0}].[{2}].[{1}] ENABLE CHANGE_TRACKING WITH (TRACK_COLUMNS_UPDATED = OFF);", _connectionString.InitialCatalog, _table, _schema);
        c.Execute(enableChangeTrackingSql);
      }
    }

    private void CreateTableTaleTable()
    {
      using (var c = new SqlConnection(_connectionString.ConnectionString))
      {

        const string tableTaleCreateSql = "if not exists (select 1 from sys.tables where name = 'TableTale') create table TableTale ([table] varchar(128) primary key, [version] bigint, [lastRun] datetime);";
        const string rowForTableSql = "if not exists (select 1 from TableTale where [Table] = @t) insert into [TableTale] values (@t, 0, getdate());";

        c.Execute(tableTaleCreateSql);
        c.Execute(rowForTableSql, new { t = _table });
      }
    }

    private IEnumerable<RowChange> GetChangesFromVersion(long version)
    {
      var changes = new List<RowChange>();
      using (var c = new SqlConnection(_connectionString.ConnectionString))
      {
        changes.AddRange(
          c.Query<object>(_sql, new {Version = version})
            .Select(JObject.FromObject)
            .Select(jRow => new RowChange
            {
              SYS_CHANGE_VERSION = jRow["SYS_CHANGE_VERSION"].Value<long>(),
              SYS_CHANGE_OPERATION = jRow["SYS_CHANGE_OPERATION"].ToString(),
              Table = _table,
              Database = c.Database,
              Row = jRow
            }));
      }
      return changes;
    }

    private string GetChangedRowSql(string table, string schema)
    {
      using (var c = new SqlConnection(_connectionString.ConnectionString))
      {
        string sql = "select ct.SYS_CHANGE_VERSION, ct.SYS_CHANGE_OPERATION, t.* from CHANGETABLE(CHANGES " + table + ", @Version) ct join [" + schema + "].[" + table + "] (nolock) t on ";
        string[] keys = c.Query<Column>("sp_pkeys " + table).ToList().Select(r => string.Format("t.[{0}] = ct.[{0}]", r.COLUMN_NAME)).ToArray();
        sql += string.Join(" and ", keys);
        return sql;
      }
    }

    struct Column
    {
      public string COLUMN_NAME;
    }
  }
}
