// Ascend/Atmosphere  -  thin glowing shell around the Earth (additive, cheap)
Shader "Ascend/Atmosphere"
{
    Properties
    {
        _AtmoColor ("Color", Color) = (0.35, 0.62, 1, 1)
        _Power     ("Edge sharpness", Range(1, 12)) = 5
        _Strength  ("Strength", Range(0, 4)) = 1.6
        [HideInInspector] _SunDir ("Sun direction", Vector) = (1, 0.3, 0, 0)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
        Blend One One
        ZWrite Off
        Cull Front
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            half4 _AtmoColor; half _Power, _Strength; float4 _SunDir;
            struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 wp : TEXCOORD1; };
            v2f vert(appdata_base v)
            {
                v2f o; o.pos = UnityObjectToClipPos(v.vertex);
                o.n = UnityObjectToWorldNormal(v.normal); o.wp = mul(unity_ObjectToWorld, v.vertex).xyz; return o;
            }
            half4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.n), l = normalize(_SunDir.xyz);
                // How close the view ray passes to the Earth's centre: bright at the Earth's edge,
                // fading to nothing at the top of the atmosphere (no hard outline)
                float3 c = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
                float rs = length(i.wp - c);            // shell radius
                float re = rs / 1.022;                  // Earth radius
                float3 dir = normalize(i.wp - _WorldSpaceCameraPos);
                float d = length(cross(dir, c - _WorldSpaceCameraPos));
                half t = saturate((rs - d) / (rs - re));
                half glow = pow(t, _Power * 0.5) * t;
                half day = saturate(dot(n, l) * 1.6 + 0.4);
                return half4(_AtmoColor.rgb * glow * day * _Strength, 1);
            }
            ENDCG
        }
    }
}
