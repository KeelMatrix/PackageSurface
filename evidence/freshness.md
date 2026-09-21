# Pre-push freshness evidence

Captured on 2026-09-21 immediately before repository initialization and the first push.

## Package freshness

```text
COMMAND: curl.exe -sS -o NUL -w "HTTP %{http_code}`n" https://api.nuget.org/v3-flatcontainer/keelmatrix.packagesurface/index.json
DURATION_MS: 435
EXIT_CODE: 0
OUTPUT:
HTTP 404

COMMAND: curl.exe -sS -o NUL -w "HTTP %{http_code}`n" https://api.nuget.org/v3/registration5-gz-semver2/keelmatrix.packagesurface/index.json
DURATION_MS: 348
EXIT_CODE: 0
OUTPUT:
HTTP 404
```

## Command-name collision check

The exact .NET tool search returned no package named `package-surface` in its first 20 results. The GitHub repository search returned no exact developer-tool repository; its results were unrelated packages using the word “surface”. This is a practical collision check, not a legal trademark search.

```text
COMMAND: dotnet tool search package-surface --take 20
DURATION_MS: 1301
EXIT_CODE: 0
OUTPUT:
Package ID                                   Latest Version      Authors         Downloads      Verified
--------------------------------------------------------------------------------------------------------
microsoft.cst.attacksurfaceanalyzer.cli      2.3.331             Microsoft       211896         x       
fs.gg.governance.fsharpsurfacecommand        1.12.1              FS-GG           2962           x       
microsoft.cst.applicationinspector.cli       1.10.2              Microsoft       353752         x       
dimonsmart.nugetmcpserver                    1.1.9               DimonSmart      2788           x       
jvcode                                       0.1.1               jonv11          200                    

COMMAND: gh search repos package-surface --limit 20 --json fullName --jq ".[].fullName"
DURATION_MS: 1261
EXIT_CODE: 0
OUTPUT:
SarahWeiii/diso
jpvantassel/swprocess
opengeos/lidar
ram-lab/lidar_appearance_calibration
dstansby/pfsspy
danjgale/surfplot
srmainwaring/asv_wave_sim
midraed/water
basf/autoadsorbate
nicebread/RSA
weria-pezeshkian/FreeDTS
rvlenth/rsm
sportsdataverse/sportyR
jpvantassel/swprepost
hridaybavle/semantic_slam
DOI-USGS/hyswap
perazz/fitpack
lfengmle/surfpy
jbrussell/mat-LRTdisp
linux-surface/repo
```

The first attempted command with an unsupported `--source` option is intentionally not used as evidence; the valid exact search above is the authoritative collision check.
