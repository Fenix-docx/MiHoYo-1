from pathlib import Path
import json,hashlib,difflib,subprocess
r=Path.cwd();o=r/'.dsh-scratch/p1';e=o/'evidence';target=r/'.dsh-scratch/codex-review/p1-report.md'
if target.exists():raise RuntimeError('Report already exists; do not overwrite reviewer artifact')
cmd=['python','.dsh-scratch/p1/verify-build.py']
p=subprocess.run(cmd,cwd=r,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
block='$ '+' '.join(cmd)+'\n'+p.stdout.decode('utf-8',errors='replace')+'\nEXIT_CODE='+str(p.returncode)+'\n'
(e/'compile-access-blocked.txt').write_text(block,encoding='utf-8')
manifest=json.loads((o/'deliverables.json').read_text(encoding='utf-8'));before=json.loads((o/'before.json').read_text(encoding='utf-8'))
roles={'ArcLengthMath.cs':'第二类椭圆积分/Carlson RF、RD，解析圆与椭圆弧长','PathSegment.cs':'扩展 QuadBezier/Arc/CatmullRom，解析求值和单位切线','PathChain.cs':'复制段、C0/G1 拼接、跨段累积弧长 LUT、零分配反查与外推','PathBuilder.cs':'Path.Through 点列 CR 链与 Path.Arc 两点弧高 DSL','MotionTrack.cs':'新增链条轨道；保留单段和动态锚点原分支','MotionPlan.cs':'新增 Curve(PathChain)，Start 最小分支接入','MotionPathAsset.cs':'p 空间点列资产，每次 BuildChain 重新烘焙','MotionGizmoDrawer.cs':'Scene 路径折线、切线箭头、方向指示与 p/世界转换','MotionPathAssetEditor.cs':'Scene 拖点/Shift 点画、Undo、资产写回及父 Rect 预览','MotionPathP1Metrics.cs':'163 项可执行纯数学断言，同时供控制台与 EditMode 使用','MotionPathP1Tests.cs':'四个 Editor 测试入口：数学、真实 Rect、轨道/API、资产 Undo'}
lines=['# UIMotion P1 实现报告','',
'状态：**实现已落盘，P1 纯逻辑 163 项及 P0 纯逻辑 25 项通过；完整 Assembly-CSharp / Assembly-CSharp-Editor 编译尚未执行，交付判据 A/C 尚未满足。** 原因是原 csproj 的依赖 HintPath 指向 Library，而现场要求“严禁碰 Library”。已经请求澄清是否允许编译器只读依赖 DLL；没有以沉默作为许可。下面明确区分已验证与未验证。','',
'没有启动 Unity（包含 batchmode）；没有访问锁文件；没有读写 Library/Temp/ProjectSettings/.plastic；没有运行 git；没有写 .meta；没有修改设计文档、usage、Gooey、UIbase/UIManager 或 Navigation。没有改动验收目录原有工件，本报告是新增文件。','',
'## 1. 交付清单','',
'| 路径 | 行数 | 职责 | 类型 |','|---|---:|---|---|']
for d in manifest:lines.append('| `'+d['path']+'` | '+str(d['lines'])+' | '+roles[Path(d['path']).name]+' | '+('修改' if d['modified'] else '新建')+' |')
lines+=['','附带复跑/证据工件均在 `.dsh-scratch/p1/`：`before.json`、`deliverables.json`、`backup.py`、`prepare-edits.py`、`publish.py`、`refine.py`、`verify-build.py`、`run-proof.py`、`audit.py`、控制台工程及 `evidence/`。`publish.py` 是一次性首次落盘脚本，不应用于复跑；验收复跑仅使用验证命令。完整源码 SHA256 见 `deliverables.json`。','',
'## 2. 与设计文档的对应','',
'| 规格 | 实现落点及验证状态 |','|---|---|',
'| §6.1 段类型 | PathSegment 新增三类，保留 Line=0/CubicBezier=1；unmanaged 泛型约束在控制台真实编译，确保字段没有托管引用。 |',
'| §6.1 拼接 | PathChain 构造时自动对齐真实起点；G1 调整 Bezier 控制柄、CR 虚点；Arc 必要时绕起点旋转。纯逻辑已验证全五种类型、连续两段 Arc 的 C0/G1。 |',
'| §6.1 匀速 | 跨段累计距离/参数/所属段数组，二分及线性插值反查；100 点混合 Cubic+Arc+CR 已执行，std/mean < 2%。 |',
'| §6.2 缓动/外推 | 首起点和末终点切线按总长外推；ClampOutside 可钳制；OutBack 的两侧外推共线、投影长度和钳制均已执行。MotionTrack 原时间纯函数结构保留。 |',
'| §6.3-1 代码 DSL | Path.Through(points) 穿过全点；Path.Arc(from,to,height) 中点弧高精确；已执行数学验证。 |',
'| §6.3-2 编辑器画点 | MotionPathAssetEditor 注册 SceneView.duringSceneGui，PositionHandle 拖点，Shift 左键 ray/plane 画点，Undo.RecordObject，SetPoint/AddPoint，SetDirty 写回；未在 Unity 中交互验收。 |',
'| §12 文件清单 | 文件均位于 Motion/Path、Motion/Editor 和 Assets/Editor，命名空间 UIPackage.Motion（工具为其 Editor 子命名空间）；新增 ArcLengthMath 专门承载椭圆积分。 |',
'| §13-2 连续性测试 | Assets/Editor/MotionPathP1Metrics.cs 中逐交点数值断言，由 MotionPathP1Tests 的数学测试调用；控制台真实运行同一份断言。 |',
'| §14-P1 三分辨率 | 原生 RectTransform 的 Editor 测试设置宽 1920/2560/1440、高 1080、非中心 pivot，调用真实 AnchorResolver.FromP/ToP 对比；未执行。控制台仅验证父高转换数学，不能冒称原生 Rect 测试通过。 |','',
'## 3. 我对既有文件的改动清单 + 修改前 SHA256','',
'所有既有文件先按原始字节备份，再修改；备份 SHA 与 before.json 匹配。发布前验证源文件 SHA 未在备份后发生人工变动；保留原换行和 BOM，完整文件原子替换。','',
'| 既有文件 | 修改前 SHA256 | 备份路径 |','|---|---|---|']
for d in manifest:
 if d['modified']:
  rel=str(Path(d['path']));lines.append('| `'+d['path']+'` | `'+before[rel]+'` | `.dsh-scratch/backups/p1/'+d['path']+'` |')
lines+=['','精确增量 diff：`.dsh-scratch/p1/evidence/changes.diff`。仅以下改动：','',
'- PathSegment：追加枚举成员和值类型参数、三个工厂、解析弧长与旋转、Evaluate/Tangent 的新增 switch 分支；CR 的 t=0/1 直接返回真实端点。旧 Line/Cubic 公式、切线退化回退不变。',
'- MotionPlan：新增 chain 字段、Curve(PathChain)；Curve(PathSegment) 清除旧链引用；Start 选择新增链条构造器，其余继续走原构造器。用户 duration=3f、原注释、From/To 等原成员均保留。',
'- MotionTrack：新增只读 chain 字段和链条构造器，新增段自动单段链化；anchors 为 true 时仍优先使用原动态锚点逻辑；Position 仅增加链条分支。Sample 原时间和差分逻辑未改。','',
'## 4. 我的设计判断与偏离','',
'1. C0 自动吸附采用整段平移，保持后段内部几何形状；CR 真实起点为 p1，不能按 p0 虚点吸附。G1 关闭时仅进行必要的 C0 平移。输入数组先复制，不改调用方原段。',
'2. G1 是方向连续，不声称 C1 速度连续。公共方向用前段起点到后段终点的正向割线（对应文档镜像控制柄思路），原控制柄长度与弦长/6 的较大值用来避免退化；CR 改 p0/p3 虚点。直线/二次曲线先精确升为三次再调整，两端控制柄独立。',
'3. Arc 无自由控制柄。前段是 Arc 时保留其末切线；后段是 Arc 时围绕它的真实起点旋转并重新平移中心，保证 G1/C0，仍是解析椭圆。启用 G1 会改变创作几何（含部分 Arc 的方向），并非保持原图形的插值开关。零长段切线返回零，方向夹角没有数学定义，只保证有限值，不宣称退化处严格 G1。',
'4. Evaluate(u)/Tangent(u) 在 [0,1] 内按段数分配参数；EvaluateByArc(u) 按全链弧长分配。所有越界位置外推统一按总长度及链首尾切线。',
'5. 便捷 Path.Arc 是给定弧高的三次抛物线弧，正弧高在行进方向左侧，midpoint 高度精确；严格圆/椭圆使用 PathSegment.Arc。两者区别写在 API 注释。',
'6. 资产不保留运行时链缓存：BuildChain 总是重新构建。因此任何 Inspector/Undo/脚本改点都会生效；已返回的链是不可变快照，调用者需要再次 BuildChain 才获取新路径。Scene 编辑预览父 Rect 仅存编辑会话，不把场景引用写入项目资产。',
'7. 分辨率比较按父高的一半（项目现有 p 空间定义）归一化；换用父高只会整体多一个相同常数，不影响形状一致性判据。',
'8. 没有把原 MotionMath 扩大成新段统一工具；其 P0 单段帮助方法继续维持旧用途。新增 CR/Arc 的真实端点和解析长度应通过 PathChain/MotionTrack 使用。所有既有单段 Line/Cubic 路径仍使用原 2/257 点 LUT。','',
'## 5. 段类型的参数编码与 LUT 分辨率选择','',
'| 类型 | 参数编码 | 求值/切线 | 长度/反查 |','|---|---|---|',
'| Line | p0 起点、p3 终点 | 原解析式 | 2 点 |',
'| CubicBezier | p0 起点、p1/p2 控制柄、p3 终点 | 原解析式 | 新链 1025 点折线积分；P0 单段仍 257 点 |',
'| QuadBezier | p0 起点、p1 控制点、p3 终点 | 精确二次式/导数 | 1025 点折线积分 |',
'| Arc | center 中心、radius 半径、ellipseScale 两轴缩放、a0/a1 弧度角、rotation 椭圆整体旋转；p0/p3 缓存端点 | center + R(rotation)·radius·(scale.x cos(a), scale.y sin(a))，导数归一化 | 每个累计弧长来自解析积分；1025 点只用于距离→角度的二分插值，不以弦长近似长度 |',
'| CatmullRom | p0/p3 虚点，p1/p2 真实端点 | 标准均匀 CR 多项式及导数 | 1025 点折线积分 |','',
'椭圆弧长是第二类椭圆积分 E(phi,m)，用 Carlson RF/RD 倍增公式求值；不是数值积分或折线 LUT 估长。圆长=r·abs(角差)；零轴用 abs(sin)/abs(cos) 的分段解析原函数；周期规约支持反向与多圈。标准椭圆 a=2,b=1 的周长 9.688448220547676 作独立对照，另用 65536 区间 Simpson 仅在验收代码中作独立参考，不用于生产。','',
'选择 1025 点是为了比 P0 的 257 点提供更细的反查，照顾强曲率及椭圆非均匀角速度；文档 32 点是建议，不能可靠作为 <2% 匀速闸门。每段最坏约 12KB（三个数组每项 4 字节），构造期分配，采样时 0 分配；这是 UI 数十段路径可接受的精度优先取舍，未做 Unity Profiler 性能测量。尖角/自返等极端曲线的等距弦长仍可能不匀，不能用加密 LUT 消除弧长与弦长本身的差别。','',
'## 6. 验证证据','',
'### 6.1 编译（命令 + 原始输出 + 编译范围反证）','',
'**完整程序集编译未执行，新资产/编辑器代码尚未有完整编译通过证据。** 验证脚本在读取任何 Library 依赖前退出；其原始输出：','',
'```text',block.rstrip(),'```','',
'原因和出处：p1-brief.md §0.1 及用户现场约束明确“严禁碰 Library/ Temp/ ProjectSettings/ .plastic/”。原 Assembly-CSharp*.csproj 的 HintPath 指向 Library/ScriptAssemblies 与 Library/PackageCache。为了不擅自把“严禁碰”缩窄成“仅禁止写”，向用户请求了是否允许编译器只读依赖的澄清。截止此报告，没有确认，所以不能执行完整构建。','',
'已准备但**未执行**的完整复跑入口（仅在允许只读 DLL 后使用）：','',
'```powershell','python .dsh-scratch/p1/verify-build.py --allow-library-read','```','',
'该脚本将生成 `.dsh-scratch/p1/projects/Assembly-CSharp*-Verify.csproj` 完整副本，绝对化原 Compile/HintPath，追加全部 P1 源文件，修改 firstpass/runtime/editor 的 ProjectReference 为 Verify 副本，把 BaseIntermediateOutputPath/IntermediateOutputPath/OutputPath 全部改到 scratch；任何受限路径出现在非 HintPath 项时拒绝执行。每个 P1 文件的 #error 只放在 `.dsh-scratch/p1/probes/`，按编译项替换；必须看到逐文件 P1_COMPILE_PROBE_* 诊断，finally 恢复 csproj 后再做最终正向 build。输出预期保存为 evidence/runtime-probe.txt、runtime-final.txt、editor-probe.txt、editor-final.txt。**这些探针与最终构建日志目前尚不存在，不可当成已取得的证据。**','',
'已验证的 C# 9 编译范围仅是控制台的实际引用源：MotionMath、MotionTypes、Ease、PathSegment、ArcLengthMath、PathChain、PathBuilder、MotionPathP1Metrics 和 Program；以下真实 run 经过 dotnet 重新编译。它不替代两个完整程序集构建。','',
'### 6.2 运行（命令 + 原始输出）','',
'可复跑（不启动 Unity、不依赖 Library）：','',
'```powershell','python .dsh-scratch/p1/run-proof.py','python .dsh-scratch/p1/audit.py','```','',
'P1 原始输出（同 evidence/p1-console.txt）：','',
'```text',(e/'p1-console.txt').read_text(encoding='utf-8').rstrip(),'```','',
'P0 原始输出（同 evidence/p0-console.txt；保留原 25 项断言，仅修改最终说明行以避免旧“PathChain 未实现”文案失真）：','',
'```text',(e/'p0-console.txt').read_text(encoding='utf-8').rstrip(),'```','',
'范围审计：','',
'```text','AUDIT expected_modified=3 unchanged=37 unexpected=0 Assets_error_probes=0','```','',
'完整审计名单见 evidence/scope-audit.json。审计不打开受限目录、不调用 git；“没有写 .meta/启动 Unity”是本轮操作记录声明，不意味着证明用户 Editor 没有自行导入生成文件。','',
'### 6.3 Editor 测试（已写 / 未编译完整程序集 / 未执行）','',
'**已写；尚未随 Assembly-CSharp-Editor 完整编译；未在 Unity 中执行。** 不能按简报要求写成“已随 Assembly-CSharp-Editor 编译通过”，因为尚未取得该证据。','',
'- ChainContinuityUniformityAndSegmentMath：同一份 163 项数值断言。',
'- ThreeRectResolutionsNormalizeToSameShape：真实 RectTransform 三尺寸、非中心 pivot，100 点 <1e-3。',
'- TrackOvershootClampSeekAndPlanCompatibility：真实 MotionTrack/RectBinder、OutBack、Clamp、重复 Sample、Curve(chain)→Curve(segment) 与 From/To 回归。',
'- AssetRebuildAndUndoDoNotReturnStaleChains：真实 ScriptableObject、旧快照不变、更新后构建、Undo 后重建。','',
'P0 的 MotionTests.cs 和 UINavigationQueueTests.cs SHA 未变，原断言完全保留。既有 24 个 Unity 测试需要验收方在用户关 Editor 后执行，本轮没有执行。','',
'## 7. 已知问题与未实现','',
'- 验收 A/C 的完整编译与逐文件错误探针反证尚缺；没有宣布 P1 完成验收。脚本已准备，需澄清 Library 依赖的只读边界后复跑；若允许并出现编译问题，需要继续修复并更新证据。',
'- Scene 实际可视化/拖点/Shift 点画/Undo 的交互没有在 Unity 手工验证。源码已实现事件订阅、手柄、Undo、写回，不能把源码审查写作交互“已通过”。',
'- 原生 RectTransform、资产 Undo、MotionPlan 接入与 P0 24 项 Unity 回归只写断言，未执行。',
'- 没有承诺任意尖角、自返曲线的等时间弦长都满足 2%；此轮混合平滑链真实数值达标。极端曲率、超多圈弧与极端坐标需要后续专项测试。',
'- 本轮不涉及 SdfContour、FromSpriteContour/FromRect/FromSdfField、导航、跨父 Reparent、Timeline、性能 Profiler。','',
'## 8. 与其他系统的接触面','',
'确实触碰了今天用户改过的 MotionPlan.cs 和 Tracks/MotionTrack.cs；保留用户手改 duration=3f 与原注释、空白、成员名称。UIPanelMotion.cs（用户今天 13:57:29 的改动）以及 AnchorResolver/Core/Integration 其他既有文件 SHA 全部未变。','',
'精确改动行（以本轮最终文件计，参见 changes.diff）：','']
for d in manifest:
 if d['modified']:
  original=(r/'.dsh-scratch/backups/p1'/d['path']).read_text(encoding='utf-8-sig').splitlines()
  current=(r/d['path']).read_text(encoding='utf-8-sig').splitlines()
  op=difflib.SequenceMatcher(a=original,b=current)
  ranges=[]
  for tag,a,b,c,f in op.get_opcodes():
   if tag!='equal':ranges.append(('旧 '+str(a+1)+'–'+str(b)+' → 新 '+str(c+1)+'–'+str(f)))
  lines.append('- `'+d['path']+'`：'+'；'.join(ranges)+'。')
lines+=['','与现有 UI 其他包的接触仅是原 MotionPlan→MotionSystem.Register、MotionTrack→AnchorResolver/RectBinder 的既有调用；没有改变 UIBase、UIManager、Navigation、Gooey 或设计/usage 文档。','']
target.write_text('\n'.join(lines),encoding='utf-8')
print('WROTE '+str(target.relative_to(r))+' lines='+str(len(lines))+'; status=implementation+pure-proof complete, full compilation pending clarification')
