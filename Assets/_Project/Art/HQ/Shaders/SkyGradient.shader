// The sky at six in the morning (the look card, 18 September 2026): a plain
// gradient from a navy zenith to a lighter horizon, with a warm glow low on
// one side where the sun is about to come up. No sun disc, no atmosphere
// maths: the reference sky is flat and dark and the lamps do the work.
Shader "Sunk Cost/Sky Gradient"
{
    Properties
    {
        _Zenith ("Zenith", Color) = (0.04, 0.06, 0.16, 1)
        _Horizon ("Horizon", Color) = (0.12, 0.18, 0.40, 1)
        _Glow ("Dawn glow", Color) = (0.95, 0.45, 0.15, 1)
        _GlowDirection ("Glow direction (xyz)", Vector) = (0.8, 0.0, 0.5, 0)
        _GlowWidth ("Glow width", Range(0.05, 1)) = 0.35
        _GlowHeight ("Glow height", Range(0.01, 0.5)) = 0.12
        _HorizonSharpness ("Horizon sharpness", Range(0.5, 8)) = 2.5
        _Ground ("Below the horizon", Color) = (0.05, 0.08, 0.18, 1)
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
            fixed4 _Zenith, _Horizon, _Glow, _Ground; float4 _GlowDirection; float _GlowWidth, _GlowHeight, _HorizonSharpness;
            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };
            v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.dir = v.vertex.xyz; return o; }
            fixed4 frag (v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float up = saturate(d.y);
                float t = pow(up, 1.0 / _HorizonSharpness);
                fixed4 sky = lerp(_Horizon, _Zenith, t);
                float3 g = normalize(_GlowDirection.xyz);
                float along = saturate(dot(normalize(float3(d.x, 0, d.z)), g));
                float lobe = pow(along, 1.0 / _GlowWidth);
                float low = saturate(1.0 - abs(d.y) / _GlowHeight);
                sky = lerp(sky, _Glow, lobe * low * low * 0.9);
                fixed4 ground = lerp(_Ground, _Horizon, saturate(1.0 + d.y * 12.0));
                return d.y >= 0 ? sky : ground;
            }
            ENDCG
        }
    }
}
