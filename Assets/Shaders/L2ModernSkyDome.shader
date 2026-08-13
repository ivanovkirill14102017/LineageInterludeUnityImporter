Shader "L2/Modern Sky Dome"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.46, 0.64, 0.92, 1)
        _MainTex ("Texture", 2D) = "white" {}
        _TextureBlend ("Texture Blend", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" }
        Cull Off
        ZWrite Off
        ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _BaseColor;
            half _TextureBlend;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 textureColor = tex2D(_MainTex, i.uv);
                fixed4 color = lerp(_BaseColor, textureColor * _BaseColor, saturate(_TextureBlend));
                color.a = 1;
                return color;
            }
            ENDCG
        }
    }
}
