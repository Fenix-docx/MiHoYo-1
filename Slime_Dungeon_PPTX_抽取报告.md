# Group1_Team1_Slime_Dungeon.pptx — 全文抽取报告

> 只读分析，未解压整包、未修改/删除任何文件。全部通过 `System.IO.Compression.ZipFile.OpenRead` 流式读取指定的 XML 条目，以及媒体条目的文件头/尾部字节。

---

## 0. 容器事实（先给结论）

| 项目 | 结果 |
|---|---|
| 文件 | `C:\Users\12118\AppData\Local\Temp\dsh-import-session-ee1436da-4cf2-4a88-90c2-00a1a51e3dc9\Group1_Team1_Slime_Dungeon.pptx` |
| 大小 | 463,936,277 B（约 442.5 MiB），最后修改 2026/7/8 21:19:45 |
| ZIP 条目 | 192 个；其中 `ppt/media/` 63 个条目 = 463,620,136 B（442.1 MB），**占整包 99.93%** |
| 幻灯片 | 22 页（`ppt/slides/slide1..22.xml`），与 `p:sldIdLst` 22 项一致 |
| 备注页 | 21 个（`notesSlide1..21.xml`），映射关系：**notesSlide N → 第 N+1 页**；**第 1 页无备注** |
| 画布尺寸 | `18288000 × 10287000` EMU = **20.00″ × 11.25″**（非默认 13.333″×7.5″） |
| 版式 | 22 页**全部**使用 `slideLayout7`（name=`Blank`, type=`blank`）；该版式自带日期域 `7/4/2026` 与页码域 `‹#›` |
| SmartArt / 图示 | ❌ 不存在 `ppt/diagrams/`；`[Content_Types].xml` 无 diagram/chart/oleObject/embeddings 声明；ZIP 内无 `diagram*`/`chart*`/`embeddings*` 条目 |
| 节 / 自定义放映 | ❌ `presentation.xml` 无 `<p:sectLst>`、无 `<p:custShowLst>`；`presentation.xml.rels` 无 customShow 关系；仅有 2 条绘图参考线 guide |
| 超链接 | ❌ 全部 rels 中无 `TargetMode="External"` |
| 嵌入字体 | 3 个字体族：可画轻中宋-简、等线、可画轻中宋-简 Bold（`ppt/fonts/font1..4.fntdata`，共 181,998 B；`embedTrueTypeFonts="1" saveSubsetFonts="1"`） |
| docProps | title=黑色高级极简个人作品集演示文稿；creator=崔诚明；lastModifiedBy=CHENGMING CUI；revision=9；created=2006-08-16；modified=2026-07-04T08:32:22Z |
| app.xml | Slides=22, Notes=21, HiddenSlides=0, MMClips=7, Words=2363, Paragraphs=234, TotalTime=84, Application=Microsoft Office PowerPoint, AppVersion=16.0000 |
| custom.xml | `AIGC` = `{"Label":"1",...}`（**AIGC 内容标识合规字段**）、`ICV`、`KSOProductBuildVer = 2052-12.1.0.26895`（说明曾被 WPS 保存过） |

---

## 1. 逐页文字（按阅读顺序）

> 说明：`a:t` 是「文本 run」，一段（paragraph）内可能被拆成多个 run；下面已把同一段的 run 合并还原成自然句子。页码 = 放映顺序。

### 【第 1 页】封面 — GAME JAM PORTFOLIO | SLIME DUNGEON
```
GAME JAM PORTFOLIO
SLIME DUNGEON
CLASS1, GROUP1
ART: CHEN JIAHAO 2408887
DESIGNER: CUI CHENGMING 2408863, QU YIXUAN 2408932, WANG XIYUE 2408900
PROGRAMER: LIN MINGXU 2408947, LU YITONG 2408895
```
- 图片：`image1.png`（1204×702）以 freeform 图形填充，位于 (10.48″,2.34″) 尺寸 8.82″×7.91″
- 备注页：**无**

### 【第 2 页】YOU'RE AWAKE... YOU'RE A SLIME NOW...
```
YOU'RE AWAKE...
 YOU'RE A SLIME NOW...
```
- 图片：`image2.png`（474×355）以 freeform 填充，位于 (5.59″,1.88″) 尺寸 8.83″×6.61″
- 备注（notesSlide1）：
```
You wake up, in a dark dark place
But you are not a hero with a sword or armour.
You are now a slime.
In our game, your body is not just your character—it is the tool you use to survive, explore, and solve the dungeon
```

### 【第 3 页】ONE-SENTENSE PITCH
```
ONE-SENTENSE PITCH
A PHYSICS-BASED PUZZLE GAME WHERE PLAYERS TRANSFORM ONE SLIME INTO DIFFERENT CONTROLLABLE FORMS TO ESCAPE A RUINED DUNGEON.

EXPLORE
MOVE THROUGH A DUNGEON BUILT AROUND UNEVEN TERRAIN, GATES, AND PHYSICAL OBSTACLES.

TRANSFORM
USE SLIME-SPECIFIC BEHAVIOURS: BOUNCE, SOFTEN, SPLIT, AND SWITCH CONTROL.

SOLVE
TRIGGER MECHANISMS AND OPEN PATHS BY COMBINING DIFFERENT SLIME STATES.
```
- 图片：`image3.png`/`image4.png`（均 808×392，左右两张卡片底图）+ `image5.gif`（1138×640 动画）+ `image6.gif`（1024×640 动画）
- 备注（notesSlide2）：
```
Welcome to Slime Dungeon. Our project is a 3D physics-based dungeon puzzle game.
Players control a slime that can move, bounce, split into smaller forms, and interact with dungeon mechanisms to find a way out.
Our target audience is players who enjoy accessible puzzle games, and physics-based movement.
We wanted the controls to remain simple, while making the player's body itself the main puzzle-solving tool.
Instead of giving players many buttons or weapons, we ask one central question:
What form should the slime become to move forward?
```

