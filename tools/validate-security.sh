#!/usr/bin/env bash
# No tool installation, workflow edits, deployment or persistent project injection.
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
exec python3 - "$repo_root" "$@" <<'PYTHON'
import argparse
import collections
import json
import os
import pathlib
import shutil
import subprocess
import sys
import tempfile
import time
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser(description='Local security scans; raw findings are not silently suppressed.')
parser.add_argument('--output', help='Fresh artifact directory outside the repository')
parser.add_argument('--codeql', action='store_true', help='Also build/analyze a fresh C# CodeQL database (40-minute total limit)')
args = parser.parse_args(sys.argv[2:])
root = pathlib.Path(sys.argv[1]).resolve()
preferred_tmp = '/tmp/opencode' if pathlib.Path('/tmp/opencode').is_dir() else None
if args.output:
    out = pathlib.Path(args.output).resolve()
    if out == root or root in out.parents:
        parser.error('--output must be outside the repository')
    out.mkdir(parents=True, exist_ok=False, mode=0o700)
else:
    out = pathlib.Path(tempfile.mkdtemp(prefix='bru-security-', dir=preferred_tmp))
print('Artifacts:', out, flush=True)
summary = {'revision': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=root, text=True).strip(), 'commands': [], 'errors': []}
has_findings = False

def run(label, command, cwd=root, timeout=600):
    path = out / (label + '.log')
    print('RUN', label, flush=True)
    entry = {'label': label, 'command': command, 'cwd': str(cwd), 'log': str(path)}
    summary['commands'].append(entry)
    try:
        with path.open('w') as stream:
            result = subprocess.run(command, cwd=cwd, stdout=stream, stderr=subprocess.STDOUT, timeout=timeout)
        entry['exit'] = result.returncode
        if result.returncode:
            raise RuntimeError(label + ' failed; see ' + str(path))
    except Exception as error:
        entry.setdefault('exit', 'timeout/error')
        raise RuntimeError(str(error)) from error
    return path

def sarif(path):
    return [result for run in json.loads(path.read_text())['runs'] for result in run.get('results', [])]

