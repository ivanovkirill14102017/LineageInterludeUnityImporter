using UnityEngine;

[ExecuteAlways]
public class FlipbookMaterialAnimator : MonoBehaviour
{
    public Material targetMaterial;
    public string texturePropertyName = "";
    public Texture2D[] frames;
    public float frameRate = 15f;
    public bool interpolate = true;

    private RenderTexture renderTexture;
    private Material blendMaterial;
    private float currentFrame = 0f;

    void Start()
    {
        if (frames == null || frames.Length == 0)
            return;
            
        if (targetMaterial == null)
        {
            var renderer = GetComponent<Renderer>();
            if (renderer != null && renderer.sharedMaterial != null)
            {
                targetMaterial = new Material(renderer.sharedMaterial);
                renderer.material = targetMaterial;
            }
        }

        if (targetMaterial == null) return;

        if (string.IsNullOrEmpty(texturePropertyName))
        {
            texturePropertyName = L2MaterialUtility.GetPrimaryTexturePropertyName(targetMaterial);
        }

        if (interpolate && frames.Length > 1 && frames[0] != null)
        {
            var shader = Shader.Find("Hidden/FlipbookBlend");
            if (shader != null)
            {
                blendMaterial = new Material(shader);
                renderTexture = new RenderTexture(frames[0].width, frames[0].height, 0, RenderTextureFormat.ARGB32);
                renderTexture.useMipMap = true;
                renderTexture.autoGenerateMips = true;
                targetMaterial.SetTexture(texturePropertyName, renderTexture);
            }
            else
            {
                interpolate = false; 
            }
        }
    }

    void Update()
    {
        if (frames == null || frames.Length == 0 || targetMaterial == null) return;

        if (frames.Length == 1)
        {
            targetMaterial.SetTexture(texturePropertyName, frames[0]);
            return;
        }

        currentFrame += Time.deltaTime * frameRate;
        
        while (currentFrame >= frames.Length)
            currentFrame -= frames.Length;

        int frameIndex1 = Mathf.FloorToInt(currentFrame) % frames.Length;
        int frameIndex2 = (frameIndex1 + 1) % frames.Length;
        float blend = currentFrame - Mathf.Floor(currentFrame);

        if (interpolate && blendMaterial != null && renderTexture != null)
        {
            blendMaterial.SetTexture("_NextTex", frames[frameIndex2]);
            blendMaterial.SetFloat("_Blend", blend);
            Graphics.Blit(frames[frameIndex1], renderTexture, blendMaterial);
        }
        else
        {
            targetMaterial.SetTexture(texturePropertyName, frames[frameIndex1]);
        }
    }

    void OnDestroy()
    {
        if (renderTexture != null)
        {
            if (RenderTexture.active == renderTexture)
            {
                RenderTexture.active = null;
            }
            renderTexture.Release();
            if (Application.isPlaying)
                Destroy(renderTexture);
            else
                DestroyImmediate(renderTexture);
        }
        if (blendMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(blendMaterial);
            else
                DestroyImmediate(blendMaterial);
        }
    }
}
