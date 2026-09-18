@echo off
set DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
set DOTNET_CLI_TELEMETRY_OPTOUT=1
"C:\Program Files\dotnet\dotnet.exe" test .\Lms.Application.Tests\Lms.Application.Tests.csproj --filter "FullyQualifiedName~CourseServiceImport|FullyQualifiedName~Payment" --nologo -v minimal --no-restore