### 【第 4 页】TRANSFORMATION REASONS
```
TRANSFORMATION REASONS

STATE SHIFTS
LARGE SLIME ↔ SMALL SLIME
ONE BODY ↔ SPLIT BODIES
SOFT MOVEMENT ↔ PUZZLE INTERACTION

DESIGN MEANING
EACH OBSTACLE ASKS THE PLAYER TO CHOOSE A BODY STATE, NOT SIMPLY PRESS A NEW ABILITY BUTTON.

PLAYER EXPERIENCE
THE DUNGEON IS UNDERSTOOD THROUGH CHANGES IN SIZE, CONTROL, AND PHYSICAL RESPONSE.
```
- 图片：`image7.gif`（1138×640 动画，98 帧）1 张，位于 (3.97″,1.08″)，尺寸 11.85″×6.67″
- 备注（notesSlide3）：
```
We selected the Game Jam theme of Transformation. In our game, transformation is not only a visual change. It directly affects gameplay.
The player shifts between being large and small, solid and flexible, one body and multiple bodies.
Each transformation changes what the player can reach, activate, or pass through.
```

### 【第 5 页】TRANSFORMATION REASONS（第 2 版）
文字与第 4 页**逐字相同**（见 §3 对比结论）：
```
TRANSFORMATION REASONS
STATE SHIFTS
LARGE SLIME ↔ SMALL SLIME
ONE BODY ↔ SPLIT BODIES
SOFT MOVEMENT ↔ PUZZLE INTERACTION
DESIGN MEANING
EACH OBSTACLE ASKS THE PLAYER TO CHOOSE A BODY STATE, NOT SIMPLY PRESS A NEW ABILITY BUTTON.
PLAYER EXPERIENCE
THE DUNGEON IS UNDERSTOOD THROUGH CHANGES IN SIZE, CONTROL, AND PHYSICAL RESPONSE.
```
- 图片：`image7.gif`（同上，位于 1.82″,2.96″，尺寸 7.77″×4.37″）**＋ 额外一张 `image8.png`（1153×559）**，位于 (10.41″,2.90″)，尺寸 7.92″×4.34″
- 备注（notesSlide4，与第 4 页备注不同）：
```
Our main design contrast is simple: the dungeon is hard, ancient, and restrictive, while the slime is soft, flexible, and adaptable.
Every obstacle is designed to create a small decision point.
Players need to observe the environment, understand the obstacle, and decide which slime form is most useful.
This makes transformation the core of both the theme and the gameplay loop.
I will now pass to [lmx], who will explain how we built the slime's core movement.
```

### 【第 6 页】CORE GAMEPLAY LOOP
```
CORE GAMEPLAY LOOP

1 OBSERVE OBSTACLE
2 CHOOSE SLIME STATE
3 USE PHYSICS ABILITY
4 TRIGGER MECHANISM
5 REACH NEXT SECTION

WHY THIS LOOP WORKS?
IT GIVES PLAYERS A CLEAR RHYTHM: READ THE ROOM, TRANSFORM THE SLIME, TEST THE INTERACTION, THEN MOVE FORWARD.
```
- 图片：`image9.png`（282×320）重复使用 5 次作为 5 个流程节点图标（同一条 blip 关系 rId3）
- 备注（notesSlide5）：
```
Our core gameplay loop is: first, the player observes an obstacle; then they choose a suitable slime form or movement action;
after that, they use the slime's physical abilities to pass the obstacle or activate a mechanism;
finally, they unlock the next part of the dungeon.
We kept this loop focused so that each mechanic supports the same overall experience.
```

### 【第 7 页】PHYSIC PROTOTYPE
```
PHYSIC PROTOTYPE
SPLIT
SOFT
BOUNCE
ROLL
SPLIT AND MERGE
PROTOTYPE FOCUS: CONTROLLABLE PHYSICAL FEEDBACK RATHER THAN FULL SIMULATION COMPLEXITY.
```
- 图片（全部为动画 GIF，一眼可见是游戏内录屏）：
  - `image12.gif`（1138×640，54 帧）→ SPLIT，位于 0.66″,1.79″，5.05″×2.84″
  - `image11.gif`（1138×640，43.8 MB）→ SOFT，位于 5.17″,1.79″
  - `image10.gif`（1138×640，44 帧）→ BOUNCE，位于 9.38″,1.79″
  - `image5.gif`（1138×640，67 帧）→ ROLL，位于 14.43″,1.79″
  - `image7.gif`（1138×640，98 帧）→ SPLIT AND MERGE，位于 1.42″,6.16″，7.55″×4.25″
  - 另有 1 个 `Group 4`（10.28″,6.39″，内部无图片）
- 备注（notesSlide6）：
```
The first technical priority was proving that the slime could feel physical and responsive.
Our prototype already supports movement, bouncing, and splitting.
We focused on making the slime react clearly to terrain, slopes, collisions, and jumps.
Rather than aiming for a fully realistic fluid simulation, we prioritised readable and controllable movement for gameplay.
```

### 【第 8 页】TECHNICAL DECISIONS & TUNING ★核心页
- **表格结构**：页面上有 1 个 `graphicFrame / Table 4`，位于 (1.13″,1.25″)，尺寸 17.75″×9.39″，网格为 **4 行 × 3 列**（col=6.27″/5.74″/5.74″，每行高 2.35″）。**12 个单元格全部为空**（`<a:t>` 计数 = 0，只有空 `<a:p>`）。真正的文字位于**叠加在表格上的独立文本框**。
- 表头文本框：`PROBLEM`(2.24″,1.99″)、`DECISION`(9.28″,2.06″)、`RESULT`(15.43″,1.99″)
- 还原后的三行（依据见 §4）：

| # | PROBLEM | DECISION | RESULT |
|---|---|---|---|
| 1 | Inaccurate density field calculation (hole breaking problem) | Increase particle count, anisotropic kernel function (stretch kernel along surface tangential direction) | Fix hole artifacts with higher cost |
| 2 | High computational cost of Marching Cubes（especially in spliting) | GPU ComputeShader and Coarse-grained culling | Significantly smoother computation with lower overhead |
| 3 | Dual displacement of main body and clones | Core-Perimeter Hierarchical Driving (MoveCoreRadius) | Only drive the blob core; peripheral particles are passively dragged via PBF density constraints, preventing all particles from moving at the same speed simultaneously) |

- 媒体：
  - `image13.png`（462×264）freeform 填充，位于 (1.32″,4.11″)
  - **视频对象 1**：`media1.mp4`（23,565 B，**3.40 s，426×240**），海报帧 `image14.jpeg`（426×240），位于 (1.25″,6.46″)，2.13″×1.20″
  - **视频对象 2**：`media2.mp4`（42,817 B，**3.20 s，426×240**），海报帧 `image15.jpeg`（426×240），位于 (1.25″,8.91″)，2.01″×1.14″
