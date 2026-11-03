// 夜空（星と月・地平線は少し明るい紺）。スカイボックス用
Shader "FlightUI/NightSky"
{
    Properties
    {
        _TopColor ("Top", Color) = (0.005, 0.01, 0.03, 1)
        _HorizonColor ("Horizon", Color) = (0.03, 0.06, 0.12, 1)
        _StarDensity ("Star Density", Range(0, 1)) = 0.35
        _StarBright ("Star Brightness", Float) = 1.6
        _MoonDir ("Moon Direction", Vector) = (0.4, 0.55, 0.7, 0)
        _MoonColor ("Moon Color", Color) = (0.95, 0.95, 0.85, 1)
        _MoonSize ("Moon Size", Range(0.001, 0.05)) = 0.012
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _TopColor, _HorizonColor, _MoonColor;
            float _StarDensity, _StarBright, _MoonSize;
            float4 _MoonDir;
            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }
            float hash(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
            fixed4 frag (v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = saturate(d.y);
                float3 col = lerp(_HorizonColor.rgb, _TopColor.rgb, pow(h, 0.45));
                // 星: 方向を細かいマスに分けて、ところどころ光らせる
                float3 g = d * 260.0;
                float3 cell = floor(g);
                float r = hash(cell);
                if (r > 1.0 - _StarDensity * 0.04)
                {
                    float3 c = cell + 0.5 + (float3(hash(cell + 1.7), hash(cell + 3.1), hash(cell + 5.3)) - 0.5) * 0.6;
                    float dist = length(g - c);
                    float tw = 0.75 + 0.25 * sin(_Time.y * (1.5 + r * 4.0) + r * 50.0);
                    col += _StarBright * smoothstep(0.35, 0.0, dist) * tw * saturate(d.y * 4.0 + 0.2);
                }
                // 月
                float3 md = normalize(_MoonDir.xyz);
                float m = dot(d, md);
                col += _MoonColor.rgb * smoothstep(1.0 - _MoonSize, 1.0 - _MoonSize * 0.85, m);
                col += _MoonColor.rgb * 0.06 * pow(saturate(m), 64.0);   // 月のまわりのぼんやりした光
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
