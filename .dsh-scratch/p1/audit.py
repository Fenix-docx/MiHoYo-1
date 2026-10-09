from pathlib import Path
import json,hashlib,difflib
r=Path.cwd();o=r/'.dsh-scratch/p1';before=json.loads((o/'before.json').read_text(encoding='utf-8'))
delivered=json.loads((o/'deliverables.json').read_text(encoding='utf-8'));allowed={str(Path(d['path'])) for d in delivered if d['modified']}
unchanged=[];unexpected=[]
for rel,sha in before.items():
 p=r/rel;current=hashlib.sha256(p.read_bytes()).hexdigest() if p.is_file() else 'MISSING'
 if current==sha:unchanged.append(rel)
 elif rel not in allowed:unexpected.append(rel)
patch=[]
for d in delivered:
 p=r/d['path'];d['sha256']=hashlib.sha256(p.read_bytes()).hexdigest();d['lines']=len(p.read_text(encoding='utf-8-sig').splitlines())
 if d['modified']:
  backup=r/'.dsh-scratch/backups/p1'/d['path']
  if hashlib.sha256(backup.read_bytes()).hexdigest()!=before[str(Path(d['path']))]:raise RuntimeError('Backup mismatch')
  patch.extend(difflib.unified_diff(backup.read_text(encoding='utf-8-sig').splitlines(True),p.read_text(encoding='utf-8-sig').splitlines(True),fromfile='before/'+d['path'],tofile='after/'+d['path']))
(o/'evidence/changes.diff').write_text(''.join(patch),encoding='utf-8')
(o/'deliverables.json').write_text(json.dumps(delivered,ensure_ascii=False,indent=2),encoding='utf-8')
result={'expected_modified':sorted(allowed),'unchanged_count':len(unchanged),'unchanged_files':unchanged,'unexpected_changes':unexpected,'forbidden_directories':'Not inspected or accessed by audit','assets_error_probe_scan':[str(p.relative_to(r)) for d in delivered for p in [r/d['path']] if '#error' in p.read_text(encoding='utf-8-sig')]}
(o/'evidence/scope-audit.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
print('AUDIT expected_modified='+str(len(allowed))+' unchanged='+str(len(unchanged))+' unexpected='+str(len(unexpected))+' Assets_error_probes='+str(len(result['assets_error_probe_scan'])))
if unexpected or result['assets_error_probe_scan']:raise RuntimeError('Scope violation')
