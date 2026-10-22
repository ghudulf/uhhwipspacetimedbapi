#!/usr/bin/env bash
# Static validation only: never publishes, signs, installs or executes a package.
set -uo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
require_all=false
if [[ $# -eq 1 && $1 == --require-all ]]; then
  require_all=true
elif [[ $# -ne 0 ]]; then
  echo 'Usage: tools/validate-packaging.sh [--require-all]' >&2
  exit 2
fi
status=0
checks=0
skips=0
run() {
  printf 'RUN'
  printf ' %q' "$@"
  printf '\n'
  checks=$((checks + 1))
  if "$@"; then
    echo 'PASS'
  else
    echo 'FAIL' >&2
    status=1
  fi
}
missing() {
  echo "SKIP $1 (not available; install it and rerun)"
  skips=$((skips + 1))
  if $require_all; then status=1; fi
}

if command -v appstreamcli >/dev/null 2>&1; then
  appstreamcli --version
  run appstreamcli validate --no-net --pedantic "$repo_root/packaging/flatpak/com.bru.avtopark.ticketsales.metainfo.xml"
else
  missing appstreamcli
fi
if command -v desktop-file-validate >/dev/null 2>&1; then
  # desktop-file-validate has no --version; query the system package if supported.
  if command -v dpkg-query >/dev/null 2>&1; then
    dpkg-query -W -f='desktop-file-utils ${Version}\n' desktop-file-utils 2>/dev/null || true
  fi
  run desktop-file-validate "$repo_root/packaging/flatpak/com.bru.avtopark.ticketsales.desktop"
else
  missing desktop-file-validate
fi
if command -v shellcheck >/dev/null 2>&1; then
  shellcheck --version
  run shellcheck "$repo_root/packaging/flatpak/prepare.sh" "$repo_root/tools/validate-packaging.sh"
else
  missing shellcheck
fi
if command -v xmllint >/dev/null 2>&1; then
  xmllint --version
  run xmllint --nonet --noout "$repo_root/packaging/flatpak/com.bru.avtopark.ticketsales.metainfo.xml" \
    "$repo_root/packaging/windows/AppxManifest.xml.in" "$repo_root/packaging/windows/BRU.Avtopark.appinstaller.in"
else
  missing xmllint
fi
if command -v pwsh >/dev/null 2>&1; then
  # Pass paths as environment data, not interpolated PowerShell source.
  powershell_script=$(cat <<'POWERSHELL'
    $ErrorActionPreference = "Stop"
    "PowerShell " + $PSVersionTable.PSVersion.ToString()
    $path = Join-Path $env:BRU_PACKAGING_ROOT "packaging/windows/Build-Package.ps1"
    $tokens = $null; $parseErrors = $null
    $null = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count) { $parseErrors | Format-List; exit 1 }
    "PASS PowerShell parsing"
    if (-not (Get-Module -ListAvailable PSScriptAnalyzer)) {
      "SKIP PSScriptAnalyzer (not available; install it and rerun)"
      if ($env:BRU_PACKAGING_REQUIRE_ALL -eq "true") { exit 1 }
      exit 0
    }
    Import-Module PSScriptAnalyzer
    "PSScriptAnalyzer " + (Get-Module PSScriptAnalyzer).Version.ToString()
    $findings = @(Invoke-ScriptAnalyzer -Path $path -Severity Information,Warning,Error)
    if ($findings.Count) { $findings | Format-Table -AutoSize; exit 1 }
    "PASS PSScriptAnalyzer: 0 findings"
POWERSHELL
)
  run env BRU_PACKAGING_ROOT="$repo_root" BRU_PACKAGING_REQUIRE_ALL="$require_all" pwsh -NoLogo -NoProfile -NonInteractive -Command "$powershell_script"
else
  missing 'PowerShell parsing / PSScriptAnalyzer (pwsh)'
fi
echo "External packaging validation: $checks checks, $skips unavailable tools, exit $status."
echo 'Missing tools are not validation. Use --require-all to fail on any missing validator/module.'
exit "$status"
