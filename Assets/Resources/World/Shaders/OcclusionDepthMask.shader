// 地图前景遮挡纯深度写入着色器（无颜色输出、无贴图依赖、单变体）：
// 在底图之后（Queue=Geometry+10）将前景建筑/树木轮廓按接地线映射后的顶视深度写入 Z-Buffer，
// 供角色双 Pass 着色器在 WebGL2 / GLES3 / Metal / Vulkan / DX 下精确区分未遮挡与被遮挡像素。
Shader "World/OcclusionDepthMask"
{
    SubShader
    {
        Tags
        {
            "Queue"="Geometry+10"
            "RenderType"="Opaque"
            "IgnoreProjector"="True"
        }

        ColorMask 0
        ZWrite On
        ZTest LEqual
        Cull Off
        Lighting Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
            };

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return 0;
            }
            ENDCG
        }
    }
}
