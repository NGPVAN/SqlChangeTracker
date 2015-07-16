using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TableTale
{
    public class Program
    {
        public static void Main()
        {
            CancellationTokenSource cts = new CancellationTokenSource();
            var tt = new TableTale(MyCustomCallback, cts.Token);
            cts.CancelAfter(TimeSpan.FromMinutes(5));
            cts.Token.WaitHandle.WaitOne();
        }

        public static void MyCustomCallback(RowChange change)
        {
            Console.WriteLine(JsonConvert.SerializeObject(change));
        }
    }
}
