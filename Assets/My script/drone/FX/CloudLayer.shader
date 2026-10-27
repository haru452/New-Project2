// 空一面の雲の層（Built-in / VRChat 用）
//  ・ワールド座標で雲の模様を貼るので、どれだけ大きくしても模様は細かいまま
//  ・模様はゆっくり流れる
//  ・上から見た面は白、下から見た面は灰色
//  ・_MinAlpha を高くすると、雲の向こうがほとんど見えなくなる
Shader "FlightUI/CloudLayer"
{
    Properties
    {
        _MainTex ("Cloud Noise", 2D) = "white" {}
        _Scale ("Noise Scale (m)", Float) = 1800
        _Scale2 ("Detail Scale (m)", Float) = 450
        _TopColor ("Top Color", Color) = (1, 1, 1, 1)
        _BottomColor ("Bottom Color", Color) = (0.62, 0.66, 0.72, 1)
        _Density ("Density", Range(0, 1)) = 1
        _MinAlpha ("Min Alpha", Range(0, 1)) = 0.96
        _Coverage ("Coverage", Range(0, 1)) = 0.4
        _Speed ("Wind Speed", Vector) = (0.004, 0.002, 0, 0)
        _EdgeFade ("Edge Fade", Range(0.01, 1)) = 0.3
        [Toggle] _ZWriteOn ("ZWrite", Float) = 1
        [Toggle] _Round ("Round Edge", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent-20" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite [_ZWriteOn]
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Scale, _Scale2, _Density, _MinAlpha, _Coverage, _EdgeFade, _Round;
            float4 _Speed;
            fixed4 _TopColor, _BottomColor;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 wpos : TEXCOORD1;
                UNITY_FOG_COORDS(2)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag (v2f i, fixed facing : VFACE) : SV_Target
            {
                float2 p = i.wpos.xz;
                float n1 = tex2D(_MainTex, p / _Scale + _Time.y * _Speed.xy).r;
                float n2 = tex2D(_MainTex, p / _Scale2 - _Time.y * _Speed.xy * 1.7).r;
                float n = n1 * 0.7 + n2 * 0.3;

                float cov = smoothstep(_Coverage, _Coverage + 0.25, n);
                float a = _Density * max(_MinAlpha, cov);

                float2 d = abs(i.uv - 0.5) * 2.0;
                float edge = _Round > 0.5 ? length(d) : max(d.x, d.y);   // 丸い雲は円形に薄くなる
                a *= 1.0 - smoothstep(1.0 - _EdgeFade, 1.0, edge);

                fixed4 baseCol = facing > 0 ? _TopColor : _BottomColor;
                fixed4 c = fixed4(baseCol.rgb * lerp(0.82, 1.06, n), a);
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
}
