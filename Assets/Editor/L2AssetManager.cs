using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using L2Viewer.PackageCore;

internal static class L2AssetManager
{
    public static string SharedPackagesRoot => $"{MapImportPaths.OutputRoot}/ClientPackages";
    public static string SharedStaticMeshesRoot => $"{SharedPackagesRoot}/StaticMeshes";
    public static string SharedMaterialsRoot => $"{SharedPackagesRoot}/Materials";
    public static string SharedTexturesRoot => $"{SharedPackagesRoot}/Textures";
    public static string SharedTerrainLayersRoot => $"{SharedPackagesRoot}/TerrainLayers";

    public static Texture2D CreateTextureAsset(TextureData textureData, string assetPath, bool linear, bool generateAlphaIfMissing = false)
    {
        if (textureData == null)
            throw new InvalidOperationException($"Could not decode texture asset: {assetPath}");

        var texture = new Texture2D(textureData.Width, textureData.Height, TextureFormat.RGBA32, false, linear)
        {
            name = Path.GetFileNameWithoutExtension(assetPath)
        };

        try
        {
            var bytes = textureData.RgbaBytes;
            if (bytes != null && bytes.Length > 0)
            {
                // Flip texture vertically (Unity expects bottom-up, raw is top-down)
                int width = textureData.Width;
                int height = textureData.Height;
                int rowPitch = width * 4;
                byte[] flippedBytes = new byte[bytes.Length];
                for (int y = 0; y < height; y++)
                {
                    int srcOffset = y * rowPitch;
                    int dstOffset = (height - 1 - y) * rowPitch;
                    Array.Copy(bytes, srcOffset, flippedBytes, dstOffset, rowPitch);
                }
                bytes = flippedBytes;

                if (generateAlphaIfMissing)
                {
                    bool hasAlpha = false;
                    for (int i = 3; i < bytes.Length; i += 4)
                    {
                        if (bytes[i] < 255)
                        {
                            hasAlpha = true;
                            break;
                        }
                    }

                    if (!hasAlpha)
                    {
                        // Generate alpha from grayscale (luminance)
                        for (int i = 0; i < bytes.Length; i += 4)
                        {
                            byte r = bytes[i];
                            byte g = bytes[i + 1];
                            byte b = bytes[i + 2];
                            byte lum = (byte)(0.299f * r + 0.587f * g + 0.114f * b);
                            bytes[i + 3] = lum;
                        }
                    }
                }
            }

            texture.LoadRawTextureData(bytes);
            texture.Apply(false, false);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Failed to load texture data for {assetPath}: {ex.Message}");
        }

        byte[] pngBytes = texture.EncodeToPNG();
        File.WriteAllBytes(assetPath, pngBytes);
        UnityEngine.Object.DestroyImmediate(texture); // Prevent memory leak which causes PNG encoding to fail
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
    }

