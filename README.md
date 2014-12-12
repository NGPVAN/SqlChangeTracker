TableTale
=========

TableTale allows you to call custom code when a row changes in Sql Server.

It uses the a change tracking feature of SQL server.  The list of changes are walked and joined back to 
the source table in batches.  TableTale serializes the entire row to JSON.  It keeps track of the latest row version 
in a metadata table that it automatically creates and maintains.

TableTale can be used to feed an ETL process, such as maintaining a search index.  
The callback provided can do anything.

    void Main()
    {		
    	string connectionString = "Server=.;Integrated Security=true;Database=Test";
    	string table = "Test3";
    	CancellationTokenSource cts  = new CancellationTokenSource();	
    	var tt = new TableTale(connectionString, table, MyCustomCallback, cts.Token);
    	cts.CancelAfter(TimeSpan.FromMinutes(5));
    	cts.Token.WaitHandle.WaitOne();
    }
    
    public void MyCustomCallback(RowChange change) {
    	Console.WriteLine(JsonConvert.SerializeObject(change));
    }