- 备注（notesSlide7，**直接印证了三行映射**）：
```
This slide shows three technical decisions we made to improve the slime simulation.
Firstly, we inaccurate density-field calculation created visible holes in the slime surface.
Secondly, Marching Cubes became expensive, especially when the slime split. We moved this process to a
GPU Compute Shader and added coarse-grained culling to keep the simulation smoother.
Finally, we used a core-perimeter hierarchical driving system with PBF density constraints, so the slime body
moves more naturally instead of every particle being driven at the same speed.
```

### 【第 9 页】CONTROL SWITCHING & PUZZLE LOGIC
```
CONTROL SWITCHING & PUZZLE LOGIC

BIG SLIME STOPS AT GATE
↓
SWITCH TO SMALL SLIME
↓
SMALL SLIME ACTIVATES GATE MECHANISM
↓
GATE OPENS
↓
SWITCH BACK AND CONTINUE
```
- 图片：`image18.gif`（1138×640，49 帧，位于 8.52″,1.18″）、`image17.gif`（1138×640，41.7 MB，位于 13.67″,4.06″）、`image16.gif`（1138×640，31 帧，位于 8.52″,7.01″），均为 6.16″×3.46″
- 备注（notesSlide8）：
```
Splitting creates the main cooperation puzzle in our game.
A large slime may be too heavy or too large to access a mechanism directly.
By separating into a smaller slime, the player can reach narrow spaces or activate a pressure plate.
The player can then switch control between the small slime and the large slime to continue progressing.
```

### 【第 10 页】STAGE 0 TUTORIAL DESIGN
```
STAGE 0 TUTORIAL DESIGN
ROUGH STONE PATH
→ SMALL RAMP
→ BRIDGE OVER MAGMA
→ NARROW GAP / MECHANISM
→ BIG–SMALL SLIME COOPERATION
```
- 图片（均为示意小图）：`image19.png`(829×457)、`image20.png`(674×441)、`image21.png`(260×173)、`image22.png`(333×228)、`image23.png`(187×234)、`image24.png`(284×347)
- 备注（notesSlide9）：
```
Stage 0 is our tutorial area. We designed it to introduce one idea at a time.
The player first moves across uneven stone ground to feel the slime's softness and rolling motion.
Then they encounter a small slope and a low wall to learn jumping.
Finally, they meet a mechanism that introduces splitting and control switching.
This structure helps players learn through play rather than through long instructions.
```

### 【第 11 页】LEVEL PROGRESSION
```
LEVEL PROGRESSION
STAGE 0 — LEARN
ROUGH FLOOR, SMALL RAMP, LOW WALL, FIRST PRESSURE PLATE.

STAGE 1 — COMBINE
MORE OBSTACLES, MORE BONUS SLIMES, HIDDEN REWARD AREA.

STAGE 2 — MANAGE
SIZE MANAGEMENT AND MIXED OBSTACLE SEQUENCES.

STAGE 3 — FINISH
ESCAPE / SCORE / ENDING FEEDBACK.
```
- 图片：`image19.png`(829×457)、`image20.png`(674×441)、`image25.png`(632×342)、`image26.png`(699×374)、`image27.png`(783×429)、`image28.png`(791×447)
- 备注（notesSlide10）：
```
After the tutorial, later stages combine the same mechanics in more demanding ways.
Stage 1 introduces more obstacles and optional hidden areas.
Stage 2 asks players to manage slime size more carefully and solve mixed obstacles.
The final stage focuses on reaching the escape point and completing the dungeon.
Our goal is to increase complexity through combination, rather than constantly adding unrelated mechanics.
```

### 【第 12 页】ART DIRECTION & DUNGEON MOOD
```
ART DIRECTION & DUNGEON MOOD
VISUAL CONTRAST
THE SLIME IS SOFT, ROUND, AND REACTIVE. THE DUNGEON IS HEAVY, ANGULAR, AND BROKEN.
ENVIRONMENT KEYWORDS
RUINED DWARVEN DUNGEON
WET STONE AND MOSS
ANCIENT PRESSURE MECHANISMS
COLLAPSED STONE PATHS
```
- 图片：`image29.png`(652×370)、`image30.png`(577×439)、`image31.png`(814×470)、`image32.png`(960×748)，各放在一个 `grpSp` 组合内
- 备注（notesSlide11）：
```
Our art direction is based on a ruined dungeon. We used hard stone surfaces, broken architecture,
and darker underground spaces to create a strong contrast with the soft and colourful slime.
This contrast is important because it makes the player character visually distinct and helps reinforce the transformation theme.
```

### 【第 13 页】FROM GREYBOX TO FINAL ENVIRONMENT
```
FROM GREYBOX TO FINAL ENVIRONMENT
THE GREYBOX DEFINES TIMING, ROUTE, AND OBSTACLES. THE FINAL ENVIRONMENT KEEPS THOSE GAMEPLAY RELATIONSHIPS READABLE.
```
- 图片：4 个 `grpSp`，各含 1 张 PNG，尺寸统一 3.99″×5.49″：
  - `Group 6` (1.71″,4.08″) → `image33.png` (686×729)
  - `Group 8` (5.93″,3.63″) → `image34.png` (687×743)
  - `Group 2` (10.15″,4.24″) → `image35.png` (825×765)
  - `Group 4` (14.34″,3.31″) → `image36.png` (687×682)
- 备注（notesSlide12）：
```
We began with greybox level construction before replacing it with environment assets.
This allowed us to test scale, movement, jump height, and obstacle placement before spending time on visual polish.
After the gameplay space was more stable, we selected modular dungeon assets and began replacing the basic blocks.
[wxy] will now explain how environment design supports player readability.
```

### 【第 14 页】SCENE CONSTRUCTION & LAYOUT
```
SCENE CONSTRUCTION & LAYOUT
ROUTE READABILITY
THE PLAYER SHOULD SEE THE NEXT OBSTACLE BEFORE NEEDING THE NEXT SLIME BEHAVIOUR.
GAMEPLAY PLACEMENT
GATES, PRESSURE PLATES, GAPS, AND ROUGH FLOORS ARE POSITIONED AS TEACHING MOMENTS.
```
- 图片：`image36.png`(687×682, 中心大图 5.16″×5.16″)、`image31.png`(814×470)、`image37.png`(814×473)、`image38.png`(653×377)、`image4.png`(808×392)
- 备注（notesSlide13）：
```
Our scene layout is designed to guide the player without relying too heavily on text instructions.
Traversable routes are kept visually readable, while key obstacles and mechanisms are placed where players can recognise them quickly.
We also use the arrangement of stone paths, walls, slopes, and gates to suggest the intended direction of movement.
```

