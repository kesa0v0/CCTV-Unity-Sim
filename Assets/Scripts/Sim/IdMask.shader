// VisibilityMeter 전용: 오브젝트를 조명 없이 _IdColor 단색으로 그린다.
Shader "Hidden/Sim/IdMask"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            Cull Off
            ZWrite On
            ZTest LEqual

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _IdColor;

            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            float4 frag() : SV_Target { return _IdColor; }
            ENDCG
        }
    }
}
