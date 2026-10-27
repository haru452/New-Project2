// 姿勢指示器（人工水平儀）の空と地面の球。丸く切り抜き、機体の傾き（_Roll 度）と機首の上げ下げ（_Pitch 度）で動く
Shader "FlightUI/AttitudeBall"
{
    Properties
    {
        _MainTex ("Sky/Ground", 2D) = "white" {}
        _Pitch ("Pitch", Float) = 0
        _Roll ("Roll", Float) = 0
        _Span ("Visible span", Float) = 0.5
    }
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float _Pitch, _Roll, _Span;
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
                float2 p = i.uv - 0.5;
                if (length(p) > 0.5) discard;
                float r = radians(_Roll);
                float s = sin(r), c = cos(r);
                float2 q = float2(c * p.x + s * p.y, -s * p.x + c * p.y);
                float2 t = float2(q.x + 0.5, 0.5 + _Pitch / 180.0 + q.y * _Span);
                return tex2D(_MainTex, t);
            }
            ENDCG
        }
    }
}