### 【第 15 页】LIGHTING, CAMERA & READABILITY
```
LIGHTING, CAMERA & READABILITY
LIGHTING
PLAYABLE ROUTE IS BRIGHTER. IMPORTANT MECHANISMS RECEIVE VISUAL EMPHASIS.
CAMERA
THE CAMERA KEEPS THE CONTROLLED SLIME AND THE NEXT INTERACTION TARGET VISIBLE.
FEEDBACK
DOORS, PLATES, AND INTERACTABLE AREAS NEED CLEAR VISUAL STATES BEFORE AND AFTER ACTIVATION.
```
- 图片：`image40.png`(812×476) 位于左 (0.20″)、**视频对象** `media3.mp4`（408,420 B，**9.80 s，1138×640**）+ 海报 `image41.jpeg`(1138×640) 居中 (6.36″)、`image39.png`(813×468) 位于右 (13.02″)
- 备注（notesSlide14）：
```
Lighting and visual contrast help communicate gameplay information.
Important routes and interactive areas should be easier to notice than background decoration.
We also want key mechanisms, such as pressure plates and doors, to provide visible feedback when activated.
This helps the player understand the result of their action immediately.
```

### 【第 16 页】DEVELOPMENT PROCESS AND EVIDENCE
```
DEVELOPMENT PROCESS AND EVIDENCE
DEFINED THE THEME, SCOPE, ROLES, AND CORE GAMEPLAY LOOP;
DEVELOPED THE SLIME PHYSICS PROTOTYPE; AFTER THAT,
TESTED THE MECHANICS IN A GREYBOX ENVIRONMENT;
BEGAN INTEGRATING ENVIRONMENT ASSETS, VISUAL FEEDBACK, AND PRESENTATION MATERIALS.
```
- 图片 / 视频（共 22 个 blip 引用）：`image42.jpeg`(552×840)、`image43.png`(720×405)、`image31.png`、`image29.png`、`image9.png`(×3 作为时间轴节点)、`image45/46/47.png`(各 432×243)、另有 2 个视频对象：
  - `media4.mp4`（221,993 B，**6.70 s，1138×640**）+ 海报 `image44.jpeg`，位于 (3.91″,4.91″)，4.54″×2.55″
  - `media3.mp4`（9.80 s，1138×640，与第 15 页同一文件）+ 海报 `image41.jpeg`，位于 (3.91″,7.37″)，4.54″×2.55″
- 备注（notesSlide15）：
```
Then I'll talk about the part of our development process:
first, we defined the core gameplay loop; then we developed the slime prototype;
after that, we tested the mechanics in a greybox environment; finally, we began integrating environment assets,
visual feedback, and presentation materials. This process helped us make decisions based on playable evidence.
```

### 【第 17 页】AUDIO DESIGN
```
AUDIO DESIGN
AUDIO CONFIRMS THE RESULT OF THE PLAYER'S TRANSFORMATION AND INTERACTION.
SLIME SOUNDS
FREE SOUND EFFECTS
DUNGEON SOUNDS
TAKEN FROM THE DUNGEON ANIMATION
BACKGROUND MUSIC
MADE BY FL STUDIO
```
- 图片：`image48.png`(341×248)、`image49.png`(349×253)、`image50.png`(544×340)、`image51.png`(341×125)、`image52.png`(341×125)
- 备注（notesSlide16）：
```
Audio is used as gameplay feedback rather than only background decoration.
The slime has sounds for bouncing, splitting, colliding, and moving.
The dungeon also has sounds for activating mechanisms and opening stone doors.
These sound cues make actions feel more satisfying and help players confirm whether an interaction has succeeded.
```

### 【第 18 页】PLAYTEST & FEEDBACK ★真表格
页面含 1 个真实 `<a:tbl>`（`Table 4`，12 个单元格，12 个 `a:t`；列宽 2743781/4268840/3386099，行高 982457/1726454/1726454/2334087）：

| Issue | What we observed | Why it mattered |
|---|---|---|
| Jump force | The slime could not reliably clear intended obstacles. | Movement felt restrictive instead of elastic and responsive. |
| Movement speed | Default movement was too slow across the level. | Exploration became less engaging and slowed down testing. |
| Elasticity feedback | Elasticity changes felt like a hidden functional setting rather than a clear player-facing state. | Players could not easily understand or plan around the slime's current condition. |

- 图片：`image53.jpeg`（768×1025，竖版截图）位于 (1.13″,2.20″)，5.32″×7.40″
- 备注（notesSlide17）：
```
During early testing, we identified three issues that affected the core player experience.
Take the First issue as example, the jump force was not strong enough for the intended obstacle scale,
so the slime could not reliably clear certain slopes and low walls.
The other issues and how we resolve it can be clearly seen on the screens.
```

### 【第 19 页】ITERATION ★真表格
页面含 1 个真实 `<a:tbl>`（15 个单元格，15 个 `a:t`；列宽 3840055/3413816/4465986）：

| Problem | Iteration | Intended Effect |
|---|---|---|
| Jumping felt weak | Increased jump force | Obstacles feel achievable while preserving a bouncy slime identity. |
| Movement was too slow | Added a sprint function | Faster traversal and more responsive player control. |
| Elasticity state was unclear | Added a HUD for elasticity and particle count | Players can read their current state before making a decision. |
| Elasticity had limited variation | Added an additional elasticity tier | Transformation becomes a more meaningful gameplay choice. |

- 图片（右侧竖排 3 张 GIF，均 5.68″×3.19″，x=13.92″）：`image54.gif`(22.4 MB, 84 帧, 上方)、`image11.gif`(43.8 MB, 中部)、`image55.gif`(37.3 MB, 下方)
- 备注（notesSlide18）：
```
Based on these findings, we made four targeted iterations.
Firstly, We increased the jump force so that the slime could clear intended obstacles more reliably and
better communicate its elastic identity.
Finally, we added another elasticity tier. This turns elasticity from a simple functional setting into a
more readable state that can support player decision-making during traversal and future puzzle design.
```

