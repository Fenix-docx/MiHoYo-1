from pathlib import Path
import re
root=Path.cwd()
out=root/'.dsh-scratch/codex-review'
files=['docs/ui-motion-module-design.md','Assets/Scripts/GooeyUIGroup.cs','Assets/A_UI/UI/Package/UIbase.cs','Assets/A_UI/UI/Package/Navigation/UINavigator.cs']
import hashlib,json
(out/'evidence/before-hashes.json').write_text(json.dumps({s:hashlib.sha256(Path(s).read_bytes()).hexdigest() for s in files if Path(s).exists()},indent=2))
# 副本的引用也改向副本，防止间接构建写入 Temp。
for name in ['Assembly-CSharp-firstpass','Assembly-CSharp','Assembly-CSharp-Editor']:
 s=Path(name+'.csproj').read_text(encoding='utf-8-sig')
 s=s.replace('Temp\\obj\\$(MSBuildProjectName)', '.dsh-scratch\\codex-review\\build\\obj\\$(MSBuildProjectName)')
 s=s.replace('Temp\\bin\\Debug\\', '.dsh-scratch\\codex-review\\build\\bin\\')
 s=re.sub(r'(<ProjectReference Include="[^"]+)(\.csproj")',r'\1-Verify\2',s)
 Path(name+'-Verify.csproj').write_text(s,encoding='utf-8')
