from pathlib import Path
import hashlib, json

root = Path(__file__).resolve().parents[2]
out = Path(__file__).resolve().parent
review = root / '.dsh-scratch/codex-review'
protected = list(review.rglob('*')) + list(root.glob('*.csproj'))
protected += [root / 'docs/ui-motion-module-design.md']
protected += [p for p in (root / 'Assets').rglob('*.cs') if p.name in {
    'GooeyUIGroup.cs', 'GooeyWarpEffect.cs', 'SdfBlendField.cs', 'SdfFieldNode.cs',
    'SdfUiSpace.cs', 'UIManager.cs', 'Subpanel.cs', 'UINavButton.cs', 'UINavNode.cs', 'UINavigationLocator.cs'}]
hashes = {str(p.relative_to(root)): hashlib.sha256(p.read_bytes()).hexdigest()
          for p in protected if p.is_file()}
(out / 'protected-before.json').write_text(json.dumps(hashes, indent=2), encoding='utf-8')
script = (review / 'verify-build.py').read_text(encoding='utf-8-sig')
script = script.replace('codex-review', 'codex-repair-round2').replace('-Verify', '-RepairRound2')
script = script.replace('try:\n prepare()', "try:\n if any(p.exists() for p in copies): raise RuntimeError('Verification project already exists')\n prepare()")
(out / 'verify-build.py').write_text(script, encoding='utf-8')
(out / 'evidence').mkdir(exist_ok=True)
(out / 'console').mkdir(exist_ok=True)
for name in ['MotionProof.csproj', 'Program.cs']:
    (out / 'console' / name).write_bytes((review / 'console' / name).read_bytes())
print('Protected file hashes recorded; isolated verifier and byte-identical console sources prepared.')
