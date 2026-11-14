using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using Cowart.InspectorGroups.Editor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Cowart.MultilingualText.Editor
{
    [CustomEditor(typeof(MultilingualTextListener))]
    [CanEditMultipleObjects]
    public class MultilingualTextListenerEditor : InspectorGroupsEditor<MultilingualTextListener>
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

        protected override void RefreshSerializedProperties()
        {
            textIdProperty = serializedObject.FindProperty("textId");
            clearTextWhenMissingProperty = serializedObject.FindProperty("clearTextWhenMissing");
        }

        private void OnEnable()
        {
            RefreshManagerReference();
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            EditorApplication.projectChanged += InvalidatePreviewCache;
        }

        protected override void OnDisable()
        {
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            EditorApplication.projectChanged -= InvalidatePreviewCache;
            base.OnDisable();
        }

        protected override void BuildInspector(InspectorContent content)
        {
            content.Group("TextTarget", "目标文本组件", DescribeTargetStatus, style: Cowart.InspectorGroups.InspectorGroupStyle.Framework);
            content.Group("TextPreview", "词条预览", preview =>
            {
                if (targets.Length > 1 || textIdProperty.hasMultipleDifferentValues)
                    preview.Message("多选状态下不显示词条内容预览。");
                else if (textIdProperty.intValue > 0) DescribeItemPreview(preview, textIdProperty.intValue);
                else preview.Message("当前文本 ID 为 0（未绑定），此组件不会修改 Text 内容。", MessageType.None);
            }, style: Cowart.InspectorGroups.InspectorGroupStyle.Framework);
            content.Group("TextActions", "文本操作", actions =>
            {
                actions.Action("刷新显示", () =>
                {
                    foreach (var candidate in targets)
                        if (candidate is MultilingualTextListener listener) listener.RefreshText();
                }, Application.isPlaying, "仅在运行模式下立即刷新绑定文本");
                actions.Action("打开编辑器", () => MultilingualTextEditorWindow.OpenManagerFile(GetConfiguredFileName()));
            }, style: Cowart.InspectorGroups.InspectorGroupStyle.Framework);
        }

        private void DescribeTargetStatus(InspectorContent content)
        {
            int missingComponentCount = 0;
            TMP_Text firstTmpText = null;
            bool firstHasLegacyText = false;
            for (int i = 0; i < targets.Length; i++)
            {
                var listener = targets[i] as MultilingualTextListener;
                if (listener == null) continue;
                var tmpText = listener.GetComponent<TMP_Text>();
                bool hasLegacyText = listener.GetComponent<Text>() != null;
                if (i == 0) { firstTmpText = tmpText; firstHasLegacyText = hasLegacyText; }
                if (tmpText == null && !hasLegacyText) missingComponentCount++;
            }
            if (missingComponentCount > 0)
            {
                string message = targets.Length == 1
                    ? "当前对象上未找到 Text 或 TextMeshPro 组件，多语言系统将无法更新界面显示。"
                    : $"所选对象中有 {missingComponentCount} 个未找到 Text 或 TextMeshPro 组件。";
                content.Message(message, MessageType.Warning);
                content.Action("添加 TextMeshPro", AddTextComponentToMissingTargets<TextMeshProUGUI>);
                content.Action("添加 Legacy Text", AddTextComponentToMissingTargets<Text>);
                return;
            }
            string componentName = targets.Length > 1
                ? $"{targets.Length} 个对象均已绑定文本组件"
                : firstTmpText != null ? "TextMeshPro (" + firstTmpText.GetType().Name + ")"
                    : firstHasLegacyText ? "Legacy Text" : "未知";
            content.Value("目标组件", componentName);
        }

        private void DescribeItemPreview(InspectorContent content, int id)
        {
            if (!TryGetConfigPath(out string jsonPath, out string pathError))
            {
                content.Message(pathError, MessageType.Error);
                return;
            }
            RefreshPreviewCache(jsonPath);
            if (!string.IsNullOrEmpty(cachedPreviewError))
            {
                content.Message(cachedPreviewError, MessageType.Warning);
                return;
            }
            if (!cachedItems.TryGetValue(id, out MultilingualTextItem item) || item.translations == null || item.translations.Count == 0)
            {
                content.Message($"配置文件中未找到 ID 为 {id} 的词条，请检查配置或在编辑器中添加。", MessageType.Warning);
                return;
            }
            content.Value("词条 ID", id.ToString());
            foreach (var translation in item.translations)
            {
                if (translation == null || string.IsNullOrEmpty(translation.content)) continue;
                string previewSnippet = translation.content.Replace("\n", " ");
                if (previewSnippet.Length > 40) previewSnippet = previewSnippet.Substring(0, 40) + "...";
                content.Value(translation.language.ToString(), previewSnippet);
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

            if (managerSerializedObject != null)
            {
                managerSerializedObject.UpdateIfRequiredOrScript();
                managerFileNameProperty = managerSerializedObject.FindProperty("fileName");
                string configuredFileName = managerFileNameProperty != null ? managerFileNameProperty.stringValue : null;
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
