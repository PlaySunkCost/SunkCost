// The sign that something can be used (SHIP-054): drawn over what the aiming dot
// rests on, for the owner's camera only (InteractHighlight). A soft warm rim at
// grazing angles, a faint lift over the whole surface, and on a flat panel (the TV
// screen, a quad) a thin frame just inside its edge, where a rim cannot show.
// Additive, no depth write, fogged to nothing like the surface under it. URP draws
// it as an SRPDefaultUnlit pass. In Resources so a build keeps it.
Shader "Sunk Cost/Interact Highlight"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 0.82, 0.5, 1)
        _Rim ("Rim", Float) = 0.22
        _Lift ("Lift", Float) = 0.02
        _Edge ("Edge frame", Float) = 0
        _EdgeUV ("Edge width in UV (x, y)", Vector) = (0.02, 0.02, 0, 0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Cull Back ZWrite Off ZTest LEqual
        Offset -1, -1
        Blend One One
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            struct appdata_t { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float3 normal : TEXCOORD0; float3 view : TEXCOORD1; float2 uv : TEXCOORD2; UNITY_FOG_COORDS(3) };
            fixed4 _Color; float _Rim, _Lift, _Edge; float4 _EdgeUV;
            v2f vert (appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.view = WorldSpaceViewDir(v.vertex);
                o.uv = v.uv;
                UNITY_TRANSFER_FOG(o, o.vertex);
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                float facing = saturate(dot(normalize(i.normal), normalize(i.view)));
                float rim = pow(1.0 - facing, 4.0);
                float2 d = min(i.uv, 1.0 - i.uv) / max(_EdgeUV.xy, 1e-4);
                float edge = _Edge * (1.0 - smoothstep(0.0, 1.0, min(d.x, d.y)));
                float breathe = 0.85 + 0.15 * sin(_Time.y * 2.5); // alive, never a blink
                fixed4 col = _Color * ((_Lift + _Rim * rim + edge) * breathe);
                col.a = 1;
                UNITY_APPLY_FOG_COLOR(i.fogCoord, col, fixed4(0, 0, 0, 0));
                return col;
            }
            ENDCG
        }
    }
}
