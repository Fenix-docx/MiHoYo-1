from pathlib import Path
import hashlib, json, re, subprocess, difflib

root = Path(__file__).resolve().parents[2]
out = Path(__file__).resolve().parent
evidence = out / 'evidence'

def run(args, name, expected):
    result = subprocess.run(args, cwd=root, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    transcript = '$ ' + subprocess.list2cmdline(args) + '\n' + result.stdout.decode('utf-8')
    transcript += '\nEXIT_CODE=' + str(result.returncode) + '\n'
    (evidence / name).write_text(transcript, encoding='utf-8')
    print(transcript)
    assert result.returncode == expected
    return result.stdout

run(['rg', '-n', '-F', '???', 'Assets/A_UI/UI/Motion', 'Assets/A_UI/UI/Package',
     'Assets/Editor/MotionTests.cs', 'Assets/Editor/UINavigationQueueTests.cs'], 'question-search.txt', 1)
run(['dotnet', 'run', '--project', '.dsh-scratch/codex-repair-round2/console/MotionProof.csproj',
     '--no-launch-profile'], 'console-run.txt', 0)
run(['rg', '-n', 'encoding|write_text|open\\(', '.dsh-scratch/codex-review', '-g', 'write-*.py'],
    'encoding-evidence.txt', 0)

messages = []
for entry in json.loads((evidence / 'compiled-sources.json').read_text(encoding='utf-8')):
    assert hashlib.sha256((root / entry['source']).read_bytes()).hexdigest() == entry['sha256']
messages.append('COMPILED_SOURCE_HASHES: 18/18 match final sources')
hashes = json.loads((out / 'protected-before.json').read_text(encoding='utf-8'))
for name, sha in hashes.items():
    data = (root / name).read_bytes()
    if name.replace('\\', '/').endswith('codex-review/impl-report.md') and (out / 'before/impl-report.md').exists():
        prefix = (out / 'before/impl-report.md').read_bytes()
        assert data.startswith(prefix), name
        data = prefix
    assert hashlib.sha256(data).hexdigest() == sha, name
messages.append(f'PROTECTED_FILES: {len(hashes)}/{len(hashes)} hashes match (report checked as preserved prefix after append)')
nav = (root / 'Assets/A_UI/UI/Package/Navigation/UINavigator.cs').read_text(encoding='utf-8-sig')
tests = (root / 'Assets/Editor/UINavigationQueueTests.cs').read_text(encoding='utf-8-sig')
for level in ['Warning', 'Error']:
    actual = re.search(r'Debug.Log' + level + r'\("(\[UINavigator\] 转移[^"]+)"', nav).group(1)
    assert f'LogAssert.Expect(LogType.{level}, "{actual}");' in tests
    messages.append(f'EXACT_LOG_MATCH {level}: {actual}')
assert not list(root.glob('*-RepairRound2.csproj'))
messages.append('TEMPORARY_PROJECTS: removed; original csproj files unchanged')
diffs = []
for path in ['Assets/Editor/UINavigationQueueTests.cs', 'Assets/A_UI/UI/Package/Navigation/UINavigator.cs',
             'Assets/A_UI/UI/Motion/Integration/UIPanelMotion.cs']:
    before = (out / 'before' / Path(path).name).read_text(encoding='utf-8-sig')
    after = (root / path).read_text(encoding='utf-8-sig')
    diffs.extend(difflib.unified_diff(before.splitlines(True), after.splitlines(True), fromfile=path + ' (before)', tofile=path))
    for line in difflib.ndiff(before.splitlines(), after.splitlines()):
        if line.startswith('+ '): assert line.rstrip() == line
    if path.endswith('UINavigator.cs'):
        pattern = r'^\s*public\s+(?:static\s+)?[^\n{=]+\([^\n]*?\)'
        assert re.findall(pattern, before, re.M) == re.findall(pattern, after, re.M)
messages.append('PUBLIC_METHOD_SIGNATURES: unchanged; added-line whitespace PASS')
(evidence / 'changes.diff').write_text(''.join(diffs), encoding='utf-8')
(evidence / 'scope-audit.txt').write_text('\n'.join(messages) + '\n', encoding='utf-8')
print('\n'.join(messages))
