from pathlib import Path
import json,hashlib,os
r=Path.cwd();o=r/'.dsh-scratch/p1';manifest=json.loads((o/'deliverables.json').read_text(encoding='utf-8'))
changes={
'Assets/A_UI/UI/Motion/Tracks/MotionTrack.cs': [('if (path.kind != PathSegmentKind.Line && path.kind != PathSegmentKind.CubicBezier)','if (!anchors && path.kind != PathSegmentKind.Line && path.kind != PathSegmentKind.CubicBezier)')],
'Assets/A_UI/UI/Motion/Path/PathSegment.cs': [('case PathSegmentKind.CatmullRom:\n                    return .5f','case PathSegmentKind.CatmullRom:\n                    if (t == 0) return p1;\n                    if (t == 1) return p2;\n                    return .5f')]}
for relative,replacements in changes.items():
 p=r/relative;d=next(x for x in manifest if x['path']==relative)
 if hashlib.sha256(p.read_bytes()).hexdigest()!=d['sha256']:raise RuntimeError('Concurrent edit: '+relative)
 original=p.read_bytes();s=original.decode('utf-8-sig').replace('\r\n','\n')
 for old,new in replacements:
  if old not in s:raise RuntimeError('No matching edit')
  s=s.replace(old,new,1)
 data=s.replace('\n','\r\n').encode('utf-8')
 if original.startswith(b'\xef\xbb\xbf'):data=b'\xef\xbb\xbf'+data
 temp=o/'staging'/(p.name+'.complete');temp.write_bytes(data);os.replace(temp,p)
 d['sha256']=hashlib.sha256(data).hexdigest();d['lines']=len(s.splitlines())
(o/'deliverables.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
print('Updated 2 complete files: exact CR endpoints; anchor override remains authoritative.')
