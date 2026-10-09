"""完整程序集验证；只读现有源码/引用，所有生成物与错误探针都留在 scratch。"""
from pathlib import Path
import xml.etree.ElementTree as ET
import subprocess,sys,json,hashlib,re
R=Path(__file__).resolve().parents[2];O=R/'.dsh-scratch/p1';E=O/'evidence';P=O/'projects'
# 原工程引用了 Library 中的 DLL；只有现场明确允许只读依赖时才执行。
if '--allow-library-read' not in sys.argv:
 raise SystemExit('BLOCKED: full csproj references Library DLLs; no access attempted. Use --allow-library-read only after user authorizes compiler read-only references.')
P.mkdir(exist_ok=True);(O/'probes').mkdir(exist_ok=True)
names=['Assembly-CSharp-firstpass','Assembly-CSharp','Assembly-CSharp-Editor']
copies={n:P/(n+'-Verify.csproj') for n in names}
files=json.loads((O/'deliverables.json').read_text(encoding='utf-8'))
source_manifest=[]
for name in names:
 tree=ET.parse(R/(name+'.csproj'));root=tree.getroot()
 for elem in root.iter():
  if elem.tag in ('BaseIntermediateOutputPath','IntermediateOutputPath'):elem.text=str(O/'build/obj'/name)+'/'
  elif elem.tag=='OutputPath':elem.text=str(O/'build/bin')+'/'
  elif elem.tag=='ProjectReference':
   elem.set('Include',str(copies[Path(elem.attrib['Include'].replace('\\','/')).stem]))
  elif elem.tag in ('Compile','Analyzer','None','Content') and 'Include' in elem.attrib:
   path=Path(elem.attrib['Include'].replace('\\','/'))
   if not path.is_absolute():elem.set('Include',str(R/path))
  elif elem.tag=='HintPath':
   path=Path(elem.text.replace('\\','/'))
   if not path.is_absolute():elem.text=str(R/path)
 included={x.get('Include','').replace('\\','/').lower() for x in root.iter('Compile')}
 sources=[R/d['path'] for d in files if (('/Editor/' in d['path'])==(name=='Assembly-CSharp-Editor'))] if name!='Assembly-CSharp-firstpass' else []
 group=ET.SubElement(root,'ItemGroup')
 for path in sources:
  if path.as_posix().lower() not in included:ET.SubElement(group,'Compile',{'Include':str(path)})
 # 不允许写入任何受限路径。HintPath 的 Library 只读输入由命令行明确许可。
 for elem in root.iter():
  if elem.tag!='HintPath':
   values=[elem.text or '']+list(elem.attrib.values())
   if any(re.search(r'(?:^|[\\/])(Library|Temp|ProjectSettings|\.plastic)(?:[\\/]|$)',v,re.I) for v in values):
    raise RuntimeError('Forbidden path outside dependency HintPath: '+str(values))
 copies[name].write_text(ET.tostring(root,encoding='unicode'),encoding='utf-8')
 source_manifest.extend({'assembly':name,'source':x.get('Include'),'sha256':hashlib.sha256(Path(x.get('Include')).read_bytes()).hexdigest()} for x in root.iter('Compile'))
(E/'compiled-sources.json').write_text(json.dumps(source_manifest,ensure_ascii=False,indent=2),encoding='utf-8')
def build(name,log,expected):
 cmd=['dotnet','build',str(copies[name].relative_to(R)),'--nologo','-v','m','-m:1','-p:BuildInParallel=false','-p:RestoreDisableParallel=true']
 process=subprocess.run(cmd,cwd=R,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
 raw=process.stdout.decode('utf-8',errors='replace')
 transcript='$ '+' '.join(cmd)+'\n'+raw+'\nEXIT_CODE='+str(process.returncode)+'\n'
 (E/log).write_text(transcript,encoding='utf-8');print(transcript,flush=True)
 if (process.returncode==0)!=expected:raise RuntimeError('Unexpected compilation result: '+log)
 return raw
for name in names[1:]:
 original=copies[name].read_bytes();tree=ET.fromstring(original)
 tokens=[]
 for d in files:
  if ('/Editor/' in d['path'])!=(name=='Assembly-CSharp-Editor'):continue
  source=R/d['path'];token='P1_COMPILE_PROBE_'+source.stem;tokens.append(token)
  probe=O/'probes'/source.name
  # 先去掉可能的 BOM，避免 BOM 被 #error 前缀移入文件内部。
  probe.write_text('#error '+token+'\n'+source.read_text(encoding='utf-8-sig'),encoding='utf-8')
  found=False
  for elem in tree.iter('Compile'):
   if elem.get('Include','').replace('\\','/').lower()==source.as_posix().lower():elem.set('Include',str(probe));found=True
  if not found:raise RuntimeError('No compile item: '+str(source))
 try:
  copies[name].write_text(ET.tostring(tree,encoding='unicode'),encoding='utf-8')
  raw=build(name,('editor' if name.endswith('Editor') else 'runtime')+'-probe.txt',False)
  for token in tokens:
   if token not in raw:raise RuntimeError('Missing probe diagnostic: '+token)
 finally:copies[name].write_bytes(original)
 build(name,('editor' if name.endswith('Editor') else 'runtime')+'-final.txt',True)
print('RESULT both full assemblies compile; every P1 source has #error negative evidence; probes only in scratch.')
