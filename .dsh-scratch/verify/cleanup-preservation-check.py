"""Read-only check of the round-3 protected files and pre-append report prefix."""
from pathlib import Path
import hashlib
import json

root = Path(__file__).resolve().parents[2]
excluded = {'verify-build.ps1', 'verify-diff.ps1', 'impl-report.md',
            'Assembly-CSharp-Verify.csproj', 'Assembly-CSharp-Editor-Verify.csproj',
            'INDEX.md', 'verify-diff-latest.txt'}
expected = {"Assets":{"count":4952,"sha256":"544c7a1aad4b0ef9fb195fbf1898cf96959bf8df6e8264ed28b9157a41febbae"},"review":{"count":135,"sha256":"0e08dbd97da0987d1eb6949d58ad507b13604020bb1c7b234e3777b34f83beca"},"original-projects":{"count":6,"sha256":"618d501ac1eed70029b9c08de36d494d07460de865205294f9692db03de774e1"},"report":{"length":58225,"sha256":"097214a59d445fc0668efbb37451a834a28785a759538022bdbdcf92fed1462c"}}
groups = {'Assets': list((root / 'Assets').rglob('*')),
          'review': list((root / '.dsh-scratch/codex-review').rglob('*')),
          'original-projects': list(root.glob('*.csproj')) + [root / 'docs/ui-motion-module-design.md']}
for name, files in groups.items():
    rows = [str(p.relative_to(root)) + ' ' + hashlib.sha256(p.read_bytes()).hexdigest()
            for p in sorted(files) if p.is_file() and p.name not in excluded]
    actual = {'count': len(rows), 'sha256': hashlib.sha256('\n'.join(rows).encode()).hexdigest()}
    assert actual == expected[name], (name, actual, expected[name])
    print(name + ' PRESERVED ' + json.dumps(actual))
report = (root / '.dsh-scratch/codex-review/impl-report.md').read_bytes()
prefix = report[:expected['report']['length']]
assert hashlib.sha256(prefix).hexdigest() == expected['report']['sha256']
print('REPORT_PREFIX PRESERVED ' + json.dumps(expected['report']))
temporary = list(root.glob('*-Verify.csproj'))
assert not temporary, temporary
print('ROOT_VERIFY_COUNT=0')
for name in ['Assembly-CSharp-Verify', 'Assembly-CSharp-Editor-Verify']:
    exists = (root / 'Temp/obj' / name).exists()
    assert not exists, name
    print('Temp/obj/' + name + ' exists=False')
print('PRESERVATION PASS; Library and UnityLockfile not accessed')
