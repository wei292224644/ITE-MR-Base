// 与 RoundedBoxUIProperties 配套的 UI 着色器。
//
// 顶点通道约定：
//   TEXCOORD1 = float4(左上, 右上, 右下, 左下) 半径
//   TEXCOORD2 = float4(宽, 高, 矩形内归一化 x, 矩形内归一化 y)
//
// 圆角用 SDF 在片元里裁，而不是靠九宫格贴图——ITE 的 CornerRadius 是每个
// 富文本 / 视频资源自带的数据，九宫格做不了动态半径。
Shader "Uality/IteTour/RoundedBoxUI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

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
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
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
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 radii    : TEXCOORD1;
                float4 rectInfo : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                float4 radii         : TEXCOORD2;
                float4 rectInfo      : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                OUT.radii = v.radii;
                OUT.rectInfo = v.rectInfo;
                return OUT;
            }

            // 每角半径的圆角矩形 SDF。p 以矩形中心为原点，返回值 <0 在内、>0 在外。
            float RoundedBoxSDF(float2 p, float2 halfSize, float4 radii)
            {
                float r = p.x > 0.0 ? (p.y > 0.0 ? radii.y : radii.z)
                                    : (p.y > 0.0 ? radii.x : radii.w);
                r = min(r, min(halfSize.x, halfSize.y));

                float2 q = abs(p) - halfSize + r;
                return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 color = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd) * IN.color;

                float2 size = IN.rectInfo.xy;
                float2 p = (IN.rectInfo.zw - 0.5) * size;
                float d = RoundedBoxSDF(p, size * 0.5, IN.radii);

                // 用屏幕空间导数取一像素宽的过渡带，避免半径边缘出现锯齿
                float aa = max(fwidth(d), 1e-5);
                color.a *= 1.0 - smoothstep(-aa, aa, d);

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip (color.a - 0.001);
                #endif

                return color;
            }
        ENDCG
        }
    }
}
