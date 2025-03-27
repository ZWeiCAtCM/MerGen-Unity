using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

public class PinchTogglePanorama : MonoBehaviour
{
    public InputActionReference leftSelect; // 监听 Pinch 手势
    public Material oldMaterial;
    public Material newMaterial;

    private Renderer sphereRenderer;
    private bool isOldMaterialActive = false; // 记录当前材质状态
    private Coroutine fadeCoroutine;

    void Start()
    {
        sphereRenderer = GetComponent<Renderer>();
        sphereRenderer.material = newMaterial; // 初始使用 newMaterial
    }

    void Update()
    {
        bool isPinching = leftSelect.action.IsPressed();

        if (isPinching && !isOldMaterialActive)
        {
            if (fadeCoroutine != null) StopCoroutine(fadeCoroutine); // 防止重复调用
            fadeCoroutine = StartCoroutine(FadeMaterial(oldMaterial));
            isOldMaterialActive = true;
        }
        else if (!isPinching && isOldMaterialActive)
        {
            if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
            fadeCoroutine = StartCoroutine(FadeMaterial(newMaterial));
            isOldMaterialActive = false;
        }
    }

    IEnumerator FadeMaterial(Material targetMaterial)
    {
        float duration = 1.5f; // 渐变时间
        float time = 0;
        Material currentMaterial = sphereRenderer.material;

        // 直接修改纹理，而不是颜色
        Texture startTexture = currentMaterial.mainTexture;
        Texture targetTexture = targetMaterial.mainTexture;

        while (time < duration)
        {
            time += Time.deltaTime;
            float t = time / duration;

            // 直接修改材质的纹理
            sphereRenderer.material.mainTexture = targetTexture;

            yield return null;
        }

        // 最后完全切换材质
        sphereRenderer.material = targetMaterial;
    }
}
