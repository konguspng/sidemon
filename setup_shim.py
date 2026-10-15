import os, json, time, sys

test_dir = os.path.expandvars(r"%LOCALAPPDATA%\Temp\claude_test")
os.makedirs(test_dir, exist_ok=True)

expired_time = int(time.time() * 1000) - 3600000 # 1 hour ago
creds = {
    "claudeAiOauth": {
        "accessToken": "fake-token-123",
        "expiresAt": expired_time,
        "subscriptionType": "pro"
    }
}
with open(os.path.join(test_dir, ".credentials.json"), "w") as f:
    json.dump(creds, f)

cmd_content = f'''@echo off
python "{os.path.join(test_dir, 'shim.py')}" %*
'''
with open(os.path.join(test_dir, "claude.cmd"), "w") as f:
    f.write(cmd_content)

shim_content = f'''import sys, json, time, os
log_file = r"{os.path.join(test_dir, 'shim_log.txt')}"
with open(log_file, "a") as f:
    f.write("CALL: " + " ".join(sys.argv[1:]) + "\\n")

args = sys.argv[1:]
if args == ["auth", "status"]:
    cred_file = r"{os.path.join(test_dir, '.credentials.json')}"
    with open(cred_file, "r") as f:
        creds = json.load(f)
    creds["claudeAiOauth"]["expiresAt"] = int(time.time() * 1000) + 3600000
    with open(cred_file, "w") as f:
        json.dump(creds, f)
    sys.exit(0)
elif args == ["-p", "ok", "--safe-mode", "--tools", ""]:
    sys.exit(0)
else:
    sys.exit(1)
'''
with open(os.path.join(test_dir, "shim.py"), "w") as f:
    f.write(shim_content)
