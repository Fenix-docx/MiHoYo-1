from pathlib import Path
import hashlib,json
root=Path.cwd()
files=list((root/'Assets/A_UI/UI/Motion').rglob('*.cs'))+list((root/'Assets/Editor').glob('*.cs'))+[root/'docs/ui-motion-module-design.md',root/'docs/ui-motion-usage.md']+list((root/'.dsh-scratch/codex-review').glob('*'))
manifest={str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in files if p.is_file()}
(root/'.dsh-scratch/p1/before.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
for name in ['Path/PathSegment.cs','MotionPlan.cs','Tracks/MotionTrack.cs']:
 p=root/'Assets/A_UI/UI/Motion'/name
 dest=root/'.dsh-scratch/backups/p1'/p.relative_to(root)
 if dest.exists(): raise RuntimeError('Backup already exists: '+str(dest))
 dest.parent.mkdir(parents=True,exist_ok=True);dest.write_bytes(p.read_bytes())
 print(str(p.relative_to(root)),manifest[str(p.relative_to(root))],str(dest.relative_to(root)))