try:
    for tool in ('dotnet', 'git'):
        if not shutil.which(tool):
            raise RuntimeError('Required tool unavailable: ' + tool)
    run('dotnet-info', ['dotnet', '--info'])
    projects = sorted(root.rglob('*.csproj'))
    projects = [p for p in projects if not any(part in ('obj', 'bin', '.git') for part in p.relative_to(root).parts)]
    package_results = []
    for project in projects:
        name = project.stem
        command = ['dotnet', 'restore', str(project)]
        if 'Maui' in name:
            command.append('-p:EnableMobileTargets=false')
        run(name + '-restore', command, project.parent)
        for mode in ('vulnerable', 'deprecated'):
            log = run(name + '-' + mode, ['dotnet', 'list', str(project), 'package', '--' + mode, '--include-transitive', '--format', 'json'], project.parent)
            text = log.read_text()
            data = json.loads(text[text.index('{'):])
            entries = [package for p in data.get('projects', []) for f in p.get('frameworks', [])
                       for key in ('topLevelPackages', 'transitivePackages') for package in f.get(key, [])]
            package_results.append({'project': str(project.relative_to(root)), 'mode': mode, 'packages': entries})
            print(name, mode, len(entries), flush=True)
    summary['packages'] = package_results
    has_findings |= any(row['packages'] for row in package_results)

    # Snapshot tracked working-tree files before analysis builds. This matches the DevSkim
    # workflow's fresh-checkout scope, not ignored compiler caches created by earlier builds.
    # Commit new files before running; never follow a symlink out to user/environment secrets.
    snapshot = out / 'source'
    snapshot.mkdir()
    for raw in subprocess.check_output(['git', 'ls-files', '-z'], cwd=root).split(b'\0'):
        if not raw:
            continue
        relative = pathlib.Path(os.fsdecode(raw))
        source = root / relative
        if source.is_symlink():
            raise RuntimeError('Tracked symlink requires explicit owner review: ' + str(relative))
        if source.is_file():
            target = snapshot / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, target)
    devskim = os.environ.get('DEVSKIM', 'devskim')
    if not shutil.which(devskim):
        raise RuntimeError('DevSkim unavailable; install the documented tool or set DEVSKIM')
    run('devskim-version', [devskim, '--version'])
    report = out / 'devskim.sarif'
    run('devskim', [devskim, 'analyze', '-I', str(snapshot), '-O', str(report), '-g', '**/.git/**,**/bin/**', '--skip-excerpts'])
    results = sarif(report)
    summary['devskim'] = dict(collections.Counter(r['ruleId'] for r in results))
    has_findings |= bool(results)

    # Match the workflow add-action without modifying any csproj or Directory.Build.props.
    scs = out / 'scs'
    scs.mkdir()
    props = ET.Element('Project')
    ET.SubElement(props, 'Import', Project=str(root / 'Directory.Build.props'))
    group = ET.SubElement(props, 'PropertyGroup')
    ET.SubElement(group, 'ErrorLog').text = str(scs / '$(MSBuildProjectName).$(TargetFramework).sarif')
    ET.SubElement(group, 'AdditionalFileItemNames').text = '$(AdditionalFileItemNames);Content'
    ET.SubElement(ET.SubElement(props, 'ItemGroup'), 'PackageReference', Include='SecurityCodeScan.VS2019', Version='5.6.7', PrivateAssets='all')
    props_path = out / 'scs.props'
    ET.ElementTree(props).write(props_path, encoding='utf-8', xml_declaration=True)
    scs_projects = [
        'server', 'BRU-AVTOPARK-AspireAPI/BRU-AVTOPARK-AspireAPI.ApiService',
        'BRU-AVTOPARK-AspireAPI/BRU-AVTOPARK-AspireAPI.AppHost',
        'BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Desktop', 'BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Maui',
        'shared/BRU.Avtopark.Client', 'tools/DatabaseBootstrap'
    ]
    for relative in scs_projects:
        project = root / relative
        command = ['dotnet', 'build', '--no-incremental', '-p:UseSharedCompilation=false', '-p:DirectoryBuildPropsPath=' + str(props_path)]
        if 'Maui' in relative:
            command.append('-p:EnableMobileTargets=false')
        log = run(project.name + '-scs', command, project)
        if 'AD0001' in log.read_text():
            raise RuntimeError('Analyzer crashed; not a valid scan: ' + str(log))
    results = [r for path in scs.glob('*.sarif') for r in sarif(path) if r.get('ruleId', '').startswith('SCS')]
    summary['securitycodescan'] = dict(collections.Counter(r['ruleId'] for r in results))
    has_findings |= bool(results)

    if args.codeql:
        codeql = os.environ.get('CODEQL', 'codeql')
        if not shutil.which(codeql):
            raise RuntimeError('CodeQL unavailable; install it and the C# query pack or set CODEQL')
        run('codeql-version', [codeql, 'version', '--format=json'])
        started = time.monotonic()
        database = out / 'codeql-db'
        run('codeql-create', [codeql, 'database', 'create', str(database), '--language=csharp', '--source-root=' + str(root),
            '--command=dotnet build BRU-AVTOPARK-AspireAPI/BRU-AVTOPARK-AspireAPI.ApiService --no-incremental -p:UseSharedCompilation=false', '--threads=2'], timeout=2400)
        report = out / 'codeql.sarif'
        run('codeql-analyze', [codeql, 'database', 'analyze', str(database), 'codeql/csharp-queries:codeql-suites/csharp-security-and-quality.qls',
            '--format=sarifv2.1.0', '--output=' + str(report), '--threads=2', '--ram=2200'], timeout=max(1, 2400 - (time.monotonic() - started)))
        results = sarif(report)
        summary['codeql'] = dict(collections.Counter(r['ruleId'] for r in results))
        has_findings |= bool(results)
except Exception as error:
    summary['errors'].append(str(error))
    print('ERROR:', error, file=sys.stderr)
finally:
    (out / 'summary.json').write_text(json.dumps(summary, indent=2))
    print('Summary:', out / 'summary.json', flush=True)
    print('Raw findings are retained. Review docs/architecture/security-validation.md; no automatic suppression/clean claim.', flush=True)
sys.exit(2 if summary['errors'] else 1 if has_findings else 0)
PYTHON
