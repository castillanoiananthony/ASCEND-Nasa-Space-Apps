// ═════════════════════════════════════════════════════════════════════════════
//  Ascend/Sky  -  procedural skybox: blue sky, sun, light clouds, fades to space.
//  Driven by AscendSky.cs (sun direction, altitude). One pass, no textures.
// ═════════════════════════════════════════════════════════════════════════════
Shader "Ascend/Sky"
{
    Properties
    {
        [Header(Sky)]
        _ZenithColor   ("Zenith (straight up)", Color) = (0.16, 0.38, 0.78, 1)
        _HorizonColor  ("Horizon", Color) = (0.64, 0.80, 0.96, 1)
        _GroundColor   ("Below the horizon", Color) = (0.38, 0.42, 0.47, 1)
        _Exposure      ("Exposure", Range(0, 3)) = 1

        [Header(Sun)]
        _SunColor      ("Sun color", Color) = (1, 0.96, 0.88, 1)
        _SunSize       ("Sun disk size", Range(0.002, 0.06)) = 0.018
        _SunGlow       ("Sun glow", Range(0, 2)) = 0.7

        [Header(Clouds)]
        _CloudCoverage ("Coverage", Range(0, 1)) = 0.42
        _CloudSoftness ("Softness", Range(0.02, 0.5)) = 0.18
        _CloudScale    ("Scale (bigger = smaller clouds)", Range(0.2, 4)) = 1.1
        _CloudSpeed    ("Wind speed", Range(0, 0.05)) = 0.006
        _CloudFade     ("Visibility (set by altitude)", Range(0, 1)) = 1

        [Header(Space)]
        _Space         ("Space (0 ground, 1 space - set by altitude)", Range(0, 1)) = 0
        _StarIntensity ("Stars", Range(0, 3)) = 1.2

        [HideInInspector] _SunDir ("Sun direction", Vector) = (0.3, 0.6, 0.5, 0)
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            half4 _ZenithColor, _HorizonColor, _GroundColor, _SunColor;
            half _Exposure, _SunSize, _SunGlow;
            half _CloudCoverage, _CloudSoftness, _CloudScale, _CloudSpeed, _CloudFade;
            half _Space, _StarIntensity;
            float4 _SunDir;

            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            // ── cheap value noise (no textures) ──
            float hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = hash12(i), b = hash12(i + float2(1, 0)), c = hash12(i + float2(0, 1)), d = hash12(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float fbm5(float2 p)
            {
                float v = 0.0, a = 0.5;
                [unroll] for (int i = 0; i < 5; i++) { v += a * vnoise(p); p = p * 2.03 + float2(17.1, 9.7); a *= 0.5; }
                return v;
            }

            float fbm3(float2 p)
            {
                float v = 0.0, a = 0.5;
                [unroll] for (int i = 0; i < 3; i++) { v += a * vnoise(p); p = p * 2.03 + float2(17.1, 9.7); a *= 0.5; }
                return v;
            }

            // Cloud density at a point on the cloud layer
            float cloudShape(float2 uv, bool detailed)
            {
                float2 warp = float2(vnoise(uv * 0.35 + 3.1), vnoise(uv * 0.35 + 7.7)) - 0.5;
                uv += warp * 0.9;                                       // billowy, less grid-like shapes
                float n = detailed ? fbm5(uv) : fbm3(uv);
                float big = vnoise(uv * 0.18 + 1.3);                    // patches of clear sky between cloud fields
                n = n * lerp(0.75, 1.15, big);
                float edge = 1.0 - _CloudCoverage;
                return smoothstep(edge * 0.75, edge * 0.75 + _CloudSoftness, n * 0.85);
            }

            half4 frag(v2f i) : SV_Target
            {
                float3 dir = normalize(i.dir);
                float3 sun = normalize(_SunDir.xyz);
                float y = dir.y;
                float sd = dot(dir, sun);

                // ── Earth sky: blue overhead, pale at the horizon ──
                float h = pow(1.0 - saturate(y), 4.0);
                float3 sky = lerp(_ZenithColor.rgb, _HorizonColor.rgb, h);
                float lowSun = 1.0 - saturate(sun.y * 3.0);                       // warmer horizon at sunrise / sunset
                sky = lerp(sky, sky * float3(1.25, 0.85, 0.62), lowSun * h * 0.6);
                float day = saturate(sun.y * 4.0 + 0.25);
                sky *= lerp(0.08, 1.0, day);                                      // night if the sun is down
                sky += _SunColor.rgb * (pow(saturate(sd), 48.0) * 0.45 + pow(saturate(sd), 6.0) * 0.10) * _SunGlow * day;
                float3 below = lerp(_HorizonColor.rgb, _GroundColor.rgb, saturate(-y * 5.0)) * lerp(0.08, 1.0, day);
                sky = y < 0.0 ? below : sky;

                // ── Space: black, a thin blue line on the horizon, stars ──
                float limb = exp(-abs(y + 0.02) * 30.0);
                float3 space = _HorizonColor.rgb * limb * 0.35 + _ZenithColor.rgb * exp(-abs(y) * 9.0) * 0.08;
                float3 col = lerp(sky, space, _Space);

                // stars
                float3 sp = dir * 220.0;
                float3 cell = floor(sp);
                float rnd = hash12(cell.xy + cell.z * 31.7);
                float star = step(0.9965, rnd) * smoothstep(0.35, 0.0, length(frac(sp) - 0.5));
                col += star * _StarIntensity * smoothstep(0.85, 0.98, _Space) * step(0.0, y + 0.05);

                // ── Sun disk ──
                float a = 1.0 - sd;                                               // ~ angle² / 2
                float r2 = _SunSize * _SunSize * 0.5;
                float disk = 1.0 - smoothstep(r2 * 0.6, r2, a);
                col += _SunColor.rgb * disk * lerp(6.0, 10.0, _Space);

                // ── Clouds (a flat layer projected on the sky) ──
                if (y > 0.0 && _CloudFade > 0.001)
                {
                    float2 uv = dir.xz / (y + 0.06) * _CloudScale;
                    uv += _Time.y * _CloudSpeed * float2(1.0, 0.35) * 10.0;
                    float d = cloudShape(uv, true);
                    if (d > 0.001)
                    {
                        // light: how much cloud lies between this point and the sun
                        float2 toSun = normalize(sun.xz + 1e-4) * 0.22;
                        float occ = cloudShape(uv + toSun, false);
                        float lit = exp(-occ * 2.2);
                        float3 shadowCol = lerp(_ZenithColor.rgb, float3(0.55, 0.58, 0.64), 0.6) * 0.85;
                        float3 litCol = _SunColor.rgb * 1.08;
                        float3 cloud = lerp(shadowCol, litCol, lit);
                        cloud += _SunColor.rgb * pow(saturate(sd), 10.0) * (1.0 - d) * 0.9;   // bright edges toward the sun
                        cloud *= lerp(0.1, 1.0, day);
                        // far clouds melt into the horizon haze
                        float haze = smoothstep(0.0, 0.32, y);
                        cloud = lerp(_HorizonColor.rgb * lerp(0.1, 1.0, day), cloud, haze);
                        float alpha = d * smoothstep(0.0, 0.08, y) * _CloudFade * (1.0 - _Space);
                        col = lerp(col, cloud, saturate(alpha));
                    }
                }

                return half4(col * _Exposure, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
