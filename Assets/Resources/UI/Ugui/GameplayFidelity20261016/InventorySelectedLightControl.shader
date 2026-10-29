// UI/Default-compatible alpha blending, stencil and RectMask2D. Original texture is unchanged.
Shader "UI/Qdao/InventorySelectedLightControl"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _SourceSize ("Unpacked Source Size", Vector) = (192,147,0,0)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="False"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 mask : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            float4 _Color;
            float4 _SourceSize;
            float4 _TextureSampleAdd;
            float4 _ClipRect;
            float _UIMaskSoftnessX;
            float _UIMaskSoftnessY;
            int _UIVertexColorAlwaysGammaSpace;

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                if (_UIVertexColorAlwaysGammaSpace && !IsGammaSpace())
                    v.color.rgb = UIGammaToLinear(v.color.rgb);
                o.color = v.color * _Color;
                float2 pixelSize = o.vertex.w;
                pixelSize /= abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
                float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
                o.mask = float4(v.vertex.xy * 2 - clampedRect.xy - clampedRect.zw,
                    0.25 / (0.25 * half2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize.xy)));
                return o;
            }

            float GreenEvidence(float3 c)
            {
                return smoothstep(0.012, 0.06, c.g - max(c.r, c.b));
            }

            float3 WorkingColor(float3 sampled)
            {
                #ifndef UNITY_COLORSPACE_GAMMA
                return LinearToGammaSpace(sampled);
                #else
                return sampled;
                #endif
            }

            float SampleGreen(float2 uv)
            {
                return GreenEvidence(WorkingColor((tex2D(_MainTex, saturate(uv)) + _TextureSampleAdd).rgb));
            }

            float4 frag(v2f i) : SV_Target
            {
                // Use the PNG's sRGB domain for both the mask and its RGB scale, before tint.
                float4 source = tex2D(_MainTex, i.texcoord) + _TextureSampleAdd;
                float3 working = WorkingColor(source.rgb);
                float green = GreenEvidence(working);
                float2 stepUV = abs(_MainTex_TexelSize.xy);
                float nearGreen = green;
                nearGreen = max(nearGreen, SampleGreen(i.texcoord + float2(stepUV.x, 0)));
                nearGreen = max(nearGreen, SampleGreen(i.texcoord - float2(stepUV.x, 0)));
                nearGreen = max(nearGreen, SampleGreen(i.texcoord + float2(0, stepUV.y)));
                nearGreen = max(nearGreen, SampleGreen(i.texcoord - float2(0, stepUV.y)));
                nearGreen = max(nearGreen, SampleGreen(i.texcoord + float2(2 * stepUV.x, 0)));
                nearGreen = max(nearGreen, SampleGreen(i.texcoord - float2(2 * stepUV.x, 0)));
                nearGreen = max(nearGreen, SampleGreen(i.texcoord + float2(0, 2 * stepUV.y)));
                nearGreen = max(nearGreen, SampleGreen(i.texcoord - float2(0, 2 * stepUV.y)));
                float2 pixel = i.texcoord * _SourceSize.xy - 0.5;
                float edgeDistance = min(min(pixel.x, _SourceSize.x - 1 - pixel.x),
                    min(pixel.y, _SourceSize.y - 1 - pixel.y));
                float band = 1 - smoothstep(10, 17, edgeDistance);
                float white = smoothstep(0.76, 0.94, min(working.r, min(working.g, working.b)));
                float mask = band * max(green, white * nearGreen);
                // Preserve untouched sampled RGB exactly in the shader path;
                // avoid a gamma round trip from tiny positive smoothstep residue at the zero boundary.
                if (mask > 0.000001)
                {
                    working *= 1 - 0.36 * mask;
                    #ifndef UNITY_COLORSPACE_GAMMA
                    source.rgb = GammaToLinearSpace(working);
                    #else
                    source.rgb = working;
                    #endif
                }
                // Match Unity 6000.6 UI/Default's vertex-alpha precision and premultiplied output.
                i.color.a = round(i.color.a * 255.0) / 255.0;
                float4 color = source * i.color;

                #ifdef UNITY_UI_CLIP_RECT
                half2 clipping = saturate((_ClipRect.zw - _ClipRect.xy - abs(i.mask.xy)) * i.mask.zw);
                color.a *= clipping.x * clipping.y;
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif
                color.rgb *= color.a;
                return color;
            }
            ENDCG
        }
    }
    Fallback "UI/Default"
}
