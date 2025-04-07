using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;

namespace SpriteReplacer
{
    [BepInPlugin("com.snowyegret.spritereplacer", "SpriteReplacer", "1.0.0")]
    public class SpriteReplacerPlugin : BaseUnityPlugin
    {
        public static ManualLogSource logger;
        private Harmony harmony;
        public static Dictionary<string, Texture2D> loadedTextures = new Dictionary<string, Texture2D>();
        private static string replacementFolder;
        public static HashSet<int> processedObjects = new HashSet<int>();
        public static bool isReplacingSprite = false;

        private void Awake()
        {
            logger = Logger;
            logger.LogInfo("SpriteReplacer 모드가 로드되었습니다!");

            replacementFolder = Path.Combine(Paths.PluginPath, "SpriteReplacer");
            logger.LogInfo($"교체 폴더 경로: {replacementFolder}");

            if (!Directory.Exists(replacementFolder))
            {
                try
                {
                    Directory.CreateDirectory(replacementFolder);
                    logger.LogInfo($"교체 폴더가 생성되었습니다: {replacementFolder}");
                }
                catch (Exception e)
                {
                    logger.LogError($"폴더 생성 실패: {e.Message}");
                }
            }
            LoadTexturesWithReadAllBytes();
            ApplyHarmonyPatches();
            SceneManager.sceneLoaded += OnSceneLoaded;

            StartCoroutine(DelayedStart());
        }

        private void ApplyHarmonyPatches()
        {
            try
            {
                harmony = new Harmony("com.snowyegret.spritereplacer");

                // Sprite.Create 메서드들 패치
                harmony.PatchAll(typeof(SpriteCreatePatch));
                harmony.PatchAll(typeof(SpriteCreatePatch2));

                // 스프라이트 렌더러와 UI 이미지 패치
                harmony.PatchAll(typeof(SpriteRendererPatch));
                harmony.PatchAll(typeof(UIImageSpritePatch));

                // Resources.Load 패치
                harmony.PatchAll(typeof(ResourcesLoadPatch));

                logger.LogInfo("모든 패치가 성공적으로 적용되었습니다.");
            }
            catch (Exception e)
            {
                logger.LogError($"패치 적용 중 오류: {e.Message}");
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            logger.LogInfo($"씬 로드됨: {scene.name}, 스프라이트 처리 시작");
            processedObjects.Clear();
            StartCoroutine(ProcessAllSprites());
            StartCoroutine(FixShrineZFighting(true));
        }

        private IEnumerator DelayedStart()
        {
            yield return new WaitForSeconds(2f);
            StartCoroutine(ProcessAllSprites());
            StartCoroutine(ContinuousMonitoring());
            StartCoroutine(FixShrineZFighting(false));
        }

        private void LoadTexturesWithReadAllBytes()
        {
            try
            {
                if (!Directory.Exists(replacementFolder))
                {
                    logger.LogWarning("교체 폴더가 존재하지 않습니다.");
                    return;
                }

                string[] files = Directory.GetFiles(replacementFolder, "*.png", SearchOption.AllDirectories);
                logger.LogInfo($"{files.Length}개의 PNG 파일을 찾았습니다.");

                foreach (string file in files)
                {
                    try
                    {
                        string name = Path.GetFileNameWithoutExtension(file);
                        byte[] fileData = File.ReadAllBytes(file);
                        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);

                        if (texture.LoadImage(fileData))
                        {
                            texture.filterMode = FilterMode.Bilinear;
                            texture.wrapMode = TextureWrapMode.Clamp;
                            texture.Apply(true);
                            loadedTextures[name] = texture;
                            logger.LogInfo($"텍스처 로드됨: {name} ({texture.width}x{texture.height})");
                        }
                    }
                    catch (Exception e)
                    {
                        logger.LogError($"파일 '{Path.GetFileName(file)}' 로드 중 오류: {e.Message}");
                    }
                }

                logger.LogInfo($"총 {loadedTextures.Count}개의 교체 텍스처가 로드되었습니다.");
            }
            catch (Exception e)
            {
                logger.LogError($"텍스처 로드 중 오류: {e.Message}");
            }
        }

