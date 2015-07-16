-- drop table dbo.TrackedTable
IF NOT EXISTS (
    SELECT * FROM sys.tables t
    INNER JOIN sys.schemas s on t.schema_id = s.schema_id
    WHERE s.name = 'dbo' and t.name = 'TrackedTable'
)
BEGIN
CREATE TABLE [dbo].[TrackedTable](
	[Id] bigint not null primary key identity(1,1),
	[ConnectionString] [nvarchar](512) NOT NULL,
	[Schema] [nvarchar](512) NOT NULL default (N'dbo'),
	[Database] [nvarchar](512) NOT NULL,
	[Table] [nvarchar](512) NOT NULL,
	[Version] [bigint] NULL,
	[LastRun] [datetime] NULL
) ON [PRIMARY]

alter table [dbo].TrackedTable add constraint UC_TrackedTable unique ([Database], [Schema], [Table])
END