### 【第 20 页】REFLECTION & FUTURE IMPROVEMENTS
```
REFLECTION & FUTURE IMPROVEMENTS
WHAT WORKED
CORE SLIME PHYSICS WAS COMPLETED EARLY. THE TEAM KEPT THE PLAYABLE LOOP FOCUSED AND TESTABLE.
WHAT WAS DIFFICULT
PHYSICS STABILITY, LEVEL DIMENSIONS, COLLISION BEHAVIOUR, AND CONTROL-SWITCH READABILITY.
NEXT STEPS
MORE PUZZLE ROOMS, BETTER VFX AND AUDIO, HIDDEN REWARDS, AND ELEMENTAL ABSORPTION ABILITIES.
```
- 媒体：`Group 2`（12.31″,3.30″）含 `image8.png`(1153×559)；`Group 4`（3.16″,2.96″）含**视频** `media5.mp4`（6,387,039 B，**67.05 s，1920×880**）+ 海报 `image56.jpeg`(1280×588)；`Group 16` 含 `image43.png`；`图片 18` = `image10.gif`(1138×640, 44 帧)
- 备注（notesSlide19）：
```
Our main success was establishing the slime's core physical identity early in development.
Once movement, bouncing, and splitting became playable, we could use them to guide level design and visual decisions.
Players also need to understand its current state, its purpose, and the choices it creates.
Overall, Slime Dungeon uses transformation not only as a theme, but as the way players move, think, and solve the dungeon.
```

### 【第 21 页】GAMEPLAY DEMO VIDEO ★demo 视频
```
GAMEPLAY DEMO VIDEO
```
- **视频对象**：`media6.mp4`，**126,585,888 B（120.7 MB）= 全包体积的 27.3%**，H.264 `avc1` + 音频 `mp4a`，时长 **102.02 s**，分辨率 **1920×1080**；在 ZIP 中以 **Stored（无压缩）** 保存（CompressedLength == Length）
- 播放器图片名：`2026-07-04 15-11-27`（即屏幕录制文件名），位于 **(0″,0″)，尺寸 20.00″×11.25″ = 铺满整页**；海报帧 `image57.png`（1920×1080，1.76 MB）
- 备注（notesSlide20）：
```
We will now show the core gameplay loop in action.
```

### 【第 22 页】THANK YOU
```
THANK YOU
```
- 图片：`image1.png`（1204×702，与第 1 页同一文件），位于 (10.48″,2.50″)，8.74″×7.84″
- 备注（notesSlide21）：**正文为空**，只有一个页码域（`a:fld type="slidenum"` → "22"，是我抽取到的唯一文本）

---

## 2. 媒体清单汇总（ppt/media/，63 个条目，共 463,620,136 B）

### 2.1 视频（6 个 MP4）

| 文件 | 大小 | 时长 | 分辨率 | 用在哪一页 | 用途 |
|---|---|---|---|---|---|
| `media1.mp4` | 23,565 B | 3.40 s | 426×240 | 8 | 技术决策旁的小演示片 |
| `media2.mp4` | 42,817 B | 3.20 s | 426×240 | 8 | 同上 |
| `media3.mp4` | 408,420 B | 9.80 s | 1138×640 | 15, 16 | 光照/可读性演示、开发过程证据 |
| `media4.mp4` | 221,993 B | 6.70 s | 1138×640 | 16 | 开发过程证据 |
| `media5.mp4` | 6,387,039 B | 67.05 s | 1920×880 | 20 | 复盘页跑图演示 |
| **`media6.mp4`** | **126,585,888 B** | **102.02 s** | **1920×1080** | **21** | **GAMEPLAY DEMO VIDEO（铺满整页）** |

### 2.2 动画 GIF（11 个，全部是游戏内录屏）

| 文件 | 大小 | 尺寸 | 帧数 | 用在哪一页 |
|---|---|---|---|---|
| `image5.gif` | 10,980,687 | 1138×640 | 67 | 3, 7(ROLL) |
| `image6.gif` | 81,276,002 | **1024×640** | 未测 | 3 |
| `image7.gif` | 12,148,816 | 1138×640 | 98 | 4, 5, 7(SPLIT AND MERGE) |
| `image10.gif` | 17,789,589 | 1138×640 | 44 | 7(BOUNCE), 20 |
| `image11.gif` | 43,804,205 | 1138×640 | 未测 | 7(SOFT), 19 |
| `image12.gif` | 20,387,849 | 1138×640 | 54 | 7(SPLIT) |
| `image16.gif` | 10,642,019 | 1138×640 | 31 | 9 |
| `image17.gif` | 41,701,904 | 1138×640 | 未测 | 9 |
| `image18.gif` | 19,911,301 | 1138×640 | 49 | 9 |
| `image54.gif` | 22,444,486 | 1138×640 | 84 | 19 |
| `image55.gif` | 37,327,504 | 1138×640 | 未测 | 19 |

> 11 个 GIF 合计 317.7 MB，占整包 68.5%——**这是文件达到 464 MB 的主因**。

### 2.3 静态图（16 PNG + 11 JPEG，仅列要点）

- 第 1/22 页共用 `image1.png`（1204×702，384 KB）
- 第 2 页示意图：`image2.png`（474×355）
- 第 7 页四个能力按钮底图：GIF（见上）
- 第 13 页 greybox→final 对比图：`image33/34/35/36.png`（686×729 / 687×743 / 825×765 / 687×682）
- 第 18 页竖版截图：`image53.jpeg`（768×1025）
- 第 21 页 demo 视频海报：`image57.png`（1920×1080，1.76 MB）

---

## 3. 第 4 页 vs 第 5 页：**不完全一致**

