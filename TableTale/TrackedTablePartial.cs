using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using System.Diagnostics;
using System.Transactions;
using System.Data;

namespace TableReader
{
    public partial class TrackedRow
    {
        public IEnumerable<RowChange> GetChangesFromVersion()
        {
            Trace.WriteLine(string.Format("Querying changes for {0}...", GetFullName()));
            var changes = new List<RowChange>();

            using (var scope = new TransactionScope(TransactionScopeOption.RequiresNew, new TransactionOptions() {  IsolationLevel = System.Transactions.IsolationLevel.ReadUncommitted }))
            {
                using (var c = new SqlConnection(ConnectionString))
                {
                    IEnumerable<dynamic> results = new List<dynamic>();

                    if (!Version.HasValue) {
                        Version = 0;
                    }
                    if (!string.IsNullOrEmpty(this.Table))
                    {
                        string changedRowSql = GetChangedRowSql();
                        results = c.Query<dynamic>(changedRowSql, new { Version = this.Version });
                    }

                    if (!string.IsNullOrEmpty(this.Procedure))
                    {
                        var p = new DynamicParameters();
                        p.Add("Version", dbType: DbType.Int64, direction: ParameterDirection.InputOutput, value: Version.Value);
                        results = c.Query<dynamic>(GetFullName(), p, commandType: CommandType.StoredProcedure);
                    }

                    changes.AddRange(
                        results
                        .ToList()
                        .Select(jRow => new RowChange
                        {
                            SYS_CHANGE_VERSION = jRow.SYS_CHANGE_VERSION,
                            SYS_CHANGE_OPERATION = jRow.SYS_CHANGE_OPERATION,
                            Table = Table,
                            Database = Database,
                            Row = jRow
                        }));
                }
            }

            return changes;
        }

        public string GetChangedRowSql()
        {

                using (var c = new SqlConnection(ConnectionString))
                {
                    var columns = GetColumns();
                    var selectClauseColumns = string.Join(",", columns.Select(x => x.IsPrimaryKey ? "ct.[" + x.Name + "]" : "t.[" + x.Name + "]").ToArray());
                    string fullName = GetFullName();

                    string sql = "select ct.SYS_CHANGE_VERSION, ct.SYS_CHANGE_OPERATION, " + selectClauseColumns + " from CHANGETABLE(CHANGES " + fullName + ", @Version) ct " +
                        "left outer join " + fullName + " (nolock) t on ";
                    string[] onClauseColumns = columns.Where(col => col.IsPrimaryKey).Select(r => string.Format("t.[{0}] = ct.[{0}]", r.Name)).ToArray();
                    sql += string.Join(" and ", onClauseColumns);
                    return sql;
                }
        }

        public string GetFullName()
        {
            return "[" + Database + "].[" + Schema + "].[" + (!string.IsNullOrEmpty(Table) ? Table : Procedure) + "]";
        }

        public string GetFileName()
        {
            return GetFullName().Replace("[", string.Empty).Replace("]", string.Empty).ToLowerInvariant();
        }

        public IList<Column> GetColumns()
        {
            using (var c = new SqlConnection(ConnectionString))
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
                                            ORDER BY c.TABLE_SCHEMA,c.TABLE_NAME, c.ORDINAL_POSITION", Database, Schema, Table);
                return c.Query<Column>(query).ToList();
            }
        }

        public void EnableChangeTracking()
        {
            Trace.WriteLine(string.Format("Enabling change tracking for {0}...", GetFullName()));
            using (var c = new SqlConnection(ConnectionString))
            {
                string enableChangeTrackingSql =
                    string.Format(
                    "IF NOT EXISTS(SELECT 1 FROM master.sys.change_tracking_databases ctd join master.sys.databases d on ctd.database_id = d.database_id WHERE d.name = '{0}') ALTER DATABASE [{0}] SET CHANGE_TRACKING = ON (CHANGE_RETENTION = 1 DAYS, AUTO_CLEANUP = ON); " +
                    "IF NOT EXISTS(SELECT 1 from [{0}].sys.change_tracking_tables ctt join [{0}].sys.objects o on o.object_id = ctt.object_id where o.[type] = 'U' and o.name = '{2}') ALTER TABLE [{0}].[{1}].[{2}] ENABLE CHANGE_TRACKING WITH (TRACK_COLUMNS_UPDATED = OFF);", Database, Schema, Table);
                c.Execute(enableChangeTrackingSql);
            }
            Trace.WriteLine(string.Format("done.", GetFullName()));
        }
    }
}
