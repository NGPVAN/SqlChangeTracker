﻿-- drop table dbo.TrackedRows
IF NOT EXISTS (
    SELECT * FROM sys.tables t
    INNER JOIN sys.schemas s on t.schema_id = s.schema_id
    WHERE s.name = 'dbo' and t.name = 'TrackedRows'
)
BEGIN
CREATE TABLE [dbo].[TrackedRows](
	[Id] bigint not null primary key identity(1,1),
	[ConnectionString] [nvarchar](512) NOT NULL,
	[Schema] [nvarchar](512) NOT NULL default (N'dbo'),
	[Database] [nvarchar](512) NOT NULL,
	[Table] [nvarchar](512) NULL,
	[Procedure] [nvarchar](512) NULL,
	[Version] [bigint] NULL,
	[LastRun] [datetime] NULL
) ON [PRIMARY]

alter table [dbo].TrackedRows add constraint UC_TrackedRows unique ([Database], [Schema], [Table], [Procedure])
END

/*
CREATE PROCEDURE procGetChanges
	@Version bigint out
AS
BEGIN
select
	GETDATE() as ExampleColumn1,
	NEWID() as ExampleColumn2
set @Version = @Version + 1
END
GO
*/