        // 신사 스프라이트 깜빡임 해결
        private IEnumerator FixShrineZFighting(bool runOnce)
        {
            do
            {
                SpriteRenderer[] renderers = Resources.FindObjectsOfTypeAll<SpriteRenderer>();
                List<SpriteRenderer> shrineRenderers = new List<SpriteRenderer>();

                foreach (var renderer in renderers)
                {
                    if (renderer != null && renderer.sprite != null && renderer.gameObject.activeSelf)
                    {
                        string spriteName = renderer.sprite.name;
                        if (spriteName.Contains("Shrine"))
                        {
                            shrineRenderers.Add(renderer);
                        }
                    }
                }

                logger.LogInfo($"신사 관련 스프라이트 {shrineRenderers.Count}개 발견");
                bool hasShrine4Body = shrineRenderers.Any(r => r.sprite.name == "Shrine4_Body");
                bool hasShrine3 = shrineRenderers.Any(r => r.sprite.name == "Shrine_3");
                bool hasShrine2 = shrineRenderers.Any(r => r.sprite.name == "Shrine_2");
                foreach (var renderer in shrineRenderers)
                {
                    string spriteName = renderer.sprite.name;
                    if (spriteName == "Shrine4_Body")
                    {
                        renderer.sortingOrder = 100;
                        Vector3 pos = renderer.transform.position;
                        pos.z = -0.1f;
                        renderer.transform.position = pos;
                        renderer.gameObject.SetActive(true);
                        logger.LogInfo("Shrine4_Body 정렬 순서 설정: 100");
                    }
                    else if (spriteName == "Shrine_3")
                    {
                        renderer.sortingOrder = 90;
                        Vector3 pos = renderer.transform.position;
                        pos.z = 0.0f;
                        renderer.transform.position = pos;
                        if (hasShrine4Body)
                        {
                            logger.LogInfo("Shrine_3 비활성화 (Shrine4_Body 존재)");
                            renderer.gameObject.SetActive(false);
                        }
                        else
                        {
                            logger.LogInfo("Shrine_3 활성화 (Shrine_3이 최고 레벨)");
                            renderer.gameObject.SetActive(true);
                        }
                    }
                    else if (spriteName == "Shrine_2")
                    {
                        renderer.sortingOrder = 80;
                        Vector3 pos = renderer.transform.position;
                        pos.z = 0.1f;
                        renderer.transform.position = pos;
                        if (hasShrine4Body || hasShrine3)
                        {
                            logger.LogInfo("Shrine_2 비활성화 (더 높은 레벨 존재)");
                            renderer.gameObject.SetActive(false);
                        }
                        else
                        {
                            logger.LogInfo("Shrine_2 활성화 (Shrine_2가 최고 레벨)");
                            renderer.gameObject.SetActive(true);
                        }
                    }
                    else
                    {
                        renderer.sortingOrder = 70;
                    }
                }

                if (runOnce)
                {
                    break;
                }
                else
                {
                    yield return new WaitForSeconds(2f);
                }
            }
            while (!runOnce);
        }
        private IEnumerator ProcessAllSprites()
        {
            logger.LogInfo("전체 스프라이트 처리 시작...");
            int countProcessed = 0;

            // 스프라이트 렌더러 처리
            SpriteRenderer[] spriteRenderers = Resources.FindObjectsOfTypeAll<SpriteRenderer>();
            if (spriteRenderers != null)
            {
                logger.LogInfo($"{spriteRenderers.Length}개의 스프라이트 렌더러를 찾았습니다.");

                foreach (var renderer in spriteRenderers)
                {
                    if (ProcessRenderer(renderer))
                    {
                        countProcessed++;
                    }
                    if (countProcessed % 10 == 0)
                        yield return null;
                }
            }

            // UI 이미지 처리
            UnityEngine.UI.Image[] images = Resources.FindObjectsOfTypeAll<UnityEngine.UI.Image>();
            if (images != null)
            {
                logger.LogInfo($"{images.Length}개의 UI 이미지를 찾았습니다.");

                foreach (var image in images)
                {
                    if (ProcessImage(image))
                    {
                        countProcessed++;
                    }

                    if (countProcessed % 10 == 0)
                        yield return null;
                }
            }

            // 스프라이트 직접 검색
            Sprite[] allSprites = Resources.FindObjectsOfTypeAll<Sprite>();
            if (allSprites != null)
            {
                logger.LogInfo($"{allSprites.Length}개의 스프라이트를 직접 찾았습니다.");

                foreach (var sprite in allSprites)
                {
                    if (ProcessSprite(sprite))
                    {
                        countProcessed++;
                    }

                    if (countProcessed % 10 == 0)
                        yield return null;
                }
            }

            // 스프라이트 교체 후 즉시 신사 깜빡임 수정 실행
            yield return StartCoroutine(FixShrineZFighting(true));
            logger.LogInfo($"총 {countProcessed}개의 스프라이트가 교체되었습니다.");
        }

