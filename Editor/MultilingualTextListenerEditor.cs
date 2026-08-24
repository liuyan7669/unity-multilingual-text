using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Cowart.MultilingualText.Editor
{
    [CustomEditor(typeof(MultilingualTextListener))]
    [CanEditMultipleObjects]
    public class MultilingualTextListenerEditor : UnityEditor.Editor
    {
        private const string DefaultFileName = "multilingual_text.json";
        private const double PreviewFileRefreshInterval = 0.5d;
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false, true);

        private readonly Dictionary<int, MultilingualTextItem> cachedItems = new Dictionary<int, MultilingualTextItem>();
        private SerializedProperty textIdProperty;
        private SerializedProperty clearTextWhenMissingProperty;
        private MultilingualTextManager sceneManager;
        private SerializedObject managerSerializedObject;
        private SerializedProperty managerFileNameProperty;
        private string cachedJsonPath = string.Empty;
        private string cachedPreviewError = string.Empty;
        private long cachedJsonLength = -1L;
        private long cachedJsonWriteTicks = -1L;
        private double nextPreviewFileRefreshTime;

        private static class Styles
        {
            public static GUIStyle HeaderTitle;
            public static GUIStyle PreviewBox;
            public static GUIStyle PreviewText;
            private static bool isInitialized;

            public static void EnsureInitialized()
            {
                if (isInitialized) return;
                isInitialized = true;

                bool isDark = EditorGUIUtility.isProSkin;

                HeaderTitle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 12,
                    normal = { textColor = isDark ? new Color(0.92f, 0.95f, 1f) : new Color(0.15f, 0.2f, 0.25f) }
                };

                PreviewBox = new GUIStyle(EditorStyles.helpBox)
                {
                    padding = new RectOffset(8, 8, 6, 6),
                    margin = new RectOffset(0, 0, 4, 4)
                };

                PreviewText = new GUIStyle(EditorStyles.wordWrappedLabel)
                {
                    fontSize = 11,
                    normal = { textColor = isDark ? new Color(0.85f, 0.9f, 0.85f) : new Color(0.1f, 0.4f, 0.15f) }
                };
            }
        }

        private void OnEnable()
        {
            textIdProperty = serializedObject.FindProperty("textId");
            clearTextWhenMissingProperty = serializedObject.FindProperty("clearTextWhenMissing");
            RefreshManagerReference();
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            EditorApplication.projectChanged += InvalidatePreviewCache;
        }

        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            EditorApplication.projectChanged -= InvalidatePreviewCache;
        }

        public override void OnInspectorGUI()
        {
            Styles.EnsureInitialized();
            serializedObject.Update();

            DrawHeaderBanner();
            EditorGUILayout.Space(4f);
            DrawTargetStatus();

            EditorGUILayout.PropertyField(textIdProperty, new GUIContent("多语言文本 ID", "对应当前管理器配置文件中的内容 ID（0 表示未绑定）"));
            EditorGUILayout.PropertyField(clearTextWhenMissingProperty, new GUIContent("缺失时清空文本", "当词条找不到时是否清空 Text 组件，关闭则保留当前文字"));

            if (targets.Length > 1 || textIdProperty.hasMultipleDifferentValues)
            {
                EditorGUILayout.HelpBox("多选状态下不显示词条内容预览。", MessageType.Info);
            }
            else if (textIdProperty.intValue > 0)
            {
                DrawItemPreview(textIdProperty.intValue);
            }
            else
            {
                EditorGUILayout.HelpBox("当前文本 ID 为 0（未绑定），此组件不会修改 Text 内容。", MessageType.None);
            }

            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.Space(4f);
            DrawActionButtons();
        }

        private static void DrawHeaderBanner()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar, GUILayout.Height(24f)))
            {
                GUIContent icon = EditorGUIUtility.IconContent("TextAsset Icon");
                GUILayout.Label(icon, GUILayout.Width(18f), GUILayout.Height(18f));
                GUILayout.Label("多语言文本监听器 (Text Listener)", Styles.HeaderTitle);
                GUILayout.FlexibleSpace();
            }
        }

        private void DrawTargetStatus()
        {
            int missingComponentCount = 0;
            TMP_Text firstTmpText = null;
            bool firstHasLegacyText = false;

            for (int i = 0; i < targets.Length; i++)
            {
                MultilingualTextListener listener = targets[i] as MultilingualTextListener;
                if (listener == null)
                {
                    continue;
                }

                TMP_Text tmpText = listener.GetComponent<TMP_Text>();
                bool hasLegacyText = listener.GetComponent<Text>() != null;
                if (i == 0)
                {
                    firstTmpText = tmpText;
                    firstHasLegacyText = hasLegacyText;
                }

                if (tmpText == null && !hasLegacyText)
                {
                    missingComponentCount++;
                }
            }

            if (missingComponentCount > 0)
            {
                string message = targets.Length == 1
                    ? "当前对象上未找到 Text 或 TextMeshPro 组件，多语言系统将无法更新界面显示。"
                    : $"所选对象中有 {missingComponentCount} 个未找到 Text 或 TextMeshPro 组件。";
                EditorGUILayout.HelpBox(message, MessageType.Warning);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("➕ 添加 TextMeshPro", EditorStyles.miniButton))
                    {
                        AddTextComponentToMissingTargets<TextMeshProUGUI>();
                    }
                    if (GUILayout.Button("➕ 添加 Legacy Text", EditorStyles.miniButton))
                    {
                        AddTextComponentToMissingTargets<Text>();
                    }
                }
                EditorGUILayout.Space(4f);
                return;
            }

            string componentName = targets.Length > 1
                ? $"{targets.Length} 个对象均已绑定文本组件"
                : firstTmpText != null
                    ? "TextMeshPro (" + firstTmpText.GetType().Name + ")"
                    : firstHasLegacyText ? "Legacy Text" : "未知";
            EditorGUILayout.LabelField(new GUIContent(" 目标组件", EditorGUIUtility.IconContent("Installed").image), new GUIContent(componentName), EditorStyles.miniLabel);
        }

        private void DrawActionButtons()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!Application.isPlaying))
                {
                    GUIContent refreshContent = new GUIContent(" 刷新显示", EditorGUIUtility.IconContent("TreeEditor.Refresh").image, "仅在运行模式下立即刷新绑定文本");
                    if (GUILayout.Button(refreshContent, GUILayout.Height(24f)))
                    {
                        for (int i = 0; i < targets.Length; i++)
                        {
                            MultilingualTextListener listener = targets[i] as MultilingualTextListener;
                            if (listener != null)
                            {
                                listener.RefreshText();
                            }
                        }
                    }
                }

                if (GUILayout.Button(new GUIContent(" 打开编辑器", EditorGUIUtility.IconContent("TextAsset Icon").image), GUILayout.Height(24f)))
                {
                    MultilingualTextEditorWindow.OpenManagerFile(GetConfiguredFileName());
                }
            }
        }

        private void DrawItemPreview(int id)
        {
            string jsonPath;
            string pathError;
            if (!TryGetConfigPath(out jsonPath, out pathError))
            {
                EditorGUILayout.HelpBox(pathError, MessageType.Error);
                return;
            }

            RefreshPreviewCache(jsonPath);
            if (!string.IsNullOrEmpty(cachedPreviewError))
            {
                EditorGUILayout.HelpBox(cachedPreviewError, MessageType.Warning);
                return;
            }

            MultilingualTextItem item;
            if (!cachedItems.TryGetValue(id, out item) || item.translations == null || item.translations.Count == 0)
            {
                EditorGUILayout.HelpBox($"配置文件中未找到 ID 为 {id} 的词条，请检查配置或在编辑器中添加。", MessageType.Warning);
                return;
            }

            using (new EditorGUILayout.VerticalScope(Styles.PreviewBox))
            {
                EditorGUILayout.LabelField($"🔍 词条 ID {id} 内容预览:", EditorStyles.miniBoldLabel);
                for (int i = 0; i < item.translations.Count; i++)
                {
                    MultilingualTextTranslation translation = item.translations[i];
                    if (translation == null || string.IsNullOrEmpty(translation.content))
                    {
                        continue;
                    }

                    string previewSnippet = translation.content.Replace("\n", " ");
                    if (previewSnippet.Length > 40)
                    {
                        previewSnippet = previewSnippet.Substring(0, 40) + "...";
                    }
                    EditorGUILayout.LabelField($"• {translation.language}:", previewSnippet, Styles.PreviewText);
                }
            }
        }

        private void RefreshPreviewCache(string jsonPath)
        {
            double currentTime = EditorApplication.timeSinceStartup;
            if (string.Equals(cachedJsonPath, jsonPath, StringComparison.OrdinalIgnoreCase)
                && currentTime < nextPreviewFileRefreshTime)
            {
                return;
            }

            nextPreviewFileRefreshTime = currentTime + PreviewFileRefreshInterval;
            long fileLength = -1L;
            long writeTicks = -1L;
            try
            {
                FileInfo fileInfo = new FileInfo(jsonPath);
                if (!fileInfo.Exists)
                {
                    ResetPreviewData(jsonPath, $"未找到配置文件：{jsonPath}");
                    return;
                }

                fileLength = fileInfo.Length;
                writeTicks = fileInfo.LastWriteTimeUtc.Ticks;
                if (string.Equals(cachedJsonPath, jsonPath, StringComparison.OrdinalIgnoreCase)
                    && cachedJsonLength == fileLength
                    && cachedJsonWriteTicks == writeTicks)
                {
                    return;
                }

                string json = File.ReadAllText(jsonPath, Utf8NoBom).TrimStart('\uFEFF');
                MultilingualTextTable table = JsonUtility.FromJson<MultilingualTextTable>(json);
                if (table == null || table.items == null)
                {
                    ResetPreviewData(jsonPath, "配置文件缺少有效的 items 数据。", fileLength, writeTicks);
                    return;
                }

                cachedItems.Clear();
                for (int i = 0; i < table.items.Count; i++)
                {
                    MultilingualTextItem item = table.items[i];
                    if (item != null && !cachedItems.ContainsKey(item.id))
                    {
                        cachedItems.Add(item.id, item);
                    }
                }

                cachedJsonPath = jsonPath;
                cachedJsonLength = fileLength;
                cachedJsonWriteTicks = writeTicks;
                cachedPreviewError = string.Empty;
            }
            catch (Exception exception)
            {
                ResetPreviewData(jsonPath, $"配置文件预览失败：{exception.Message}", fileLength, writeTicks);
            }
        }

        private void ResetPreviewData(string jsonPath, string error, long fileLength = -1L, long writeTicks = -1L)
        {
            cachedJsonPath = jsonPath;
            cachedJsonLength = fileLength;
            cachedJsonWriteTicks = writeTicks;
            cachedPreviewError = error;
            cachedItems.Clear();
        }

        private bool TryGetConfigPath(out string fullPath, out string error)
        {
            fullPath = string.Empty;
            error = string.Empty;
            string fileName = GetConfiguredFileName();

            try
            {
                if (Path.IsPathRooted(fileName))
                {
                    error = "管理器的配置文件名必须是 StreamingAssets 目录下的相对路径。";
                    return false;
                }

                string streamingAssetsPath = Path.GetFullPath(Application.streamingAssetsPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                fullPath = Path.GetFullPath(Path.Combine(streamingAssetsPath, fileName));
                if (!fullPath.StartsWith(streamingAssetsPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    error = "管理器的配置文件必须位于 StreamingAssets 目录内。";
                    fullPath = string.Empty;
                    return false;
                }
                return true;
            }
            catch (Exception exception)
            {
                error = $"管理器的配置文件名无效：{exception.Message}";
                fullPath = string.Empty;
                return false;
            }
        }

        private string GetConfiguredFileName()
        {
            if (sceneManager == null)
            {
                RefreshManagerReference();
            }

            if (managerSerializedObject != null && managerFileNameProperty != null)
            {
                managerSerializedObject.UpdateIfRequiredOrScript();
                string configuredFileName = managerFileNameProperty.stringValue;
                if (!string.IsNullOrWhiteSpace(configuredFileName))
                {
                    return configuredFileName.Trim();
                }
            }

            return DefaultFileName;
        }

        private void RefreshManagerReference()
        {
            sceneManager = null;
            managerSerializedObject = null;
            managerFileNameProperty = null;

            MultilingualTextListener listener = target as MultilingualTextListener;
            Scene scene = listener != null ? listener.gameObject.scene : default(Scene);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return;
            }

            GameObject[] rootObjects = scene.GetRootGameObjects();
            for (int i = 0; i < rootObjects.Length; i++)
            {
                sceneManager = rootObjects[i].GetComponentInChildren<MultilingualTextManager>(true);
                if (sceneManager != null)
                {
                    managerSerializedObject = new SerializedObject(sceneManager);
                    managerFileNameProperty = managerSerializedObject.FindProperty("fileName");
                    return;
                }
            }
        }

        private void OnHierarchyChanged()
        {
            RefreshManagerReference();
            InvalidatePreviewCache();
        }

        private void InvalidatePreviewCache()
        {
            cachedJsonPath = string.Empty;
            cachedJsonLength = -1L;
            cachedJsonWriteTicks = -1L;
            cachedPreviewError = string.Empty;
            cachedItems.Clear();
            nextPreviewFileRefreshTime = 0d;
            Repaint();
        }

        private void AddTextComponentToMissingTargets<T>() where T : Component
        {
            for (int i = 0; i < targets.Length; i++)
            {
                MultilingualTextListener listener = targets[i] as MultilingualTextListener;
                if (listener == null
                    || listener.GetComponent<TMP_Text>() != null
                    || listener.GetComponent<Text>() != null)
                {
                    continue;
                }

                Undo.AddComponent<T>(listener.gameObject);
            }
        }
    }
}
