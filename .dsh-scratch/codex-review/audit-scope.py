from pathlib import Path
import hashlib,json,re,difflib
root=Path.cwd(); out=root/'.dsh-scratch/codex-review'; lines=[]
manifest=json.loads((out/'evidence/compiled-sources.json').read_text(encoding='utf-8'))
for item in manifest:
 p=root/item['source']; assert hashlib.sha256(p.read_bytes()).hexdigest()==item['sha256'],p
lines.append('COMPILED_SOURCE_HASHES: 18/18 match final source')
before=json.loads((out/'evidence/before-hashes.json').read_text())
for name in ['docs/ui-motion-module-design.md','Assets/Scripts/GooeyUIGroup.cs']:
 assert hashlib.sha256((root/name).read_bytes()).hexdigest()==before[name],name
 lines.append('UNCHANGED_SHA256: '+name+' '+before[name])
for oldname,newname in [('UIbase.before.cs','Assets/A_UI/UI/Package/UIbase.cs'),('UINavigator.before.cs','Assets/A_UI/UI/Package/Navigation/UINavigator.cs')]:
 old=(out/'evidence'/oldname).read_text();new=(root/newname).read_text()
 # 保留原有 public 方法及属性/事件名字；另外由编译器检查所有现有调用者。
 methods=re.findall(r'public\s+(?:static\s+)?[\w<>, ]+\s+(\w+)\(([^)]*)\)',old)
 for method,args in methods:
  assert re.search(r'public\s+(?:static\s+)?[\w<>, ]+\s+'+method+r'\('+re.escape(args)+r'\)',new),(method,args)
 lines.append('EXISTING_PUBLIC_METHOD_SIGNATURES: '+newname+' '+str(len(methods))+' retained')
 for line in difflib.unified_diff(old.splitlines(),new.splitlines()):
  if line.startswith('+') and not line.startswith('+++'): assert line==line.rstrip(),newname
for item in manifest:
 p=root/item['source']
 if p.name not in ['UIbase.cs','UINavigator.cs']:
  assert all(line==line.rstrip() for line in p.read_text().splitlines()),p
lines.append('ADDED_LINE_WHITESPACE: PASS (Python source comparison; no git invoked)')
assert not list(root.glob('*-Verify.csproj'))
lines.append('TEMP_ROOT_VERIFY_PROJECTS: removed')
(out/'evidence/scope-audit.txt').write_text('\n'.join(lines)+'\n',encoding='utf-8')
print('\n'.join(lines))
