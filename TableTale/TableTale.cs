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
    private CancellationToken _token;
    private readonly Task _task;
    private readonly Action<RowChange> _onChange;
    private readonly TimeSpan _interval;

    public TableTale(Action<RowChange> onChange, CancellationToken token)
    {
      _interval = TimeSpan.Parse(System.Configuration.ConfigurationManager.AppSettings["PollInterval"]);
      _token = token;
      _onChange = onChange;

      Action a = DoWork;
      _task = Task.Run(a, _token);

      using(var m = new TestEntities())
      {
          foreach (var tt in m.TrackedTables) {
              EnableChangeTracking(tt);
          }
      }
    }

    public TaskStatus Status
    {
      get { return _task.Status; }
    }

    private void DoWork()
    {
      while (!_token.IsCancellationRequested)
      {
        using (var m = new TestEntities())
        {
            foreach (var tt in m.TrackedTables.ToList())
            {
                var changes = GetChangesFromVersion(tt).ToList();

                foreach (var change in changes)
                {
                    if (change.SYS_CHANGE_VERSION > (tt.Version ?? 0))
                    {
                        tt.Version = change.SYS_CHANGE_VERSION;
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


                tt.LastRun = DateTime.Now;
                Trace.WriteLine(string.Format("{0}: {1} changes", GetFullName(tt), changes.Count()));
                m.SaveChanges();
            }
        }

        _token.WaitHandle.WaitOne(_interval);
      }
    }

    private static void EnableChangeTracking(TrackedTable table)
    {
        using (var c = new SqlConnection(table.ConnectionString))
      {
        string enableChangeTrackingSql =
          string.Format(
            "IF NOT EXISTS(SELECT 1 FROM master.sys.change_tracking_databases ctd join master.sys.databases d on ctd.database_id = d.database_id WHERE d.name = '{0}') ALTER DATABASE [{0}] SET CHANGE_TRACKING = ON (CHANGE_RETENTION = 1 DAYS, AUTO_CLEANUP = ON); " +
            "IF NOT EXISTS(SELECT 1 from [{0}].sys.change_tracking_tables ctt join [{0}].sys.objects o on o.object_id = ctt.object_id where o.[type] = 'U' and o.name = '{2}') ALTER TABLE [{0}].[{1}].[{2}] ENABLE CHANGE_TRACKING WITH (TRACK_COLUMNS_UPDATED = OFF);", table.Database, table.Schema, table.Table);
        c.Execute(enableChangeTrackingSql);
      }
    }

    private static IEnumerable<RowChange> GetChangesFromVersion(TrackedTable table)
    {
        var changes = new List<RowChange>();
        using (var c = new SqlConnection(table.ConnectionString))
        {
            string changedRowSql = GetChangedRowSql(table);
            changes.AddRange(
              c.Query<object>(changedRowSql, new { Version = table.Version ?? 0 })
                .Select(JObject.FromObject)
                .Select(jRow => new RowChange
                {
                    SYS_CHANGE_VERSION = jRow["SYS_CHANGE_VERSION"].Value<long>(),
                    SYS_CHANGE_OPERATION = jRow["SYS_CHANGE_OPERATION"].ToString(),
                    Table = table.Table,
                    Database = table.Database,
                    Row = jRow
                }));
        }
        return changes;
    }

    private static IList<Column> GetColumns(TrackedTable table)
    {
        using (var c = new SqlConnection(table.ConnectionString))
        {
            string query = string.Format(@"USE {0};
                                            SELECT  c.COLUMN_NAME as Name,CASE WHEN pk.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END AS IsPrimaryKey
                                            FROM INFORMATION_SCHEMA.COLUMNS c
                                            LEFT JOIN (
                                                        SELECT ku.TABLE_CATALOG,ku.TABLE_SCHEMA,ku.TABLE_NAME,ku.COLUMN_NAME
                                                        FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS AS tc
                                                        INNER JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE AS ku
                                                            ON tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
                                                            AND tc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME
                                                        )   pk
                                            ON  c.TABLE_CATALOG = pk.TABLE_CATALOG
                                                        AND c.TABLE_SCHEMA = pk.TABLE_SCHEMA
                                                        AND c.TABLE_NAME = pk.TABLE_NAME
                                                        AND c.COLUMN_NAME = pk.COLUMN_NAME
                                            WHERE c.TABLE_NAME = '{2}' and c.TABLE_SCHEMA = '{1}'
                                            ORDER BY c.TABLE_SCHEMA,c.TABLE_NAME, c.ORDINAL_POSITION", table.Database, table.Schema, table.Table);
            return c.Query<Column>(query).ToList();
        }
    }

    private static string GetFullName(TrackedTable table)
    {
        return "[" + table.Database + "].[" + table.Schema + "].[" + table.Table + "]";
    }

    private static string GetChangedRowSql(TrackedTable table)
    {
        using (var c = new SqlConnection(table.ConnectionString))
        {
            var columns = GetColumns(table);
            var selectClauseColumns = string.Join(",", columns.Select(x => x.IsPrimaryKey ? "ct.[" + x.Name + "]" : "t.[" + x.Name + "]").ToArray());
            string fullName = GetFullName(table);

            string sql = "select ct.SYS_CHANGE_VERSION, ct.SYS_CHANGE_OPERATION, " + selectClauseColumns + " from CHANGETABLE(CHANGES " + fullName + ", @Version) ct " +
                "left outer join " + fullName + " (nolock) t on ";
            string[] onClauseColumns = columns.Where(col => col.IsPrimaryKey).Select(r => string.Format("t.[{0}] = ct.[{0}]", r.Name)).ToArray();
            sql += string.Join(" and ", onClauseColumns);
            return sql;
        }
    }

    struct Column
    {
      public string Name;
      public bool IsPrimaryKey;
    }
  }
}
