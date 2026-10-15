import json

file_path = "SidebarDiagnostics/ChangeLog.json"
with open(file_path, "r", encoding="utf-8") as f:
    data = json.load(f)

for item in data:
    if item["Version"] == "4.3.0":
        item["Changes"].insert(0, "Crash Prevention: Improved defensive coding to prevent crashes on diverse hardware configurations.")
        item["Changes"].insert(1, "UI Scale Auto-Fit: Added toggle for 'Shrink automatically to fit the screen'. The slider is no longer hard-capped below 1.00 when auto-fit is triggered.")
        item["Changes"].insert(2, "Settings Window Improvements: Settings window is now properly titled, displays the app's icon, and appears in the taskbar.")
        break

with open(file_path, "w", encoding="utf-8") as f:
    json.dump(data, f, indent=2)
