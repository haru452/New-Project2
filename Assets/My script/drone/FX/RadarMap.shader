// レーダーに島を映す: 世界の地図（上から見た陸の形）を、自分の位置と機首の向きに合わせて丸く切り抜いて表示
Shader "FlightUI/RadarMap"
{
    Properties
    {
        _MainTex ("World map (land)", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Center ("Center UV (xy)", Vector) = (0.5,0.5,0,0)
        _Dir ("Heading (x = fwd.x, y = fwd.z)", Vector) = (0,1,0,0)
        _Scale ("Range / map size", Float) = 0.1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _Color, _Center, _Dir;
            float _Scale;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                float2 p = (i.uv - 0.5) * 2.0;          // -1〜1（1 = レーダーの範囲の端）
                if (dot(p, p) > 1.0) discard;
                float2 f = _Dir.xy;                       // 機首の向き（世界の x, z）
                float2 r = float2(f.y, -f.x);             // 右
                float2 w = (p.x * r + p.y * f) * _Scale;  // 世界での位置（地図の大きさで割った値）
                float2 uv = _Center.xy + w;
                if (uv.x < 0 || uv.y < 0 || uv.x > 1 || uv.y > 1) discard;
                fixed4 c = tex2D(_MainTex, uv) * _Color;
                if (c.a < 0.01) discard;
                return c;
            }
            ENDCG
        }
    }
}
