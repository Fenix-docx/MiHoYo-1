"""在项目根生成显式编译副本，所有输出重定向到 scratch；不启动 Unity。"""
from pathlib import Path
import re, subprocess, hashlib, json, sys
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'.dsh-scratch/codex-repair-round2'
EVIDENCE=OUT/'evidence'
RUNTIME=sorted((ROOT/'Assets/A_UI/UI/Motion').rglob('*.cs'))+[
 ROOT/'Assets/A_UI/UI/Package/Navigation/UINavigationQueue.cs',
 ROOT/'Assets/A_UI/UI/Package/UIbase.cs',ROOT/'Assets/A_UI/UI/Package/Navigation/UINavigator.cs']
EDITOR=[ROOT/'Assets/Editor/MotionTests.cs',ROOT/'Assets/Editor/UINavigationQueueTests.cs']
names=['Assembly-CSharp-firstpass','Assembly-CSharp','Assembly-CSharp-Editor']
copies=[ROOT/(name+'-RepairRound2.csproj') for name in names]

def prepare():
 for name, dest in zip(names,copies):
  text=(ROOT/(name+'.csproj')).read_text(encoding='utf-8-sig')
  text=text.replace('Temp\\obj\\$(MSBuildProjectName)', '.dsh-scratch\\codex-repair-round2\\build\\obj\\$(MSBuildProjectName)')
  text=text.replace('Temp\\bin\\Debug\\', '.dsh-scratch\\codex-repair-round2\\build\\bin\\')
  text=re.sub(r'(<ProjectReference Include="[^"]+)(\.csproj")',r'\1-RepairRound2\2',text)
  sources=RUNTIME if name=='Assembly-CSharp' else EDITOR if name=='Assembly-CSharp-Editor' else []
  included={x.replace('\\','/').lower() for x in re.findall(r'<Compile Include="([^"]+)"',text)}
  extra=''.join('<Compile Include="'+str(p.relative_to(ROOT))+'" />\n' for p in sources if p.relative_to(ROOT).as_posix().lower() not in included)
  text=text.replace('</Project>','<ItemGroup>'+extra+'</ItemGroup></Project>')
  if 'Temp\\' in text: raise RuntimeError('验证项目中仍存在 Temp 路径')
  dest.write_text(text,encoding='utf-8')

def build(project,log,expected):
 cmd=['dotnet','build',project.name,'--nologo','-v','m','-m:1','-p:BuildInParallel=false','-p:RestoreDisableParallel=true']
 result=subprocess.run(cmd,cwd=ROOT,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
 output=result.stdout.decode('utf-8',errors='replace')
 transcript='$ '+' '.join(cmd)+'\n'+output+'\nEXIT_CODE='+str(result.returncode)+'\n'
 (EVIDENCE/log).write_text(transcript,encoding='utf-8')
 print(transcript,flush=True)
 if (result.returncode==0)!=expected: raise RuntimeError('非预期构建结果: '+log)
 return output

def probe(project,sources,log):
 original=project.read_text(encoding='utf-8');changed=original
 probe_dir=OUT/'probes';probe_dir.mkdir(exist_ok=True)
 for p in sources:
  copy=probe_dir/p.name
  # 错误只注入 scratch 副本，避免正在运行的 Unity 自动导入错误源码。
  copy.write_bytes(('#error UIMOTION_COMPILE_PROBE_'+p.stem+'\n').encode()+p.read_bytes())
  old=str(p.relative_to(ROOT))
  changed=re.sub(r'(<Compile Include=")'+re.escape(old)+r'(")',lambda m:m[1]+str(copy.relative_to(ROOT))+m[2],changed)
  if str(copy.relative_to(ROOT)) not in changed: raise RuntimeError('没有替换编译项: '+old)
 try:
  project.write_text(changed,encoding='utf-8')
  output=build(project,log,False)
  for p in sources:
   if 'UIMOTION_COMPILE_PROBE_'+p.stem not in output: raise RuntimeError('缺少文件编译证据: '+p.name)
 finally: project.write_text(original,encoding='utf-8')

try:
 if any(p.exists() for p in copies): raise RuntimeError('Verification project already exists')
 prepare()
 manifest=[{'source':str(p.relative_to(ROOT)),'sha256':hashlib.sha256(p.read_bytes()).hexdigest()} for p in RUNTIME+EDITOR]
 (EVIDENCE/'compiled-sources.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
 probe(copies[1],RUNTIME,'runtime-probe-failed.txt')
 build(copies[1],'runtime-final-passed.txt',True)
 probe(copies[2],EDITOR,'editor-probe-failed.txt')
 build(copies[2],'editor-final-passed.txt',True)
finally:
 for p in copies:
  if p.parent.resolve()!=ROOT.resolve(): raise RuntimeError('清理目标越界')
  if p.exists(): p.unlink()
