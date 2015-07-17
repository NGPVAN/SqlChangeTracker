using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Newtonsoft.Json.Linq;

namespace TableReader
{
  public class TableTale
  {
    private CancellationToken _token;
    private readonly Task _task;
    private readonly Action<TrackedTable, List<RowChange>> _onChange;
    private readonly TimeSpan _interval;

    public TableTale(Action<TrackedTable, List<RowChange>> onChange, CancellationToken token)
    {
      _interval = TimeSpan.Parse(System.Configuration.ConfigurationManager.AppSettings["PollInterval"]);
      _token = token;
      _onChange = onChange;

      using (var m = new TestEntities())
      {
          foreach (var tt in m.TrackedTables)
          {
              if (!string.IsNullOrEmpty(tt.Table))
              {
                  tt.EnableChangeTracking(); // table-based tracking only
              }
          }
      }

      _task = Task.Run(() => { DoWork(); }, _token);
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
                try
                {
                    var queryStopwatch = Stopwatch.StartNew();
                    var changes = tt.GetChangesFromVersion().ToList();
                    queryStopwatch.Stop();

                    var publishStopwatch = Stopwatch.StartNew();
                    _onChange(tt, changes);
                    publishStopwatch.Stop();

                    foreach (var change in changes)
                    {
                        if (change.SYS_CHANGE_VERSION > (tt.Version ?? 0))
                        {
                            tt.Version = change.SYS_CHANGE_VERSION;
                        }
                    }

                    tt.LastRun = DateTime.Now;
                    Trace.WriteLine(string.Format("{0}: {1} changes.  query took {2}, publish took {3}", tt.GetFullName(), changes.Count(), queryStopwatch.Elapsed.ToString(), publishStopwatch.Elapsed.ToString()));
                    m.SaveChanges();
                }
                catch (Exception ex)
                {
                    Trace.WriteLine(ex.ToString());
                }
            }
        }

        Trace.WriteLine(string.Format("Sleeping for {0}", _interval));
        _token.WaitHandle.WaitOne(_interval);
      }
    }
  }
}
