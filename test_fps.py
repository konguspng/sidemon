import os, sys, subprocess, shutil, time
p = r"SidebarDiagnostics/App.xaml.cs"
txt = open(p, encoding='utf-8').read()
if "CompositionTarget.Rendering" not in txt:
    txt = txt.replace("public App()", "public static int _frames = 0;\n        public App()")
    txt = txt.replace("protected override void OnStartup(StartupEventArgs e)\n        {", "protected override void OnStartup(StartupEventArgs e)\n        {\n            System.Windows.Media.CompositionTarget.Rendering += (s, ev) => _frames++;")
    open(p, 'w', encoding='utf-8').write(txt)

p2 = r"SidebarDiagnostics/SidebarModel.cs"
txt2 = open(p2, encoding='utf-8').read()
if "File.AppendAllText" not in txt2:
    txt2 = txt2.replace("private void MonitorTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)\n        {", "private void MonitorTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)\n        {\n            System.IO.File.AppendAllText(@\"profiling.txt\", $\"Tick! Frames: {SidebarDiagnostics.App._frames}\\n\");\n            SidebarDiagnostics.App._frames = 0;")
    open(p2, 'w', encoding='utf-8').write(txt2)

if os.path.exists("profiling.txt"): os.remove("profiling.txt")

env = dict(os.environ, MSBuildEnableWorkloadResolver="false")
subprocess.run(["dotnet", "build", "SidebarDiagnostics/SidebarDiagnostics.csproj", "-c", "Release"], env=env)
p = subprocess.Popen(["SidebarDiagnostics/bin/Release/net48/SideMon.exe"])
time.sleep(10)
p.kill()
try:
    print(open("profiling.txt").read())
except:
    pass
