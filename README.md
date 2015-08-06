SqlChangeTracker
=========

SqlChangeTracker is a windows service (or console app) which tracks row changes in Sql Server.  

There are two modes of operation: **table mode** and **stored procedure mode**.  The metadata table allows both modes to be used at once.  

In the table mode, only a table name needs to be given, and all row changes are tracked.  Change tracking is automatically enabled on the table, if it is not already.  Note that delete operations will currently only return the primary key values.  

In stored procedure mode, a custom stored procedure can be given which returns the row changes.  That procedure is passed a version parameter, which represents the last tracked version.

# Configuration

Change the connection string in the app.config to a database.  Run the included db.sql to set up the metadata table.

# Metadata

A single metadata table is used which keeps record of the latest version seen for each tracked item.  The table contains all the connection information necessary for the tracker to connect to the database being tracked.

# Azure Blob Storage

Optionally, the configuration variables `AccountName` and `AccountKey` can be set.  In this mode, the changes will be serialized to json files and uploaded as blobs to Azure blob storage.

# Table Mode

Simply insert a row into the metadata table listing the connection string, database, schema, and table to track.

# Stored Procedure Mode

A stored procedure can be used instead of a table.  In this mode, change tracking is not automatically enabled.  The procedure must adhere to several conventions:

* An input/output parameter named `@Version` must exist
* The `@Version` must be set by the procedure to be upper bound (inclusive) of row versions returned in the result set.  Each iteration of calling the procedure will pass the value set by the previous call.
* The rows must include two columns, `SYS_CHANGE_OPERATION` and `SYS_CHANGE_VERSION` 

Example:

	create PROCEDURE procGetChanges
		@Version bigint = 0 output 
	AS
	BEGIN
	set @Version = isnull(@Version,0) + 1 -- new version must be set
	select 
		GETDATE() as ExampleColumn1,
		NEWID() as ExampleColumn2,
		@Version as SYS_CHANGE_VERSION, -- must be included
		'I' as SYS_CHANGE_OPERATION -- must be included
	END
	GO