| 对比维度 | 结果 |
|---|---|
| 原始 XML | `slide4.xml` = 9,699 B ≠ `slide5.xml` = 10,479 B（`-ceq` 比较为 False） |
| `a:t` 数量 | 4 页 9 个，5 页 9 个，**相同** |
| **文字内容** | **完全一致**（把全部 `a:t` 按顺序拼接后 `-ceq` 比较 = True） |
| 形状数量 | 第 4 页 9 个（top-level sp/pic）；第 5 页 10 个 |
| 差异 1：图片张数 | 第 4 页只有 1 张 `image7.gif`（11.85″×6.67″，居中）；**第 5 页有 2 张**：`image7.gif`（缩到 7.77″×4.37″，移到左侧 1.82″,2.96″）**＋ 新增 `image8.png`（1153×559，位于 10.41″,2.90″，7.92″×4.34″）** |
| 差异 2：所有文本框坐标 | 全部不同（第 5 页整体重排，例如 `STATE SHIFTS` 从 2.35″,7.91″ → 2.64″,8.27″） |
| 差异 3：备注页 | `notesSlide3`（3538 B，讲 Transformation 主题）≠ `notesSlide4`（2754 B，讲 dungeon/slime 软硬对比 + 交给 [lmx] 讲物理） |
| 差异 4：图片关系 | 第 4 页 1 条图片关系（rId3）；第 5 页 2 条（rId3=`image8.png`, rId4=`image7.gif`） |

**结论**：第 4、5 页是**同一页内容的两个版本**——文案零差异，但第 5 页多了一张静图（`image8.png`）且版式整体重排、讲稿不同。第 5 页看起来是后续修订版（追加了视觉素材），但无法从文件本身判定哪一版是初稿（`slide5.xml` 内含新建形状 id 更大，且第 5 页备注里出现了"接下来交给 [lmx]"，符合更后期的演讲切分）。

---

## 4. 第 8 页表格「问题 → 决策 → 结果」还原依据

**为什么需要还原**：`Table 4` 是一个 **4 行 × 3 列的空表格骨架**（12 个 `<a:tc>`，每个单元格内只有空 `<a:p>`，`<a:t>` 数量 = 0），仅提供视觉网格。所有文字放在 9 个独立 TextBox 里，`slide8.xml` 中的**文档顺序与视觉顺序完全不一致**（例如文本文档顺序是 …problem1 → result3 → decision3 → problem3 → result2 → decision2 → problem2 → result1 → decision1）。

**还原依据（三重独立证据，全部指向同一结果）**：

1. **表格网格几何**：Table 4 起点 x=1,028,700，列宽 5,730,557 / 5,250,022 / 5,250,022 →
   - 列 1（PROBLEM 区）：x ∈ [1,028,700, 6,759,257]
   - 列 2（DECISION 区）：x ∈ [6,759,257, 12,009,279]
   - 列 3（RESULT 区）：x ∈ [12,009,279, 17,259,301]
   行高各 2,147,331，起点 y=1,142,917 → 数据行分别是 y ∈ [3,290,248, 5,437,579]（row2）、[5,437,579, 7,584,910]（row3）、[7,584,910, 9,732,241]（row4）。

2. **每个文本框都严格落在唯一一个 (行, 列) 单元格内**（无一跨界，无一含糊）：

| 文本框 | x 区间 | 命中列 | y 区间 | 命中行 | 归类 |
|---|---|---|---|---|---|
| TextBox 11 `Inaccurate density field calculation (hole breaking problem)` | 3,382,370–6,449,352 | 列1 | 3,634,227–5,169,508 | row2 | PROBLEM |
| TextBox 19 `Increase particle count, anisotropic kernel function (...)` | 7,285,485–11,438,851 | 列2 | 3,659,998–4,763,079 | row2 | DECISION |
| TextBox 18 `Fix hole artifacts with higher cost` | 12,953,915–15,493,135 | 列3 | 3,634,227–4,797,989 | row2 | RESULT |
| TextBox 17 `High computational cost of Marching Cubes（especially in spliting)` | 3,322,319–6,389,301 | 列1 | 5,911,120–7,446,401 | row3 | PROBLEM |
| TextBox 16 `GPU ComputeShader and Coarse-grained culling` | 7,615,099–10,682,081 | 列2 | 5,911,120–7,446,401 | row3 | DECISION |
| TextBox 15 `Significantly smoother computation with lower overhead` | 12,953,915–16,428,879 | 列3 | 5,651,449–7,186,730 | row3 | RESULT |
| TextBox 14 `Dual displacement of main body and clones` | 3,159,277–6,226,259 | 列1 | 8,189,351–9,353,113 | row4 | PROBLEM |
| TextBox 13 `Core-Perimeter Hierarchical Driving (MoveCoreRadius)` | 7,950,970–10,699,384 | 列2 | 7,985,456–9,520,737 | row4 | DECISION |
| TextBox 12 `Only drive the blob core; peripheral particles ... simultaneously)` | 12,478,065–16,816,865 | 列3 | 7,723,019–10,001,338 | row4 | RESULT |

3. **讲稿佐证**：notesSlide7 明确说"three technical decisions"，并逐一介绍三项：①密度场算错→表面出现空洞；②Marching Cubes 太贵（尤其分裂时）→迁到 GPU Compute Shader + 粗粒度剔除；③用 core-perimeter 层级驱动 + PBF 密度约束，让"不是每个粒子都以同样速度被驱动"。与上表三行完全对应。

**不确定之处**：

- 第 3 行 PROBLEM 单元格的文字 `Dual displacement of main body and clones`（"主体与克隆体的双重/并行位移"）**句法上更像在描述一种"做法"而非问题**；但从几何位置（列 1、row4）、表头 `PROBLEM`、以及讲稿"every particle being driven at the same speed"来看，它确实是被当作"错误做法/问题"写的。可理解为"（朴素方案）主体与克隆体一起并行位移"→ 问题。**语义上略有歧义，位置归属无歧义。**
- 表格第 1 行（表头行，y 1,142,917–3,290,248）**没有任何文本框叠加**，故表头文字 `PROBLEM/DECISION/RESULT` 是浮在表格上方的独立文本框，而非单元格内容（y≈1.99″，正好落在表头行的垂直居中偏下位置）。因此**无法从单元格内容验证"表头行"到底对应哪一列的定义**，只能依赖 x 坐标对齐（PROBLEM 文本框 x 中心 ≈ (2.24″+2.06″/2)=3.27″，落在列 1 内；DECISION 中心 ≈ 10.0″，落在列 2 内；RESULT 中心 ≈ 16.0″，落在列 3 内）——对齐非常明确。
- RESULT(row4) 原文末尾有一个**多余的右括号** `simultaneously)`，疑为原稿笔误（未做任何修改）。

---

## 5. 【技术要点】史莱姆实现技术信息点（逐条 + 来源页）

