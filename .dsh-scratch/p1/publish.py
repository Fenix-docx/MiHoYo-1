from pathlib import Path
import hashlib,json,os
r=Path.cwd();o=r/'.dsh-scratch/p1';before=json.loads((o/'before.json').read_text(encoding='utf-8'))
paths={
 'ArcLengthMath.cs':'Assets/A_UI/UI/Motion/Path/ArcLengthMath.cs',
 'PathSegment.cs':'Assets/A_UI/UI/Motion/Path/PathSegment.cs',
 'PathChain.cs':'Assets/A_UI/UI/Motion/Path/PathChain.cs',
 'PathBuilder.cs':'Assets/A_UI/UI/Motion/Path/PathBuilder.cs',
 'MotionTrack.cs':'Assets/A_UI/UI/Motion/Tracks/MotionTrack.cs',
 'MotionPlan.cs':'Assets/A_UI/UI/Motion/MotionPlan.cs',
 'MotionPathAsset.cs':'Assets/A_UI/UI/Motion/Path/MotionPathAsset.cs',
 'MotionGizmoDrawer.cs':'Assets/A_UI/UI/Motion/Editor/MotionGizmoDrawer.cs',
 'MotionPathAssetEditor.cs':'Assets/A_UI/UI/Motion/Editor/MotionPathAssetEditor.cs',
 'MotionPathP1Metrics.cs':'Assets/Editor/MotionPathP1Metrics.cs',
 'MotionPathP1Tests.cs':'Assets/Editor/MotionPathP1Tests.cs'}
# 所有目标先检查，已有文件只接受备份时的字节；不覆盖后来发生的人工编辑。
for filename,relative in paths.items():
 p=r/relative; key=str(p.relative_to(r))
 if p.exists() and (key not in before or hashlib.sha256(p.read_bytes()).hexdigest()!=before[key]):
  raise RuntimeError('Concurrent edit or unexpected existing target: '+relative)
manifest=[]
for filename,relative in paths.items():
 p=r/relative;existing=p.exists();old=p.read_bytes() if existing else b''
 data=(o/'staging'/filename).read_text(encoding='utf-8').replace('\r\n','\n')
 # 保留既有文件的换行与 BOM；新文件使用本目录的 CRLF。
 if not existing or b'\r\n' in old:data=data.replace('\n','\r\n')
 raw=data.encode('utf-8');raw=(b'\xef\xbb\xbf'+raw) if old.startswith(b'\xef\xbb\xbf') else raw
 staged=o/'staging'/(filename+'.complete');staged.write_bytes(raw)
 p.parent.mkdir(parents=True,exist_ok=True)
 if existing and hashlib.sha256(p.read_bytes()).hexdigest()!=before[str(p.relative_to(r))]: raise RuntimeError('Late concurrent edit: '+relative)
 os.replace(staged,p) # 完整文件原子替换；没有把半成品或错误探针写入 Assets。
 manifest.append({'path':relative,'modified':existing,'lines':len(data.splitlines()),'sha256':hashlib.sha256(raw).hexdigest()})
(o/'deliverables.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
print('PUBLISHED complete files='+str(len(manifest))+' existing=3 new='+str(len(manifest)-3))
