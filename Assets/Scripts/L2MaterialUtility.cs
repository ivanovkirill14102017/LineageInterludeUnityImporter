using UnityEngine;
using UnityEngine.Rendering;

public static class L2MaterialUtility
{
    public enum PipelineKind
    {
        Legacy,
        Universal,
        HighDefinition
    }

    public static Shader FindBestLitShader()
    {
        return Shader.Find("Universal Render Pipeline/Lit")
               ?? Shader.Find("Universal Render Pipeline/Simple Lit")
               ?? Shader.Find("Standard");
    }

    public static Shader FindBestUnlitShader()
    {
        return Shader.Find("Universal Render Pipeline/Unlit")
               ?? Shader.Find("Unlit/Color");
    }

    public static PipelineKind DetectPipeline(Shader shader)
    {
        var shaderName = shader != null ? shader.name : string.Empty;
        if (shaderName.StartsWith("Universal Render Pipeline/", System.StringComparison.Ordinal))
        {
            return PipelineKind.Universal;
        }

        return PipelineKind.Legacy;
    }

    public static bool IsHdrp(Shader shader)
    {
        return DetectPipeline(shader) == PipelineKind.HighDefinition;
    }

    public static void AssignMainTexture(Material material, Texture texture)
    {
        if (material == null || texture == null)
        {
            return;
        }

        if (material.HasProperty("_BaseColorMap"))
        {
            material.SetTexture("_BaseColorMap", texture);
        }

        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", texture);
        }

        if (material.HasProperty("_MainTex"))
        {
            material.SetTexture("_MainTex", texture);
        }
    }

    public static void AssignMainTextureOffset(Material material, Vector2 offset)
    {
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_BaseColorMap"))
        {
            material.SetTextureOffset("_BaseColorMap", offset);
        }

        if (material.HasProperty("_BaseMap"))
        {
            material.SetTextureOffset("_BaseMap", offset);
        }

        if (material.HasProperty("_MainTex"))
        {
            material.SetTextureOffset("_MainTex", offset);
        }
    }

    public static void SetBaseColor(Material material, Color color)
    {
        if (material == null)
        {
            return;
        }

        material.color = color;

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
    }

    public static string GetPrimaryTexturePropertyName(Material material)
    {
        if (material == null)
        {
            return "_MainTex";
        }

        if (material.HasProperty("_BaseColorMap"))
        {
            return "_BaseColorMap";
        }

        if (material.HasProperty("_BaseMap"))
        {
            return "_BaseMap";
        }

        return "_MainTex";
    }

    public static void ConfigureCutout(Material material)
    {
        if (material == null)
        {
            return;
        }

        material.SetOverrideTag("RenderType", "TransparentCutout");
        if (material.HasProperty("_AlphaClip"))
        {
            material.SetFloat("_AlphaClip", 1f);
        }

        if (material.HasProperty("_AlphaCutoffEnable"))
        {
            material.SetFloat("_AlphaCutoffEnable", 1f);
        }

        if (material.HasProperty("_Cutoff"))
        {
            material.SetFloat("_Cutoff", 0.5f);
        }

        material.SetInt("_SrcBlend", (int)BlendMode.One);
        material.SetInt("_DstBlend", (int)BlendMode.Zero);
        material.SetInt("_ZWrite", 1);
        material.EnableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.DisableKeyword("_ALPHAMODULATE_ON");
        material.renderQueue = (int)RenderQueue.AlphaTest;
    }

    public static void ConfigureTransparent(Material material, BlendMode srcBlend, BlendMode dstBlend, bool premultiplyKeyword, bool modulateKeyword = false)
    {
        if (material == null)
        {
            return;
        }

        material.SetOverrideTag("RenderType", "Transparent");
        if (material.HasProperty("_SurfaceType"))
        {
            material.SetFloat("_SurfaceType", 1f);
        }

        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
        }

        material.SetInt("_SrcBlend", (int)srcBlend);
        material.SetInt("_DstBlend", (int)dstBlend);
        material.SetInt("_ZWrite", 0);
        material.DisableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_ALPHABLEND_ON");

        if (premultiplyKeyword)
        {
            material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        }
        else
        {
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        }

        if (modulateKeyword)
        {
            material.EnableKeyword("_ALPHAMODULATE_ON");
        }
        else
        {
            material.DisableKeyword("_ALPHAMODULATE_ON");
        }

        material.renderQueue = (int)RenderQueue.Transparent;
    }
}