### 5.1 密度场（Density Field）
1. 出现过 **density field 计算不准确**的问题，表现为**史莱姆表面出现空洞 / 破洞（hole breaking problem）**——【P8】
2. 密度场计算不准的直接后果被写成"表面可见空洞"（"inaccurate density-field calculation created visible holes in the slime surface"）——【P8 notes】
3. 针对空洞问题的两项决策：**① 提高粒子数量；② 采用各向异性核函数（anisotropic kernel function）——沿表面切向拉伸核**——【P8】
4. 代价明确写为 **"Fix hole artifacts with higher cost"**（修好了空洞，但开销更高）——【P8】

### 5.2 Marching Cubes / 表面重建
5. **Marching Cubes 计算开销过高，尤其在史莱姆分裂（splitting）时**——【P8】
6. 决策：把该过程**迁移到 GPU Compute Shader**，并**加入粗粒度剔除（coarse-grained culling）**——【P8】（讲稿原文："We moved this process to a GPU Compute Shader and added coarse-grained culling"）——【P8 notes】
7. 结果：**"Significantly smoother computation with lower overhead"**（计算明显更顺、开销更低）——【P8】
8. 注意：此处只写了 MC 的"计算"迁移到 GPU，**没有说明密度场光栅化是否也在 GPU** —— 【不确定项】

### 5.3 物理仿真与 PBF
9. 整体方案是 **PBF（Position-Based Fluids）密度约束**——多处出现 "PBF density constraints"——【P8, P8 notes】
10. **核心-边缘层级驱动（Core-Perimeter Hierarchical Driving，参数名 `MoveCoreRadius`）**：**只驱动 blob 的核心，外围粒子由 PBF 密度约束被动拖拽**——【P8】
11. 该设计的目的：**避免所有粒子以同一速度被同步驱动**（"preventing all particles from moving at the same speed simultaneously"），使身体运动更自然——【P8, P8 notes】
12. 该行的问题侧被写作 `Dual displacement of main body and clones`（主体与克隆体的双重位移）——【P8】
13. 需求侧的对应问题：**Jump force 不足**，史莱姆无法可靠越过预期障碍 → 运动感觉"受限"，而非"弹性、响应快"——【P18】
14. 迭代：**增大跳跃力（Increased jump force）**，目标是在保持弹跳史莱姆身份的同时让障碍"可达成"——【P19】
15. 需求侧问题：**默认移动速度过慢**，探索变得无趣、拖慢测试——【P18】
16. 迭代：**加入冲刺功能（sprint function）**，获得更快的位移与更灵敏的操控——【P19】

### 5.4 分裂 / 合体与控制切换
17. 原型已支持 **movement、bouncing、splitting**——【P7 notes】
18. 四个物理能力标签：**BOUNCE / ROLL / SPLIT / SOFT**（另有 **SPLIT AND MERGE** 组合演示）——【P7】
19. 三层状态对偶：**LARGE SLIME ↔ SMALL SLIME、ONE BODY ↔ SPLIT BODIES、SOFT MOVEMENT ↔ PUZZLE INTERACTION**——【P4, P5】
20. 玩法能力集合写为 **BOUNCE, SOFTEN, SPLIT, AND SWITCH CONTROL**——【P3】
21. **分裂是合作解谜的核心**：大史莱姆太重/太大无法直接触及机关，分离成小史莱姆后可进入窄缝或触发压力板，然后**在大小两个史莱姆之间切换控制**继续推进——【P9 notes】
22. 控制切换的具体谜题链路：`BIG SLIME STOPS AT GATE → SWITCH TO SMALL SLIME → SMALL SLIME ACTIVATES GATE MECHANISM → GATE OPENS → SWITCH BACK AND CONTINUE`——【P9】
23. **"Control-switch readability（控制切换可读性）"被列为开发中最困难的问题之一**——【P20】
24. `Deeply`：碰撞行为（**collision behaviour**）与**物理稳定性（physics stability）**也被列为难点——【P20】

### 5.5 弹性 / 密度档位（Elasticity / 密度分级）
25. 需求侧问题：**弹性变化感觉像"隐藏的功能开关"，而不是玩家可感知的状态**，玩家无法理解或预判史莱姆当前状态——【P18】
26. 迭代：**增加 HUD 显示弹性（elasticity）和粒子数（particle count）**，让玩家在做决策前能读到当前状态——【P19】
27. 迭代：**增加额外的弹性档位（an additional elasticity tier）**，让变形成为更有意义的玩法选择——【P19】
28. 讲稿强调把弹性"从一个单纯的功能设置变成更可读的状态"——【P19 notes】

### 5.6 HUD / 可读性 / 表现反馈
29. HUD 需显示：**elasticity + particle count**——【P19】
30. **相机（Camera）要同时保持"当前控制的史莱姆"和"下一个交互目标"可见**——【P15】
31. **可通行路线要更亮**，重要机关获得视觉强调——【P15】
32. 门、压力板、可交互区域在**激活前/后需要清晰不同的视觉状态**——【P15】
33. 音频用于**确认玩家变形与交互的结果**：史莱姆有弹跳、分裂、碰撞、移动音效；地牢有机关启动与石门开启音效——【P17, P17 notes】

### 5.7 性能调优 / 工程取舍
34. **粗粒度剔除（coarse-grained culling）**是性能手段之一——【P8】
35. **GPU ComputeShader 化**是主要性能手段——【P8】
36. 结论性的取舍表述：**"PROTOTYPE FOCUS: CONTROLLABLE PHYSICAL FEEDBACK RATHER THAN FULL SIMULATION COMPLEXITY"**（原型重点是可控的物理反馈，而非完整的流体仿真复杂度）——【P7】
37. 讲稿同义重申：**"Rather than aiming for a fully realistic fluid simulation, we prioritised readable and controllable movement for gameplay."**——【P7 notes】
38. **"提高粒子数 + 各向异性核"是"用更高开销换更好表面"**的显式权衡——【P8】
39. 核心成功经验：**史莱姆的核心物理身份在开发早期就完成**；一旦 movement / bouncing / splitting 可玩，就能用它来指导关卡设计与视觉决策——【P20 notes】
40. 未来计划中包含 **更好的 VFX 与音频、隐藏奖励、元素吸收能力（elemental absorption abilities）**——【P20】

