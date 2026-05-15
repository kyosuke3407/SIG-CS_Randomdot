Shader "Unlit/RDS_Subpixel"
{
    Properties
    {
        _EyeSign ("Eye Sign (+1 = Left, -1 = Right)", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        LOD 100

        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "RDSUnlit"

            Tags
            {
                "LightMode" = "UniversalForward"
            }

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // =====================================================
            // Material側で設定する値
            // =====================================================
            float _EyeSign;

            // =====================================================
            // C#側から受け取る値
            // Propertiesには書かない
            // =====================================================
            float _WidthPx;
            float _HeightPx;
            float _Pp;

            float _CircleCenterXPx;
            float _CircleCenterYPx;
            float _CircleRadiusPx;

            float _HalfDisparityPx;

            float _PWhite;
            float _BackgroundSeed;
            float _ObjectSeed;
            float _GrayscaleDots;

            float _ShowCircleGuide;

            float _CropEnabled;
            float _CropHalfWidthMm;
            float _CropHalfHeightMm;

            float _VirtualOffsetXPx;
            float _VirtualOffsetYPx;
            float _FadeLevel;
            float _DebugSolidCircle;
            float _IsResting;
            float4 _RestColor;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            // =====================================================
            // PCG風 uint hash
            // sin(dot())を使わず，整数セル座標から乱数を作る
            // =====================================================

            uint PcgHash(uint x)
            {
                uint state = x * 747796405u + 2891336453u;
                uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
                return (word >> 22u) ^ word;
            }

            uint Hash2D(uint2 p, uint seed)
            {
                uint h = seed;
                h ^= PcgHash(p.x + 0x9E3779B9u);
                h ^= PcgHash(p.y + 0x85EBCA6Bu);
                return PcgHash(h);
            }

            float Random01(uint2 cell, uint seed)
            {
                uint h = Hash2D(cell, seed);
                return (float)(h & 0x00FFFFFFu) / 16777215.0;
            }

            float RandomDot(uint2 cell, uint seed, float pWhite, float useGrayscale)
            {
                float r = Random01(cell, seed);
                if (useGrayscale > 0.5)
                {
                    return r; // グレースケールノイズ
                }
                else
                {
                    return (r < pWhite) ? 1.0 : 0.0; // 2値ノイズ
                }
            }

            uint SeedToUint(float seed)
            {
                return (uint)max(seed, 0.0);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float widthPx = max(_WidthPx, 1.0);
                float heightPx = max(_HeightPx, 1.0);
                float pp = max(_Pp, 0.000001);

                // =================================================
                // UV -> 実画面pixel座標
                // pixel.x: 左から右
                // pixel.y: 上から下
                // =================================================
                float2 pixel;
                pixel.x = input.uv.x * widthPx;
                pixel.y = (1.0 - input.uv.y) * heightPx;

                // =================================================
                // 仮想曲面ディスプレイ座標
                //
                // displayNum=1の平面条件では，
                // C#側から _VirtualOffsetXPx / _VirtualOffsetYPx が入る．
                //
                // 実画面上では仮想ディスプレイ全体がoffset方向へ動く．
                // そのため，描画内容の生成には offset を引いた座標を使う．
                // =================================================
                float2 virtualPixel = pixel - float2(_VirtualOffsetXPx, _VirtualOffsetYPx);

                // =================================================
                // 物理サイズベースのクロップ
                //
                // 描画内容は縮小せず，
                // 仮想曲面ディスプレイ座標上のmm座標が，
                // 曲面ディスプレイ実寸の範囲外なら黒にする．
                // =================================================
                if (_CropEnabled > 0.5)
                {
                    float xMm = (virtualPixel.x - widthPx * 0.5) * pp;
                    float yMm = (heightPx * 0.5 - virtualPixel.y) * pp;

                    bool outside =
                        (abs(xMm) > _CropHalfWidthMm) ||
                        (abs(yMm) > _CropHalfHeightMm);

                    if (outside)
                    {
                        return half4(0.0, 0.0, 0.0, 1.0);
                    }
                }

                if (_IsResting > 0.5)
                {
                    return half4(_RestColor.r, _RestColor.g, _RestColor.b, 1.0);
                }

                // 1 px単位のランダムドット
                // モアレが残る場合は 2.0 などに変更
                float dotSizePx = 1.0;

                // =================================================
                // 背景ドット (サブピクセル補間)
                // =================================================
                float2 pBg = virtualPixel / dotSizePx;
                float2 p0Bg = floor(pBg);
                float2 fBg = frac(pBg);
                uint seedBg = SeedToUint(_BackgroundSeed);
                
                float bg00 = RandomDot((uint2)p0Bg, seedBg, _PWhite, _GrayscaleDots);
                float bg10 = RandomDot((uint2)(p0Bg + float2(1, 0)), seedBg, _PWhite, _GrayscaleDots);
                float bg01 = RandomDot((uint2)(p0Bg + float2(0, 1)), seedBg, _PWhite, _GrayscaleDots);
                float bg11 = RandomDot((uint2)(p0Bg + float2(1, 1)), seedBg, _PWhite, _GrayscaleDots);
                
                float bg0 = lerp(bg00, bg10, fBg.x);
                float bg1 = lerp(bg01, bg11, fBg.x);
                float bg = lerp(bg0, bg1, fBg.y);

                // =================================================
                // 左右視差
                // L Material: _EyeSign = +1
                // R Material: _EyeSign = -1
                // =================================================
                float shiftPx = _EyeSign * _HalfDisparityPx;

                float2 shiftedCenter = float2(
                    _CircleCenterXPx + shiftPx,
                    _CircleCenterYPx
                );

                // =================================================
                // 円マスク
                // =================================================
                float2 diff = virtualPixel - shiftedCenter;
                float dist2 = dot(diff, diff);

                float insideCircle = step(dist2, _CircleRadiusPx * _CircleRadiusPx);

                // =================================================
                // 円内部ドット (サブピクセル補間)
                // =================================================
                float2 sourcePixel = virtualPixel - float2(shiftPx, 0.0);
                
                float2 pObj = sourcePixel / dotSizePx;
                float2 p0Obj = floor(pObj);
                float2 fObj = frac(pObj);
                uint seedObj = SeedToUint(_ObjectSeed);

                float obj00 = RandomDot((uint2)p0Obj, seedObj, _PWhite, _GrayscaleDots);
                float obj10 = RandomDot((uint2)(p0Obj + float2(1, 0)), seedObj, _PWhite, _GrayscaleDots);
                float obj01 = RandomDot((uint2)(p0Obj + float2(0, 1)), seedObj, _PWhite, _GrayscaleDots);
                float obj11 = RandomDot((uint2)(p0Obj + float2(1, 1)), seedObj, _PWhite, _GrayscaleDots);

                float o0 = lerp(obj00, obj10, fObj.x);
                float o1 = lerp(obj01, obj11, fObj.x);
                float obj = lerp(o0, o1, fObj.y);

                float value;
                if (_DebugSolidCircle > 0.5)
                {
                    // デバッグ用のベタ塗り円（円内部は白、外部は黒）
                    value = insideCircle;
                }
                else
                {
                    float valueDot = lerp(bg, obj, insideCircle);
                    value = valueDot;
                }

                // =================================================
                // デバッグ用：円の輪郭
                // =================================================
                if (_ShowCircleGuide > 0.5)
                {
                    float d = sqrt(dist2);
                    float edge = 1.0 - smoothstep(1.0, 3.0, abs(d - _CircleRadiusPx));
                    value = lerp(value, 1.0, edge * 0.7);
                }

                return half4(value * _FadeLevel, value * _FadeLevel, value * _FadeLevel, 1.0);
            }

            ENDHLSL
        }
    }

    FallBack Off
}