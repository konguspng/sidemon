<?php
// SideMon report relay. Receives a user-approved log report and stores it as a file in a PRIVATE
// GitHub repo. The GitHub token lives ONLY in the config file below, never in the app or this repo.
//
// Setup on the server:
//  1. Upload this file to the web folder (https://pishonstudio.com/sidemon/report.php).
//  2. Create sidemon_relay_config.php in the FTP HOME folder, which is ABOVE the public web folder /pishonstudio.com (not web-accessible):
//       <?php return [
//         'github_token' => 'PASTE_TOKEN_HERE',          // fine-grained, Contents:write on sidemon-reports only
//         'repo'         => 'konguspng/sidemon-reports',
//         'secret'       => 'PASTE_SHARED_SECRET_HERE',  // same value is compiled into the app
//       ];
//  3. The folder sidemon_relay_data is created next to the config (home folder) for rate-limit counters.

header('Content-Type: application/json');
header('X-Content-Type-Options: nosniff');

function fail($code, $msg) { http_response_code($code); echo json_encode(['ok' => false, 'error' => $msg]); exit; }

if ($_SERVER['REQUEST_METHOD'] !== 'POST') fail(405, 'POST only');

$cfgFile = dirname(__DIR__, 2) . '/sidemon_relay_config.php';
if (!is_file($cfgFile)) fail(500, 'not configured');
$cfg = require $cfgFile;
if (empty($cfg['github_token']) || strpos($cfg['github_token'], 'PASTE_') === 0) fail(500, 'not configured');

$raw = file_get_contents('php://input', false, null, 0, 70000);
if ($raw === false || strlen($raw) > 65536) fail(413, 'too large');
$in = json_decode($raw, true);
if (!is_array($in)) fail(400, 'bad json');

// shared secret: stops casual abuse only (it ships inside the app)
if (empty($cfg['secret']) || !hash_equals($cfg['secret'], (string)($in['secret'] ?? ''))) fail(403, 'forbidden');

// rate limit: 5 reports per IP per hour, 200 per day overall
$dataDir = dirname(__DIR__, 2) . '/sidemon_relay_data';
if (!is_dir($dataDir)) @mkdir($dataDir, 0700, true);
$ip = $_SERVER['REMOTE_ADDR'] ?? 'x';
$ipKey = $dataDir . '/ip_' . hash('sha256', $ip . date('YmdH'));
$dayKey = $dataDir . '/day_' . date('Ymd');
$ipN = (int)@file_get_contents($ipKey); $dayN = (int)@file_get_contents($dayKey);
if ($ipN >= 5 || $dayN >= 200) fail(429, 'rate limited');
@file_put_contents($ipKey, $ipN + 1); @file_put_contents($dayKey, $dayN + 1);
foreach (glob($dataDir . '/*') as $f) if (filemtime($f) < time() - 172800) @unlink($f);

// sanitise fields (server-side scrub as a second line of defence; the app scrubs first)
$ver = preg_replace('/[^0-9A-Za-z.\-]/', '', (string)($in['version'] ?? 'unknown'));
$os  = substr(preg_replace('/[^\x20-\x7E]/', '', (string)($in['os'] ?? '')), 0, 120);
$log = (string)($in['log'] ?? '');
$log = preg_replace('/github_pat_[A-Za-z0-9_]+|ghp_[A-Za-z0-9]+|sk-[A-Za-z0-9\-_]{16,}/', '[REDACTED]', $log);
$log = preg_replace('/C:\\\\Users\\\\[^\\\\\s]+/i', 'C:\\Users\\<user>', $log);
$log = substr($log, -60000);
if (trim($log) === '') fail(400, 'empty');

$id = bin2hex(random_bytes(4));
$path = 'reports/' . date('Y-m') . '/' . date('Ymd-His') . '-v' . $ver . '-' . $id . '.txt';
$body = "SideMon report\nversion: $ver\nos: $os\nreceived: " . gmdate('c') . "\n\n" . $log . "\n";

$ch = curl_init('https://api.github.com/repos/' . $cfg['repo'] . '/contents/' . $path);
curl_setopt_array($ch, [
    CURLOPT_CUSTOMREQUEST => 'PUT',
    CURLOPT_RETURNTRANSFER => true,
    CURLOPT_TIMEOUT => 20,
    CURLOPT_HTTPHEADER => [
        'Authorization: Bearer ' . $cfg['github_token'],
        'Accept: application/vnd.github+json',
        'User-Agent: sidemon-relay',
        'Content-Type: application/json',
    ],
    CURLOPT_POSTFIELDS => json_encode(['message' => "report $id (v$ver)", 'content' => base64_encode($body)]),
]);
$res = curl_exec($ch); $code = curl_getinfo($ch, CURLINFO_HTTP_CODE); curl_close($ch);

if ($code === 201) { echo json_encode(['ok' => true, 'id' => $id]); exit; }
error_log('sidemon relay: github returned ' . $code);   // never echo GitHub's reply or the token
fail(502, 'upstream error');