### 5.8 密度场 / 网格相关补充（页内其余技术线索）
41. 第 6 页把玩法循环抽象为 5 步：**OBSERVE OBSTACLE → CHOOSE SLIME STATE → USE PHYSICS ABILITY → TRIGGER MECHANISM → REACH NEXT SECTION**——【P6】
42. 第 10 页教程关的物理教学顺序：**ROUGH STONE PATH → SMALL RAMP → BRIDGE OVER MAGMA → NARROW GAP / MECHANISM → BIG–SMALL SLIME COOPERATION**（先感受软度与滚动，再学跳跃，最后引入分裂与控制切换）——【P10, P10 notes】
43. 第 13 页明确 **greybox 先行**：先用灰盒测试**尺度、移动、跳跃高度、障碍摆放**，再做视觉打磨——【P13 notes】
44. 第 11 页关卡推进：**Stage 0 LEARN（rough floor/small ramp/low wall/first pressure plate）→ Stage 1 COMBINE（更多障碍、更多 bonus slimes、隐藏奖励区）→ Stage 2 MANAGE（尺寸管理与混合障碍序列）→ Stage 3 FINISH（escape / score / ending feedback）**——【P11】

### 5.9 其他可核实的技术性事实（来自容器层）
45. 全篇**未出现** SmartArt/图示对象，所有"流程图"（第 6 页循环、第 9 页箭头链、第 16 页时间轴）都是**文本框 + 图片/自动图形**手工拼的——【容器事实】
46. 第 9、16 页的箭头/流程均以文本 `↓`、`→` 呈现，说明没有使用连线的 SmartArt——【P9, P10, P16】
47. 第 6 页的 5 个循环节点**复用了同一张 `image9.png`（282×320）**（同一 rId 引用 5 次）——【P6, 容器事实】
48. 第 16 页时间轴的 3 个节点**复用同一张 `image9.png`**（rId16 引用 3 次）——【P16, 容器事实】
49. 第 15 页与第 16 页**复用同一个视频 `media3.mp4`**——【P15, P16, 容器事实】
50. 第 1 页与第 22 页**复用同一张 `image1.png`**；第 4/5/7 页复用 `image7.gif`；第 5/20 页复用 `image8.png`；第 7/19 页复用 `image11.gif`；第 7/20 页复用 `image10.gif`；第 10/11 页复用 `image19/20.png`；第 12/14/16 页复用 `image31.png`；第 12/16 页复用 `image29.png`；第 13/14 页复用 `image36.png`；第 16/20 页复用 `image43.png`——【容器事实】

---

## 6. 【不确定项】

1. **第 8 页表格映射的语义歧义**：三行的行列归属由几何 + 讲稿双重确定，但 `Dual displacement of main body and clones` 作为"问题"的措辞偏"做法"，**语义上可解读为"（错误的）做法"**，无法 100% 排除作者本意是别的排法。（另：RESULT(row4) 结尾多一个 `)`，疑似笔误。）
2. **第 8 页表格单元格图形化**：表格 12 个单元格全空，无法确认表头行在 PowerPoint 里是否被设置了"标题行"样式属性；`<a:tblPr/>` 为空、无 `tableStyleId`，所以连表格样式名都无法给出。
3. **`media1.mp4` / `media2.mp4` 体积异常小**：23.6 KB / 42.8 KB，但时长 3.4 s / 3.2 s、426×240。按码率折算约 55 kbps，画质极低或是"仅少量帧/几乎静态"的占位视频。**我未解码其内容**，无法确认画面里是什么（推测是第 8 页旁的小演示，但不排除是损坏/占位文件）。
4. **4 个 >30 MB 的 GIF 未统计帧数**（`image6.gif` 81 MB、`image11.gif` 43.8 MB、`image17.gif` 41.7 MB、`image55.gif` 37.3 MB）：我出于内存/耗时考虑跳过了逐帧解析，只确认了逻辑尺寸。已验证的 7 个 GIF 帧数为 31–98 帧。
5. **`image6.gif` 逻辑尺寸为 1024×640，其余 10 个 GIF 均为 1138×640**，尺寸不一致的原因不明（可能是不同录制批次/工具）。
6. **第 2 页示意图偏小**：`image2.png` 仅 474×355 却被拉伸到 8.83″×6.61″（约 54 DPI），实际放映可能偏模糊——这是"文件里如是"，我无法判断是否有意为之。
7. **第 22 页备注为空**：`notesSlide21` 文本占位符内只有一个空段落，唯一文本是页码域 "22"（我抽取到的 21 个备注页末尾的 "* 2"…"* 22" 均为页码域，不是讲稿内容）。
8. **节 / 自定义放映确实不存在**：`presentation.xml` 无 `sectLst`、无 `custShowLst`，`presentation.xml.rels` 也无 customShow 关系。若放映方希望分区，需要在 PowerPoint 里另建。
9. **无 SmartArt 可抽**：题目假设的 `ppt/diagrams/*.xml` 在该文件中**完全不存在**，因此"SmartArt 文字"一项为空——不是抽取失败，而是本来就没有。
10. **备注页与幻灯片的对应关系**：`notesSlideN` 与 `slideN` 的编号**错位 1**（notesSlide1 属于第 2 页），我是通过 `ppt/slides/_rels/slideN.xml.rels` 的 `notesSlide` 关系反查确认的，而非按编号猜测——这一点若有人按编号直接对应会全部错位。
11. **第 4/5 页的"哪一版是初稿"无法从文件判定**；我只能确认文字相同、第 5 页多一张图且版式与讲稿不同。
12. **媒体内容（画面）我未做视觉识别**：图片/视频的具体画面内容（例如第 13 页 4 张图哪两张是 greybox、哪两张是 final）仅从"greybox→final"标题与尺寸推断，未逐张看图确认。
13. **`docProps/app.xml` 的 `MMClips=7` 与实际的 6 个视频对象数目不一致**（视频对象分布在第 8 页 ×2、15、16 ×2、20、21 = 7 个对象，但只有 6 个不同 mp4 文件）。以"对象数=7"解释则自洽——但这只是我的推断。
14. 第 15/16 页的视频对象与第 8 页一样采用 `p14:media r:embed` + `a:videoFile r:link` **双关系指向同一 mp4**；在第 20/21 页同样如此。**这意味着同一视频在包内只存一份，播放时靠 rels 关联**——符合观察，但 PowerPoint 版本差异可能导致某些 viewer 只认其中之一。
