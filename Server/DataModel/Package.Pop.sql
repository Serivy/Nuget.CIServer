if OBJECT_ID('dbo.Package') is null
begin
	create table Package (
		PackageId int,

		[File] nvarchar(max),
		Id nvarchar(max)
	)
end