        // 렌더러 처리 헬퍼 메서드
        private bool ProcessRenderer(SpriteRenderer renderer)
        {
            if (renderer == null || renderer.sprite == null) return false;

            int id = renderer.GetInstanceID();
            if (processedObjects.Contains(id)) return false;

            try
            {
                string spriteName = renderer.sprite.name;

                if (loadedTextures.ContainsKey(spriteName))
                {
                    ReplaceRendererSprite(renderer);
                    processedObjects.Add(id);
                    return true;
                }
                else
                {
                    processedObjects.Add(id);
                }
            }
            catch (Exception e)
            {
                logger.LogError($"렌더러 처리 중 오류: {e.Message}");
            }

            return false;
        }

        // 이미지 처리 헬퍼 메서드
        private bool ProcessImage(UnityEngine.UI.Image image)
        {
            if (image == null || image.sprite == null) return false;

            int id = image.GetInstanceID();
            if (processedObjects.Contains(id)) return false;

            try
            {
                string spriteName = image.sprite.name;

                if (loadedTextures.ContainsKey(spriteName))
                {
                    ReplaceImageSprite(image);
                    processedObjects.Add(id);
                    return true;
                }
                else
                {
                    processedObjects.Add(id);
                }
            }
            catch (Exception e)
            {
                logger.LogError($"이미지 처리 중 오류: {e.Message}");
            }

            return false;
        }

        // 스프라이트 처리 헬퍼 메서드
        private bool ProcessSprite(Sprite sprite)
        {
            if (sprite == null) return false;

            int id = sprite.GetInstanceID();
            if (processedObjects.Contains(id)) return false;

            try
            {
                string spriteName = sprite.name;

                if (loadedTextures.ContainsKey(spriteName))
                {
                    bool result = ReplaceSpriteDirectly(sprite);
                    processedObjects.Add(id);
                    return result;
                }
                else
                {
                    processedObjects.Add(id);
                }
            }
            catch (Exception e)
            {
                logger.LogError($"스프라이트 처리 중 오류: {e.Message}");
            }

            return false;
        }

        // 모니터링
        private IEnumerator ContinuousMonitoring()
        {
            while (true)
            {
                yield return new WaitForSeconds(3f);
                int count = 0;
                SpriteRenderer[] renderers = Resources.FindObjectsOfTypeAll<SpriteRenderer>();
                if (renderers != null)
                {
                    foreach (var renderer in renderers)
                    {
                        if (ProcessRenderer(renderer))
                        {
                            count++;
                        }
                        if (count > 0 && count % 20 == 0)
                            yield return null;
                    }
                }

                // 새로운 UI 이미지 확인
                UnityEngine.UI.Image[] images = Resources.FindObjectsOfTypeAll<UnityEngine.UI.Image>();
                if (images != null)
                {
                    foreach (var image in images)
                    {
                        if (ProcessImage(image))
                        {
                            count++;
                        }

                        if (count > 0 && count % 20 == 0)
                            yield return null;
                    }
                }

                // 모니터링 결과 로깅 (처리된 경우만)
                if (count > 0)
                {
                    logger.LogInfo($"모니터링 중 {count}개의 새 스프라이트가 교체되었습니다.");
                    yield return StartCoroutine(FixShrineZFighting(true));
                }
            }
        }

