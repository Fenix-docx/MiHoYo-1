from pathlib import Path
root=Path.cwd();stage=root/'.dsh-scratch/p1/staging';base=root/'Assets/A_UI/UI/Motion'
def load(name):return (base/name).read_text(encoding='utf-8-sig')
def save(name,text): (stage/name.split('/')[-1]).write_text(text,encoding='utf-8')
s=load('Path/PathSegment.cs')
s=s.replace('// P1 才实现其余几何类型；参数区可扩展，采样入口保持 tag + switch。','// 参数区只含值类型，采样入口保持 tag + switch；保留 P0 的枚举数值。')
s=s.replace('Line, CubicBezier }','Line, CubicBezier, QuadBezier, Arc, CatmullRom }')
s=s.replace('        public Vector2 Evaluate(float t)', '''        // Arc 单独编码中心/轴缩放/弧度角/旋转；p0/p3 仍缓存真实端点供旧工具读取。
        // CR 的 p0/p3 是虚点，p1/p2 是真实起止点；其他类型的 p0/p3 为起止点。
        public Vector2 center, ellipseScale;
        public float radius, a0, a1, rotation;
        public static PathSegment QuadBezier(Vector2 from, Vector2 control, Vector2 to) =>
            new PathSegment { kind = PathSegmentKind.QuadBezier, p0 = from, p1 = control, p3 = to };
        public static PathSegment CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3) =>
            new PathSegment { kind = PathSegmentKind.CatmullRom, p0 = p0, p1 = p1, p2 = p2, p3 = p3 };
        public static PathSegment Arc(Vector2 center, float radius, float a0, float a1) =>
            Arc(center, radius, a0, a1, Vector2.one);
        public static PathSegment Arc(Vector2 center, float radius, float a0, float a1, Vector2 ellipseScale)
        {
            var result = new PathSegment { kind = PathSegmentKind.Arc, center = center, radius = radius,
                a0 = a0, a1 = a1, ellipseScale = ellipseScale };
            result.p0 = result.Evaluate(0); result.p3 = result.Evaluate(1);
            return result;
        }
        /// <summary>圆：r·角差；椭圆：第二类椭圆积分，反向和多圈同样有效。</summary>
        public float ArcLength(float t = 1)
        {
            if (kind != PathSegmentKind.Arc) throw new InvalidOperationException("仅 Arc 有解析弧长。");
            return (float)ArcLengthMath.Length(radius * (double)ellipseScale.x, radius * (double)ellipseScale.y,
                a0, a0 + ((double)a1 - a0) * t);
        }
        private Vector2 Rotate(Vector2 v)
        {
            float c = (float)Math.Cos(rotation), s = (float)Math.Sin(rotation);
            return new Vector2(c * v.x - s * v.y, s * v.x + c * v.y);
        }
        public Vector2 Evaluate(float t)''')
s=s.replace('                default: throw new NotSupportedException("未实现的路径段。");', '''                case PathSegmentKind.QuadBezier:
                    float q = 1 - t;
                    return q * q * p0 + 2 * q * t * p1 + t * t * p3;
                case PathSegmentKind.Arc:
                    double angle = a0 + ((double)a1 - a0) * t;
                    return center + Rotate(new Vector2((float)Math.Cos(angle) * radius * ellipseScale.x,
                        (float)Math.Sin(angle) * radius * ellipseScale.y));
                case PathSegmentKind.CatmullRom:
                    return .5f * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * (t * t)
                        + (-p0 + 3 * p1 - 3 * p2 + p3) * (t * t * t));
                default: throw new NotSupportedException("未实现的路径段。");''',1)
s=s.replace('                default: throw new NotSupportedException("未实现的路径段。");\n            }\n            return v.normalized;', '''                case PathSegmentKind.QuadBezier:
                    v = 2 * ((1 - t) * (p1 - p0) + t * (p3 - p1));
                    if (v.sqrMagnitude < 1e-12f) v = p3 - p0;
                    break;
                case PathSegmentKind.Arc:
                    double angle = a0 + ((double)a1 - a0) * t;
                    v = Rotate(new Vector2(-(float)Math.Sin(angle) * radius * ellipseScale.x,
                        (float)Math.Cos(angle) * radius * ellipseScale.y)) * (a1 - a0);
                    if (v.sqrMagnitude < 1e-12f) v = Evaluate(1) - Evaluate(0);
                    break;
                case PathSegmentKind.CatmullRom:
                    v = .5f * ((-p0 + p2) + 2 * (2 * p0 - 5 * p1 + 4 * p2 - p3) * t
                        + 3 * (-p0 + 3 * p1 - 3 * p2 + p3) * (t * t));
                    if (v.sqrMagnitude < 1e-12f) v = p2 - p1;
                    break;
                default: throw new NotSupportedException("未实现的路径段。");
            }
            return v.normalized;''')
save('PathSegment.cs',s)
s=load('Tracks/MotionTrack.cs');s=s.replace('        private readonly float[] lut;','        private readonly float[] lut;\n        private readonly PathChain chain;')
s=s.replace('            MotionMath.BuildArcLut(path, lut);\n        }','''            MotionMath.BuildArcLut(path, lut);
            // 新段使用链条的真实端点与解析弧长，P0 单段路径仍走原实现。
            if (path.kind != PathSegmentKind.Line && path.kind != PathSegmentKind.CubicBezier)
                chain = new PathChain(new[] { path });
        }
        public MotionTrack(PathChain chain, float duration, Ease ease, MotionClock clock, RectTransform target,
            PathSpace space, bool clamp = false)
            : this(PathSegment.Line(default, default), duration, ease, clock, target, space, clamp: clamp)
        {
            this.chain = chain ?? throw new ArgumentNullException(nameof(chain));
        }''')
s=s.replace('            var p = AnchorResolver.FromP(MotionMath.EvaluateByArc(path, ease.Evaluate(t), lut, clamp), space.rect);','''            var point = chain != null ? chain.EvaluateByArc(ease.Evaluate(t), clamp)
                : MotionMath.EvaluateByArc(path, ease.Evaluate(t), lut, clamp);
            var p = AnchorResolver.FromP(point, space.rect);''')
save('MotionTrack.cs',s)
s=load('MotionPlan.cs');s=s.replace('        private PathSegment path;','        private PathSegment path;\n        private PathChain chain;')
s=s.replace('Curve(PathSegment value) { path = value;','Curve(PathSegment value) { chain = null; path = value;')
s=s.replace('        public MotionPlan Duration(float value)', '''        public MotionPlan Curve(PathChain value)
        {
            chain = value ?? throw new ArgumentNullException(nameof(value));
            hasCurve = true; hasFrom = hasTo = false; return this;
        }
        public MotionPlan Duration(float value)''')
s=s.replace('            var track = new MotionTrack(hasCurve ? path : PathSegment.Line(default, default), duration, ease, clock,\n                target, new PathSpace { rect = parent }, !hasCurve, from, to, clamp);','''            var track = hasCurve && chain != null
                ? new MotionTrack(chain, duration, ease, clock, target, new PathSpace { rect = parent }, clamp)
                : new MotionTrack(hasCurve ? path : PathSegment.Line(default, default), duration, ease, clock,
                target, new PathSpace { rect = parent }, !hasCurve, from, to, clamp);''')
save('MotionPlan.cs',s)
