// ═════════════════════════════════════════════════════════════════════════════
//  Ascend/Earth  -  HD Earth seen from orbit, in one pass:
//  day map, city lights on the night side, a soft day/night line with a sunset
//  glow, moving clouds (with shadows), shiny oceans and a blue atmosphere edge.
//  The sun direction comes from AscendEarth.cs (_SunDir, world space).
// ═════════════════════════════════════════════════════════════════════════════
Shader "Ascend/Earth"
{
    Properties
    {
        _DayTex     ("Day map", 2D) = "white" {}
        _NightTex   ("Night lights", 2D) = "black" {}
        _CloudTex   ("Clouds", 2D) = "black" {}
        _SpecTex    ("Ocean mask (white = water)", 2D) = "black" {}
        _NightBoost ("City light brightness", Range(0, 6)) = 2.6
        _CloudAmount("Cloud amount", Range(0, 1)) = 0.85
        _AtmoColor  ("Atmosphere", Color) = (0.35, 0.6, 1, 1)
        _Exposure   ("Exposure", Range(0.3, 2)) = 1.05
        [HideInInspector] _SunDir ("Sun direction", Vector) = (1, 0.3, 0, 0)
        [HideInInspector] _CloudShift ("Cloud drift", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _DayTex, _NightTex, _CloudTex, _SpecTex;
            half _NightBoost, _CloudAmount, _Exposure, _CloudShift;
            half4 _AtmoColor;
            float4 _SunDir;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 n : TEXCOORD1; float3 wp : TEXCOORD2; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.n = UnityObjectToWorldNormal(v.normal);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.n);
                float3 l = normalize(_SunDir.xyz);
                float3 v = normalize(_WorldSpaceCameraPos - i.wp);
                float ndl = dot(n, l);

                float2 cuv = float2(i.uv.x + _CloudShift, i.uv.y);
                half3 day = tex2D(_DayTex, i.uv).rgb;
                half clouds = tex2D(_CloudTex, cuv).r * _CloudAmount;
                // cloud shadow: clouds a little toward the sun darken the ground
                half shadow = tex2D(_CloudTex, cuv + float2(-l.x, l.y) * 0.0015).r * _CloudAmount;
                half water = tex2D(_SpecTex, i.uv).r;

                // daylight with a soft terminator
                half lit = saturate(ndl * 1.15 + 0.08);
                half3 ground = day * (0.035 + 1.15 * lit) * (1 - shadow * 0.35);

                // sun glint on the oceans
                float3 h = normalize(l + v);
                half spec = pow(saturate(dot(n, h)), 90) * water * 1.4 + pow(saturate(dot(n, h)), 12) * water * 0.08;
                ground += spec * smoothstep(0.0, 0.2, ndl) * half3(1.0, 0.95, 0.85);

                // clouds: white in the sun, grey-blue in shadow
                half3 cloudCol = lerp(half3(0.05, 0.06, 0.09), half3(1.0, 0.99, 0.97), saturate(ndl * 1.3 + 0.12));
                half3 col = lerp(ground, cloudCol, clouds);

                // sunset band along the day/night line
                half band = exp(-abs(ndl - 0.03) * 34.0) * step(-0.05, ndl);
                col = lerp(col, col * half3(1.3, 0.7, 0.42), band * 0.6);

                // city lights on the night side (warm, a little glow, dimmed by clouds)
                half night = 1 - smoothstep(-0.18, 0.06, ndl);
                half3 lights = tex2D(_NightTex, i.uv).rgb;
                lights = lights * lights * half3(1.0, 0.82, 0.55) * _NightBoost;
                col += lights * night * (1 - clouds * 0.75);

                // atmosphere: blue haze toward the edge, only where it's lit
                half fres = pow(1 - saturate(dot(n, v)), 3.0);
                half atmoLit = saturate(ndl * 1.4 + 0.35);
                col = lerp(col, _AtmoColor.rgb * (0.4 + 0.9 * atmoLit), fres * 0.55 * atmoLit * atmoLit);
                col += _AtmoColor.rgb * 0.03 * lit;

                return half4(col * _Exposure, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