        // 스프라이트 렌더러 교체 - (원본 렌더링 속성 보존 + 신사 특별 처리)
        public static bool ReplaceRendererSprite(SpriteRenderer renderer)
        {
            if (renderer == null || renderer.sprite == null || isReplacingSprite) return false;

            try
            {
                string spriteName = renderer.sprite.name;

                if (loadedTextures.ContainsKey(spriteName))
                {
                    isReplacingSprite = true;

                    try
                    {
                        int originalSortingOrder = renderer.sortingOrder;
                        int originalSortingLayerID = renderer.sortingLayerID;
                        string originalSortingLayerName = renderer.sortingLayerName;
                        Vector3 originalPosition = renderer.transform.position;
                        bool originalFlipX = renderer.flipX;
                        bool originalFlipY = renderer.flipY;
                        Color originalColor = renderer.color;
                        Texture2D newTexture = loadedTextures[spriteName];
                        Sprite oldSprite = renderer.sprite;
                        float pixelsPerUnit = oldSprite.pixelsPerUnit;
                        Vector2 pivot = oldSprite.pivot;
                        float pivotX = pivot.x / oldSprite.rect.width;
                        float pivotY = pivot.y / oldSprite.rect.height;

                        Sprite newSprite = Sprite.Create(
                            newTexture,
                            new Rect(0, 0, newTexture.width, newTexture.height),
                            new Vector2(pivotX, pivotY),
                            pixelsPerUnit,
                            0,
                            SpriteMeshType.FullRect
                        );

                        newSprite.name = spriteName;
                        renderer.sprite = newSprite;
                        renderer.flipX = originalFlipX;
                        renderer.flipY = originalFlipY;
                        renderer.color = originalColor;

                        if (spriteName.Contains("Shrine"))
                        {
                            if (spriteName == "Shrine4_Body")
                            {
                                renderer.sortingOrder = 100;
                                Vector3 pos = originalPosition;
                                pos.z = -0.1f;
                                renderer.transform.position = pos;
                                logger.LogInfo($"신사 스프라이트 특별 처리: {spriteName}, 정렬 순서: 100");
                            }
                            else if (spriteName == "Shrine_3")
                            {
                                renderer.sortingOrder = 90;
                                Vector3 pos = originalPosition;
                                pos.z = 0.0f;
                                renderer.transform.position = pos;
                                logger.LogInfo($"신사 스프라이트 특별 처리: {spriteName}, 정렬 순서: 90");
                            }
                            else if (spriteName == "Shrine_2")
                            {
                                renderer.sortingOrder = 80;
                                Vector3 pos = originalPosition;
                                pos.z = 0.1f;
                                renderer.transform.position = pos;
                                logger.LogInfo($"신사 스프라이트 특별 처리: {spriteName}, 정렬 순서: 80");
                            }
                            else
                            {
                                renderer.sortingOrder = originalSortingOrder;
                                renderer.sortingLayerID = originalSortingLayerID;
                                renderer.sortingLayerName = originalSortingLayerName;
                                renderer.transform.position = originalPosition;
                            }
                        }
                        else
                        {
                            renderer.sortingOrder = originalSortingOrder;
                            renderer.sortingLayerID = originalSortingLayerID;
                            renderer.sortingLayerName = originalSortingLayerName;
                            renderer.transform.position = originalPosition;
                        }

                        return true;
                    }
                    finally
                    {
                        isReplacingSprite = false;
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogError($"스프라이트 렌더러 교체 중 오류: {e.Message}");
                isReplacingSprite = false;
            }

            return false;
        }

        public static bool ReplaceImageSprite(UnityEngine.UI.Image image)
        {
            if (image == null || image.sprite == null || isReplacingSprite) return false;

            try
            {
                string spriteName = image.sprite.name;

                if (loadedTextures.ContainsKey(spriteName))
                {
                    isReplacingSprite = true;

                    try
                    {
                        Color originalColor = image.color;
                        bool originalRaycastTarget = image.raycastTarget;
                        UnityEngine.UI.Image.Type originalType = image.type;
                        bool originalPreserveAspect = image.preserveAspect;
                        int originalOrder = image.transform.GetSiblingIndex();
                        UnityEngine.UI.Image.FillMethod originalFillMethod = image.fillMethod;
                        float originalFillAmount = image.fillAmount;

                        Texture2D newTexture = loadedTextures[spriteName];
                        Sprite oldSprite = image.sprite;
                        float pixelsPerUnit = oldSprite.pixelsPerUnit;
                        Vector2 pivot = oldSprite.pivot;
                        float pivotX = pivot.x / oldSprite.rect.width;
                        float pivotY = pivot.y / oldSprite.rect.height;

                        Sprite newSprite = Sprite.Create(
                            newTexture,
                            new Rect(0, 0, newTexture.width, newTexture.height),
                            new Vector2(pivotX, pivotY),
                            pixelsPerUnit,
                            0,
                            SpriteMeshType.FullRect
                        );

                        newSprite.name = spriteName;
                        image.sprite = newSprite;
                        image.color = originalColor;
                        image.raycastTarget = originalRaycastTarget;
                        image.type = originalType;
                        image.preserveAspect = originalPreserveAspect;
                        image.transform.SetSiblingIndex(originalOrder);
                        image.fillMethod = originalFillMethod;
                        image.fillAmount = originalFillAmount;
                        return true;
                    }
                    finally
                    {
                        isReplacingSprite = false;
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogError($"UI 이미지 교체 중 오류: {e.Message}");
                isReplacingSprite = false;
            }

            return false;
        }

        public static bool ReplaceSpriteDirectly(Sprite sprite)
        {
            if (sprite == null || isReplacingSprite) return false;

            try
            {
                string spriteName = sprite.name;

                if (loadedTextures.ContainsKey(spriteName))
                {
                    Texture2D newTexture = loadedTextures[spriteName];
                    isReplacingSprite = true;

                    try
                    {
                        Vector2 pivot = sprite.pivot;
                        float pivotX = pivot.x / sprite.rect.width;
                        float pivotY = pivot.y / sprite.rect.height;
                        FieldInfo textureField = typeof(Sprite).GetField("m_Texture",
                            BindingFlags.Instance | BindingFlags.NonPublic);
                        FieldInfo rectField = typeof(Sprite).GetField("m_Rect",
                            BindingFlags.Instance | BindingFlags.NonPublic);
                        FieldInfo pivotField = typeof(Sprite).GetField("m_Pivot",
                            BindingFlags.Instance | BindingFlags.NonPublic);
                        FieldInfo meshTypeField = typeof(Sprite).GetField("m_MeshType",
                            BindingFlags.Instance | BindingFlags.NonPublic);

                        if (textureField != null && rectField != null && pivotField != null)
                        {
                            textureField.SetValue(sprite, newTexture);
                            Rect newRect = new Rect(0, 0, newTexture.width, newTexture.height);
                            rectField.SetValue(sprite, newRect);

                            Vector2 newPivot = new Vector2(
                                pivotX * newTexture.width,
                                pivotY * newTexture.height
                            );
                            pivotField.SetValue(sprite, newPivot);

                            if (meshTypeField != null)
                            {
                                meshTypeField.SetValue(sprite, SpriteMeshType.FullRect);
                            }

                            return true;
                        }
                    }
                    finally
                    {
                        isReplacingSprite = false;
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogError($"스프라이트 직접 교체 중 오류: {e.Message}");
                isReplacingSprite = false;
            }

            return false;
        }
    }

    // Sprite.Create 메서드 패치 (첫 번째 오버로드)
    [HarmonyPatch(typeof(Sprite), "Create", new Type[] {
        typeof(Texture2D), typeof(Rect), typeof(Vector2), typeof(float), typeof(uint),
        typeof(SpriteMeshType), typeof(Vector4), typeof(bool)
    })]
    public class SpriteCreatePatch
    {
        [HarmonyPrefix]
        public static void Prefix(ref Texture2D texture, ref SpriteMeshType meshType, ref Vector2 pivot, ref Rect rect)
        {
            if (SpriteReplacerPlugin.isReplacingSprite) return;
            meshType = SpriteMeshType.FullRect;
            Vector2 originalPivot = pivot;
            if (texture != null && SpriteReplacerPlugin.loadedTextures.ContainsKey(texture.name))
            {
                float pivotX = pivot.x / rect.width;
                float pivotY = pivot.y / rect.height;
                texture = SpriteReplacerPlugin.loadedTextures[texture.name];
                rect = new Rect(0, 0, texture.width, texture.height);
                pivot = new Vector2(pivotX * texture.width, pivotY * texture.height);
            }
        }
    }

    // Sprite.Create 메서드 패치 (두 번째 오버로드)
    [HarmonyPatch(typeof(Sprite), "Create", new Type[] {
        typeof(Texture2D), typeof(Rect), typeof(Vector2), typeof(float), typeof(uint),
        typeof(SpriteMeshType)
    })]
    public class SpriteCreatePatch2
    {
        [HarmonyPrefix]
        public static void Prefix(ref Texture2D texture, ref SpriteMeshType meshType, ref Vector2 pivot, ref Rect rect)
        {
            if (SpriteReplacerPlugin.isReplacingSprite) return;
            meshType = SpriteMeshType.FullRect;
            Vector2 originalPivot = pivot;
            if (texture != null && SpriteReplacerPlugin.loadedTextures.ContainsKey(texture.name))
            {
                float pivotX = pivot.x / rect.width;
                float pivotY = pivot.y / rect.height;

                texture = SpriteReplacerPlugin.loadedTextures[texture.name];
                rect = new Rect(0, 0, texture.width, texture.height);
                pivot = new Vector2(pivotX * texture.width, pivotY * texture.height);
            }
        }
    }

    // 스프라이트 렌더러 스프라이트 설정 패치 (정렬 속성 보존)
    [HarmonyPatch(typeof(SpriteRenderer), "sprite", MethodType.Setter)]
    public class SpriteRendererPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(SpriteRenderer __instance, ref Sprite value)
        {
            if (__instance == null || value == null || SpriteReplacerPlugin.isReplacingSprite) return true;

            string spriteName = value.name;
            if (SpriteReplacerPlugin.loadedTextures.ContainsKey(spriteName))
            {
                SpriteReplacerPlugin.isReplacingSprite = true;
                try
                {
                    int originalSortingOrder = __instance.sortingOrder;
                    int originalSortingLayerID = __instance.sortingLayerID;
                    string originalSortingLayerName = __instance.sortingLayerName;
                    Vector3 originalPosition = __instance.transform.position;
                    Texture2D newTexture = SpriteReplacerPlugin.loadedTextures[spriteName];
                    float pixelsPerUnit = value.pixelsPerUnit;
                    Vector2 pivot = value.pivot;
                    float pivotX = pivot.x / value.rect.width;
                    float pivotY = pivot.y / value.rect.height;

                    Sprite newSprite = Sprite.Create(
                        newTexture,
                        new Rect(0, 0, newTexture.width, newTexture.height),
                        new Vector2(pivotX, pivotY),
                        pixelsPerUnit,
                        0,
                        SpriteMeshType.FullRect
                    );

                    newSprite.name = spriteName;
                    value = newSprite;

                    // 신사 스프라이트인 경우
                    if (spriteName.Contains("Shrine"))
                    {
                        if (spriteName == "Shrine4_Body")
                        {
                            SpriteRendererPatchData.StoreRendererData(__instance.GetInstanceID(),
                                100, originalSortingLayerID, originalSortingLayerName,
                                new Vector3(originalPosition.x, originalPosition.y, -0.1f));
                        }
                        else if (spriteName == "Shrine_3")
                        {
                            SpriteRendererPatchData.StoreRendererData(__instance.GetInstanceID(),
                                90, originalSortingLayerID, originalSortingLayerName,
                                new Vector3(originalPosition.x, originalPosition.y, 0.0f));
                        }
                        else if (spriteName == "Shrine_2")
                        {
                            SpriteRendererPatchData.StoreRendererData(__instance.GetInstanceID(),
                                80, originalSortingLayerID, originalSortingLayerName,
                                new Vector3(originalPosition.x, originalPosition.y, 0.1f));
                        }
                        else
                        {
                            SpriteRendererPatchData.StoreRendererData(__instance.GetInstanceID(),
                                originalSortingOrder, originalSortingLayerID,
                                originalSortingLayerName, originalPosition);
                        }
                    }
                    else
                    {
                        SpriteRendererPatchData.StoreRendererData(__instance.GetInstanceID(),
                            originalSortingOrder, originalSortingLayerID,
                            originalSortingLayerName, originalPosition);
                    }
                }
                finally
                {
                    SpriteReplacerPlugin.isReplacingSprite = false;
                }
            }

            return true;
        }

        [HarmonyPostfix]
        public static void Postfix(SpriteRenderer __instance)
        {
            if (__instance != null && SpriteRendererPatchData.HasStoredData(__instance.GetInstanceID()))
            {
                SpriteRendererPatchData.RendererData data =
                    SpriteRendererPatchData.GetRendererData(__instance.GetInstanceID());

                __instance.sortingOrder = data.sortingOrder;
                __instance.sortingLayerID = data.sortingLayerID;
                __instance.sortingLayerName = data.sortingLayerName;
                __instance.transform.position = data.position;
                string spriteName = __instance.sprite.name;
                if (spriteName.Contains("Shrine"))
                {
                    SpriteReplacerPlugin.logger.LogInfo($"신사 스프라이트 패치 적용됨: {spriteName}, 정렬 순서: {data.sortingOrder}");
                }
                SpriteRendererPatchData.RemoveRendererData(__instance.GetInstanceID());
            }
        }
    }

    // 렌더러 속성 임시 저장용 정적 클래스
    public static class SpriteRendererPatchData
    {
        // 렌더러 속성 데이터 구조체
        public struct RendererData
        {
            public int sortingOrder;
            public int sortingLayerID;
            public string sortingLayerName;
            public Vector3 position;
        }

        // 렌더러 ID별 데이터 저장
        private static Dictionary<int, RendererData> storedData = new Dictionary<int, RendererData>();

        // 데이터 저장
        public static void StoreRendererData(int instanceID, int sortingOrder, int sortingLayerID,
            string sortingLayerName, Vector3 position)
        {
            RendererData data = new RendererData
            {
                sortingOrder = sortingOrder,
                sortingLayerID = sortingLayerID,
                sortingLayerName = sortingLayerName,
                position = position
            };

            storedData[instanceID] = data;
        }

        // 데이터 확인
        public static bool HasStoredData(int instanceID)
        {
            return storedData.ContainsKey(instanceID);
        }

        // 데이터 가져오기
        public static RendererData GetRendererData(int instanceID)
        {
            if (storedData.ContainsKey(instanceID))
            {
                return storedData[instanceID];
            }
            return new RendererData();
        }

        // 데이터 제거
        public static void RemoveRendererData(int instanceID)
        {
            if (storedData.ContainsKey(instanceID))
            {
                storedData.Remove(instanceID);
            }
        }
    }

    // UI 이미지 패치 - 정렬 속성 보존
    [HarmonyPatch(typeof(UnityEngine.UI.Image), "sprite", MethodType.Setter)]
    public class UIImageSpritePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(UnityEngine.UI.Image __instance, ref Sprite value)
        {
            if (__instance == null || value == null || SpriteReplacerPlugin.isReplacingSprite) return true;

            string spriteName = value.name;
            if (SpriteReplacerPlugin.loadedTextures.ContainsKey(spriteName))
            {
                SpriteReplacerPlugin.isReplacingSprite = true;
                try
                {
                    Color originalColor = __instance.color;
                    bool originalRaycastTarget = __instance.raycastTarget;
                    UnityEngine.UI.Image.Type originalType = __instance.type;
                    bool originalPreserveAspect = __instance.preserveAspect;
                    int originalSiblingIndex = __instance.transform.GetSiblingIndex();
                    Texture2D newTexture = SpriteReplacerPlugin.loadedTextures[spriteName];
                    float pixelsPerUnit = value.pixelsPerUnit;
                    Vector2 pivot = value.pivot;
                    float pivotX = pivot.x / value.rect.width;
                    float pivotY = pivot.y / value.rect.height;

                    Sprite newSprite = Sprite.Create(
                        newTexture,
                        new Rect(0, 0, newTexture.width, newTexture.height),
                        new Vector2(pivotX, pivotY),
                        pixelsPerUnit,
                        0,
                        SpriteMeshType.FullRect
                    );

                    newSprite.name = spriteName;
                    value = newSprite;

                    UIImagePatchData.StoreImageData(__instance.GetInstanceID(), originalColor,
                        originalRaycastTarget, originalType, originalPreserveAspect, originalSiblingIndex);
                }
                finally
                {
                    SpriteReplacerPlugin.isReplacingSprite = false;
                }
            }

            return true;
        }

        [HarmonyPostfix]
        public static void Postfix(UnityEngine.UI.Image __instance)
        {
            if (__instance != null && UIImagePatchData.HasStoredData(__instance.GetInstanceID()))
            {
                UIImagePatchData.ImageData data =
                    UIImagePatchData.GetImageData(__instance.GetInstanceID());

                __instance.color = data.color;
                __instance.raycastTarget = data.raycastTarget;
                __instance.type = data.type;
                __instance.preserveAspect = data.preserveAspect;
                __instance.transform.SetSiblingIndex(data.siblingIndex);

                UIImagePatchData.RemoveImageData(__instance.GetInstanceID());
            }
        }
    }

    // UI 이미지 속성 임시 저장용 정적 클래스
    public static class UIImagePatchData
    {
        // UI 이미지 속성 데이터 구조체
        public struct ImageData
        {
            public Color color;
            public bool raycastTarget;
            public UnityEngine.UI.Image.Type type;
            public bool preserveAspect;
            public int siblingIndex;
        }

        // 이미지 ID별 데이터 저장
        private static Dictionary<int, ImageData> storedData = new Dictionary<int, ImageData>();

        // 데이터 저장
        public static void StoreImageData(int instanceID, Color color, bool raycastTarget,
            UnityEngine.UI.Image.Type type, bool preserveAspect, int siblingIndex)
        {
            ImageData data = new ImageData
            {
                color = color,
                raycastTarget = raycastTarget,
                type = type,
                preserveAspect = preserveAspect,
                siblingIndex = siblingIndex
            };

            storedData[instanceID] = data;
        }

        // 데이터 확인
        public static bool HasStoredData(int instanceID)
        {
            return storedData.ContainsKey(instanceID);
        }

        // 데이터 가져오기
        public static ImageData GetImageData(int instanceID)
        {
            if (storedData.ContainsKey(instanceID))
            {
                return storedData[instanceID];
            }

            // 기본값
            return new ImageData
            {
                color = Color.white,
                raycastTarget = true,
                type = UnityEngine.UI.Image.Type.Simple,
                preserveAspect = false,
                siblingIndex = 0
            };
        }

        // 데이터 제거
        public static void RemoveImageData(int instanceID)
        {
            if (storedData.ContainsKey(instanceID))
            {
                storedData.Remove(instanceID);
            }
        }
    }

    // Resources.Load 패치
    [HarmonyPatch(typeof(Resources), "Load", new Type[] { typeof(string), typeof(Type) })]
    public class ResourcesLoadPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref UnityEngine.Object __result)
        {
            // Sprite 타입일 때만 처리
            if (__result != null && __result is Sprite)
            {
                Sprite sprite = __result as Sprite;
                if (sprite != null && SpriteReplacerPlugin.loadedTextures.ContainsKey(sprite.name) &&
                    !SpriteReplacerPlugin.processedObjects.Contains(sprite.GetInstanceID()))
                {
                    SpriteReplacerPlugin.ReplaceSpriteDirectly(sprite);
                    SpriteReplacerPlugin.processedObjects.Add(sprite.GetInstanceID());
                }
            }
        }
    }
}
