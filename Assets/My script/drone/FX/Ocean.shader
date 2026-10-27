// 波のある海（Built-in / VRChat 用・軽量）
//  ・大きなうねり 3 本を頂点で動かす（Gerstner 波）。遠くほど小さくして、ちらつきを防ぐ
//  ・細かい波はノイズ画像で表面の向きだけ揺らす（頂点は増やさない）
//  ・空の映り込み（フレネル）、太陽の照り返し、波頭の白い泡
//  ・メッシュは OceanFollow でプレイヤーの真下について動く。模様はワールド座標なので、ずれない
Shader "FlightUI/Ocean"
{
    Properties
    {
        _NoiseTex ("Detail Noise", 2D) = "gray" {}
        _DeepColor ("Deep Color", Color) = (0.01, 0.09, 0.16, 1)
        _ShallowColor ("Crest Color", Color) = (0.05, 0.32, 0.38, 1)
        _UnderColor ("Underside Color", Color) = (0.02, 0.18, 0.22, 1)
        _FoamColor ("Foam Color", Color) = (0.92, 0.96, 1, 1)
        _WaveA ("Wave A (dir.xy, steep, length)", Vector) = (1, 0.3, 0.2, 80)
        _WaveB ("Wave B (dir.xy, steep, length)", Vector) = (0.6, 1, 0.18, 45)
        _WaveC ("Wave C (dir.xy, steep, length)", Vector) = (-0.7, 0.8, 0.15, 24)
        _WaveScale ("Wave Height", Range(0, 1.5)) = 0.6
        _FadeMul ("Wave Fade (x wavelength)", Float) = 5
        _DetailScale ("Detail Scale (m)", Float) = 22
        _DetailStrength ("Detail Strength", Range(0, 2)) = 0.7
        _SpecPower ("Sun Spec Power", Float) = 350
        _SpecStrength ("Sun Spec Strength", Float) = 3
        _FoamAmount ("Foam", Range(0, 1)) = 0.3
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+10" }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            sampler2D _NoiseTex;
            fixed4 _DeepColor, _ShallowColor, _UnderColor, _FoamColor;
            float4 _WaveA, _WaveB, _WaveC;
            float _WaveScale, _FadeMul, _DetailScale, _DetailStrength, _SpecPower, _SpecStrength, _FoamAmount;
            float _Udon_OceanTime;   // OceanFollow が毎フレーム入れる共通時刻（全員で同じ波になる）

            float OceanTime() { return _Udon_OceanTime > 0.0 ? _Udon_OceanTime : _Time.y; }

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 wpos : TEXCOORD0;
                float3 nrm : TEXCOORD1;
                float h : TEXCOORD2;
                float fade : TEXCOORD3;
                UNITY_FOG_COORDS(4)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float3 Gerstner(float4 w, float2 p, float dist, inout float3 tng, inout float3 bin)
            {
                float fade = 1.0 - smoothstep(0.0, 1.0, dist / (w.w * _FadeMul));
                float steep = w.z * _WaveScale * fade;
                float k = 6.28318 / w.w;
                float c = sqrt(9.8 / k);
                float2 d = normalize(w.xy);
                float f = k * (dot(d, p) - c * OceanTime());
                float a = steep / k;
                float sf = sin(f);
                float cf = cos(f);
                tng += float3(-d.x * d.x * steep * sf, d.x * steep * cf, -d.x * d.y * steep * sf);
                bin += float3(-d.x * d.y * steep * sf, d.y * steep * cf, -d.y * d.y * steep * sf);
                return float3(d.x * a * cf, a * sf, d.y * a * cf);
            }

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 center = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
                float dist = distance(wp.xz, center.xz);
                float3 tng = float3(1, 0, 0);
                float3 bin = float3(0, 0, 1);
                float3 off = Gerstner(_WaveA, wp.xz, dist, tng, bin)
                           + Gerstner(_WaveB, wp.xz, dist, tng, bin)
                           + Gerstner(_WaveC, wp.xz, dist, tng, bin);
                wp += off;
                o.nrm = normalize(cross(bin, tng));
                o.h = off.y;
                o.fade = 1.0 - smoothstep(0.0, 1.0, dist / (_WaveB.w * _FadeMul));
                o.wpos = wp;
                o.pos = mul(UNITY_MATRIX_VP, float4(wp, 1));
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            float H(float2 uv) { return tex2D(_NoiseTex, uv).r; }

            fixed4 frag (v2f i, fixed facing : VFACE) : SV_Target
            {
                if (facing < 0)
                {
                    // 水中から見上げた面
                    fixed4 uc = _UnderColor;
                    UNITY_APPLY_FOG(i.fogCoord, uc);
                    return uc;
                }

                float3 V = normalize(_WorldSpaceCameraPos - i.wpos);
                float dist = distance(_WorldSpaceCameraPos, i.wpos);

                // 細かい波（表面の向きだけ揺らす）
                float tm = OceanTime();
                float2 uv1 = i.wpos.xz / _DetailScale + tm * float2(0.02, 0.012);
                float2 uv2 = i.wpos.xz / (_DetailScale * 2.7) - tm * float2(0.011, 0.017);
                float e = 0.02;
                float h1 = H(uv1), h2 = H(uv2);
                float2 g = float2(h1 - H(uv1 + float2(e, 0)), h1 - H(uv1 + float2(0, e)))
                         + 0.6 * float2(h2 - H(uv2 + float2(e, 0)), h2 - H(uv2 + float2(0, e)));
                float ds = _DetailStrength * 8.0 * saturate(1.0 - dist / 3000.0);
                float3 n = normalize(normalize(i.nrm) + float3(g.x * ds, 0, g.y * ds));

                // 空の映り込み
                float ndv = saturate(dot(n, V));
                float fres = 0.02 + 0.98 * pow(1.0 - ndv, 5.0);
                float3 R = reflect(-V, n);
                R.y = abs(R.y);
                half4 env = UNITY_SAMPLE_TEXCUBE(unity_SpecCube0, R);
                half3 sky = DecodeHDR(env, unity_SpecCube0_HDR);

                // 水の色（波頭ほど明るい青緑）
                float crest = saturate(i.h / 2.5 * 0.5 + 0.5);
                float3 L = normalize(_WorldSpaceLightPos0.xyz);
                float3 light = saturate(_LightColor0.rgb * saturate(L.y + 0.2) + unity_AmbientSky.rgb * 0.5);
                float3 water = lerp(_DeepColor.rgb, _ShallowColor.rgb, crest * crest) * light;
                float3 col = lerp(water, sky, fres);

                // 太陽の照り返し
                float3 Hv = normalize(L + V);
                col += _LightColor0.rgb * pow(saturate(dot(n, Hv)), _SpecPower) * _SpecStrength;

                // 波頭の泡
                float foamN = H(i.wpos.xz / 9.0 + tm * 0.03);
                float foam = saturate((crest - (1.0 - _FoamAmount)) / max(_FoamAmount, 0.01))
                           * smoothstep(0.45, 0.75, foamN) * i.fade;
                col = lerp(col, _FoamColor.rgb * light, foam);

                fixed4 c = fixed4(col, 1);
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
}
