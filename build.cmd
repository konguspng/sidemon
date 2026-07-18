@echo off
rem This machine's .NET SDK has stale workload manifests (mono/emscripten leftovers)
rem that break MSBuild's workload resolver. This app uses no workloads, so skip it.
set MSBuildEnableWorkloadResolver=false

set CONFIG=%1
if "%CONFIG%"=="" set CONFIG=Release

dotnet build "%~dp0SidebarDiagnostics\SidebarDiagnostics.csproj" -c %CONFIG%
