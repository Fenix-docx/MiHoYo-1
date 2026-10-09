from pathlib import Path
import subprocess
r=Path.cwd();o=r/'.dsh-scratch/p1'
project=o/'console/P1Proof.csproj'
s=project.read_text(encoding='utf-8')
for n in ['PathSegment.cs','ArcLengthMath.cs','PathChain.cs','PathBuilder.cs']:
 s=s.replace(str(o/'staging'/n),str(r/'Assets/A_UI/UI/Motion/Path'/n))
s=s.replace(str(o/'staging/MotionPathP1Metrics.cs'),str(r/'Assets/Editor/MotionPathP1Metrics.cs'))
project.write_text(s,encoding='utf-8')
# 保留 P0 的 25 项纯逻辑回归代码，不修改其验收断言。
p0=o/'p0-console';p0.mkdir(exist_ok=True)
s=(r/'.dsh-scratch/codex-repair-round2/console/MotionProof.csproj').read_text(encoding='utf-8')
s=s.replace('<Compile Include="Program.cs" />','<Compile Include="Program.cs" /><Compile Include="../../../Assets/A_UI/UI/Motion/Path/ArcLengthMath.cs" />')
(p0/'MotionProof.csproj').write_text(s,encoding='utf-8')
s=(r/'.dsh-scratch/codex-repair-round2/console/Program.cs').read_text(encoding='utf-8')
s=s.replace('PathChain is P1, not implemented.','P0 regression only; PathChain numerical checks run separately.')
(p0/'Program.cs').write_text(s,encoding='utf-8')
for name,p in [('p1-console',project),('p0-console',p0/'MotionProof.csproj')]:
 cmd=['dotnet','run','--project',str(p.relative_to(r)),'--nologo']
 result=subprocess.run(cmd,cwd=r,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
 text='$ '+' '.join(cmd)+'\n'+result.stdout.decode('utf-8',errors='replace')+'\nEXIT_CODE='+str(result.returncode)+'\n'
 (o/'evidence'/(name+'.txt')).write_text(text,encoding='utf-8');print(text,flush=True)
 if result.returncode:raise RuntimeError(name+' failed')
