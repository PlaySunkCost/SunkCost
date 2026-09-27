// The console screens' paint (the shared console, 27 September 2026): what
// ScreenPainter draws its quads with, into each surface's RenderTexture. Unlit,
// alpha-blended, no depth: the vertex colour, times the texture. _AlphaOnly 1
// takes only the texture's alpha (the font atlas, a silhouette tinted by the
// vertex colour); 0 multiplies the texture's colour in (a colour picture of Dan's).
Shader "Sunk Cost/Screen Paint"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _AlphaOnly ("Alpha only", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Lighting Off Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata_t { float4 vertex : POSITION; float4 color : COLOR; float2 texcoord : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float4 color : COLOR; float2 texcoord : TEXCOORD0; };
            sampler2D _MainTex; float _AlphaOnly;
            v2f vert (appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.texcoord = v.texcoord;
                return o;
            }
            float4 frag (v2f i) : SV_Target
            {
                float4 t = tex2D(_MainTex, i.texcoord);
                float4 c = i.color;
                c.rgb *= lerp(t.rgb, float3(1, 1, 1), _AlphaOnly);
                c.a *= t.a;
                return c;
            }
            ENDCG
        }
    }
}
