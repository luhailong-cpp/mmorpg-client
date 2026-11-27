// 角色/宠物/NPC 双 Pass 遮挡透视着色器（单变体、WebGL2 / IL2CPP 兼容）：
// Pass 0 (ZTest LEqual)：未被前景遮挡的像素按正常不透明度绘制（同时兼容区域半透明 color.a）；
// Pass 1 (ZTest Greater)：被前景深度遮挡的像素按 min(color.a, _OccludedAlpha) 绘制半透明人影。
// DisableBatching 保证 Built-in RP Transparent 队列严格按单角色 sortingOrder 成对执行双 Pass，
// 避免屋后角色的半透明人影反盖站在屋前的其他角色。
Shader "World/ActorOcclusionSprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _OccludedAlpha ("Occluded Alpha", Range(0, 1)) = 0.45
        _OccludedTint ("Occluded Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
            "DisableBatching"="True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "ACTOR_NORMAL"
            ZTest LEqual

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.texcoord);
                return tex * i.color;
            }
            ENDCG
        }

        Pass
        {
            Name "ACTOR_OCCLUDED_SILHOUETTE"
            ZTest Greater

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed _OccludedAlpha;
            fixed4 _OccludedTint;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.texcoord);
                fixed alpha = tex.a * min(i.color.a, _OccludedAlpha);
                return fixed4(tex.rgb * i.color.rgb * _OccludedTint.rgb, alpha);
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
