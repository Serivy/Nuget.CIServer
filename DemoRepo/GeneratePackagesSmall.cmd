SET ID=%computername%%date%%time% 
SET ID=%ID:/=%
SET ID=%ID: =%
SET ID=%ID:.=%
SET ID=%ID:,=%
SET ID=%ID::=%
SET ID=%ID:-=%

for /f "skip=1" %%x in ('wmic os get localdatetime') do if not defined mydate set mydate=%%x

mkdir %ID%

FOR /L %%A IN (1,1,2) DO (
FOR /L %%B IN (1,1,2) DO (
  nuget pack spec.nuspec -OutputDirectory %ID% -Properties id=%ID%%%A;Version=1.%%B.0
)
)