    public static void EnsureFolderExists(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath) || AssetDatabase.IsValidFolder(assetPath)) 
            return;
            
        var folders = assetPath.Replace('\\', '/').Split('/');
        string path = folders[0];
        for (int i = 1; i < folders.Length; i++)
        {
            string newPath = path + "/" + folders[i];
            if (!AssetDatabase.IsValidFolder(newPath))
            {
                AssetDatabase.CreateFolder(path, folders[i]);
            }
            path = newPath;
        }
    }

    public static void EnsureParentFolderExists(string assetPath)
    {
        var normalized = assetPath.Replace('\\', '/');
        var slashIndex = normalized.LastIndexOf('/');
        if (slashIndex <= 0)
        {
            return;
        }

        EnsureFolderExists(normalized.Substring(0, slashIndex));
    }

    public static string BuildClientPackageAssetPath(string root, string referenceText, string prefix, string extension, string fallbackCategory, string suffix = null)
    {
        var normalizedRoot = root.Replace('\\', '/');
        var parts = (referenceText ?? string.Empty)
            .Split(new[] { '.', '/', '\\', ':' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(AssetNameUtility.SanitizeName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        string fileStem;
        string directory = normalizedRoot;
        if (parts.Length == 0)
        {
            directory = $"{normalizedRoot}/{AssetNameUtility.SanitizeName(fallbackCategory)}";
            fileStem = AssetNameUtility.SanitizeName(prefix);
        }
        else
        {
            if (parts.Length > 1)
            {
                directory = $"{normalizedRoot}/{string.Join("/", parts.Take(parts.Length - 1))}";
            }

            fileStem = parts[parts.Length - 1];
        }

        EnsureFolderExists(directory);
        var suffixPart = string.IsNullOrWhiteSpace(suffix) ? string.Empty : $"_{AssetNameUtility.SanitizeName(suffix)}";
        return $"{directory}/{prefix}_{fileStem}{suffixPart}.{extension.TrimStart('.')}";
    }

    public static string BuildReferenceText(string packageName, string objectName, string fallback = null)
    {
        if (!string.IsNullOrWhiteSpace(packageName) && !string.IsNullOrWhiteSpace(objectName))
        {
            return $"{packageName}.{objectName}";
        }

        if (!string.IsNullOrWhiteSpace(objectName))
        {
            return objectName;
        }

        if (!string.IsNullOrWhiteSpace(packageName))
        {
            return packageName;
        }

        return fallback ?? "ImportedAsset";
    }

    public static void ApplyMaterialTraits(Material material, L2Viewer.SceneDomain.Services.MaterialKnownTraits traits, bool isHdrp)
    {
        material.enableInstancing = true;

        var pipeline = L2MaterialUtility.DetectPipeline(material.shader);

        if (pipeline == L2MaterialUtility.PipelineKind.HighDefinition)
        {
            material.SetFloat("_Smoothness", traits.HasSpecularInput ? 0.75f : 0.25f);
        }
        else
        {
            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", traits.HasSpecularInput ? 0.75f : 0.25f);
            }

            if (material.HasProperty("_Glossiness"))
            {
                material.SetFloat("_Glossiness", traits.HasSpecularInput ? 0.75f : 0.25f);
            }
        }

        if (traits == null || traits.BlendModeHint == L2Viewer.SceneDomain.Services.MaterialBlendModeHint.Unknown)
            return;

        if (traits.TwoSided == true)
        {
            if (material.HasProperty("_DoubleSidedEnable"))
            {
                material.SetFloat("_DoubleSidedEnable", 1f);
            }

            if (material.HasProperty("_Cull"))
            {
                material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            }

            material.doubleSidedGI = true;
        }

        if (traits.BlendModeHint == L2Viewer.SceneDomain.Services.MaterialBlendModeHint.Masked || traits.HasMaskInput || traits.HasOpacityInput)
        {
            L2MaterialUtility.ConfigureCutout(material);
        }

        if (traits.BlendModeHint == L2Viewer.SceneDomain.Services.MaterialBlendModeHint.Translucent)
        {
            L2MaterialUtility.ConfigureTransparent(
                material,
                UnityEngine.Rendering.BlendMode.SrcAlpha,
                UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha,
                premultiplyKeyword: pipeline == L2MaterialUtility.PipelineKind.Legacy);
        }
        else if (traits.BlendModeHint == L2Viewer.SceneDomain.Services.MaterialBlendModeHint.Additive)
        {
            L2MaterialUtility.ConfigureTransparent(
                material,
                UnityEngine.Rendering.BlendMode.SrcAlpha,
                UnityEngine.Rendering.BlendMode.One,
                premultiplyKeyword: pipeline == L2MaterialUtility.PipelineKind.Legacy);
        }
        else if (traits.BlendModeHint == L2Viewer.SceneDomain.Services.MaterialBlendModeHint.Modulated)
        {
            L2MaterialUtility.ConfigureTransparent(
                material,
                UnityEngine.Rendering.BlendMode.DstColor,
                UnityEngine.Rendering.BlendMode.Zero,
                premultiplyKeyword: pipeline == L2MaterialUtility.PipelineKind.Legacy,
                modulateKeyword: pipeline == L2MaterialUtility.PipelineKind.Universal);
        }

        if (pipeline == L2MaterialUtility.PipelineKind.HighDefinition)
        {
            if (traits.BlendModeHint == L2Viewer.SceneDomain.Services.MaterialBlendModeHint.Translucent ||
                traits.BlendModeHint == L2Viewer.SceneDomain.Services.MaterialBlendModeHint.Modulated)
            {
                material.SetFloat("_BlendMode", 0f);
            }

            if (traits.BlendModeHint == L2Viewer.SceneDomain.Services.MaterialBlendModeHint.Additive)
            {
                material.SetFloat("_BlendMode", 2f);
                material.EnableKeyword("_BLENDMODE_ADD");
            }

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }

        if (traits.HasSelfIlluminationInput)
        {
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            material.EnableKeyword("_EMISSION");
            if (material.HasProperty("_UseEmissiveIntensity"))
            {
                material.SetFloat("_UseEmissiveIntensity", 1f);
            }
        }
    }
}



