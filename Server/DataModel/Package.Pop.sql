if OBJECT_ID('dbo.Package') is null
begin
	create table Package (
		PackageId int identity(1,1),
		Id nvarchar(500) not null unique,
		Package nvarchar(max),
		DerivedPackageData nvarchar(max),
	)
end




