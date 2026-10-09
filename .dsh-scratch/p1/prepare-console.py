from pathlib import Path
r=Path.cwd();o=r/'.dsh-scratch/p1/console';o.mkdir(exist_ok=True)
files=[r/'Assets/A_UI/UI/Motion/Core/MotionMath.cs',r/'Assets/A_UI/UI/Motion/Core/MotionTypes.cs',r/'Assets/A_UI/UI/Motion/Path/Ease.cs']+[r/'.dsh-scratch/p1/staging'/n for n in ['PathSegment.cs','ArcLengthMath.cs','PathChain.cs','PathBuilder.cs','MotionPathP1Metrics.cs']]
items=''.join('<Compile Include="'+str(p)+'" />' for p in files)
(o/'P1Proof.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><LangVersion>9.0</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>'+items+'<Compile Include="Program.cs"/><Reference Include="UnityEngine.CoreModule"><HintPath>C:/Users/12118/Unity/6000.3.24f1/Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll</HintPath></Reference></ItemGroup></Project>',encoding='utf-8')
(o/'Program.cs').write_text('using System;\nclass Program { static void Main() { MotionPathP1Metrics.Run(Console.WriteLine); } }\n',encoding='utf-8')
