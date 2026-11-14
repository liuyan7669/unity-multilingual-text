using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Cowart.InspectorGroups.Editor;

namespace Cowart.MultilingualText.Editor
{
    [CustomEditor(typeof(MultilingualTextManager))]
    [CanEditMultipleObjects]
    public class MultilingualTextManagerEditor : InspectorGroupsEditor<MultilingualTextManager>
    {
        private const string DefaultFileName = "multilingual_text.json";
        private const double ConfigFileRefreshInterval = 0.5d;
        private SerializedProperty fileNameProperty;
        private SerializedProperty loadStateProperty;
        private SerializedProperty lastLoadErrorProperty;
        private string cachedConfigPath = string.Empty;
        private bool cachedConfigExists;
        private long cachedConfigLength;
        private double nextConfigFileRefreshTime;

        protected override void RefreshSerializedProperties()
        {
            fileNameProperty = serializedObject.FindProperty("fileName");
            loadStateProperty = serializedObject.FindProperty("loadState");
            lastLoadErrorProperty = serializedObject.FindProperty("lastLoadError");
        }

        private void OnEnable()
        {
            EditorApplication.projectChanged += InvalidateConfigFileState;
        }

        protected override void OnDisable()
        {
            EditorApplication.projectChanged -= InvalidateConfigFileState;
            base.OnDisable();
        }

        protected override void BuildInspector(InspectorContent content)
        {
            content.Exclude("loadState", "lastLoadError");
            content.Group("ConfigFile", "配置文件与诊断", DescribeConfiguration, style: Cowart.InspectorGroups.InspectorGroupStyle.Framework);
            content.Group("LanguageStatus", "运行状态", DescribeRuntimeStatus, style: Cowart.InspectorGroups.InspectorGroupStyle.Framework);
            content.Group("LanguageActions", "多语言操作", DescribeActions, style: Cowart.InspectorGroups.InspectorGroupStyle.Framework);
            if (Application.isPlaying) Repaint();
        }

        private void DescribeConfiguration(InspectorContent content)
        {
            if (fileNameProperty.hasMultipleDifferentValues)
            {
                content.Message("所选管理器的配置文件名不一致，请分别编辑或先统一文件名。");
                return;
            }
            if (!TryGetConfigPath(out string fullPath, out string relativePath, out string error))
            {
                content.Message(error, MessageType.Error);
                return;
            }
            RefreshConfigFileState(fullPath);
            if (cachedConfigExists)
                content.Message($"{relativePath} ({cachedConfigLength / 1024f:0.0} KB)");
            else
            {
                content.Message($"未找到配置文件：{relativePath}", MessageType.Warning);
                content.Action("创建默认配置文件", () => CreateDefaultConfigFile(fullPath));
            }
            content.Action("定位配置文件", () => PingFile(fullPath), cachedConfigExists);
            content.Action("浏览配置文件目录", () => RevealDirectory(fullPath));
        }

        private void DescribeRuntimeStatus(InspectorContent content)
        {
            var state = Application.isPlaying ? MultilingualTextManager.LoadState
                : (MultilingualTextLoadState)loadStateProperty.intValue;
            content.Value("加载状态", state.ToString());
            string error = Application.isPlaying ? MultilingualTextManager.LastLoadError : lastLoadErrorProperty.stringValue;
            if (!string.IsNullOrWhiteSpace(error)) content.Message(error, MessageType.Error);
            content.Action("重新加载", () => MultilingualTextManager.Reload(), Application.isPlaying);
        }

        private void DescribeActions(InspectorContent content)
        {
            content.Action("重置语言偏好存储键名", () =>
            {
                serializedObject.FindProperty("languagePreferenceKey").stringValue = "MultilingualText.CurrentLanguage";
                serializedObject.ApplyModifiedProperties();
            });
            content.Action("打开多语言可视化编辑器", () =>
                MultilingualTextEditorWindow.OpenManagerFile(fileNameProperty.stringValue), !fileNameProperty.hasMultipleDifferentValues);
            content.Action("百度自动翻译", () => BaiduTranslationEditorWindow.Open(null));
            content.Action("打开 StreamingAssets 目录", () =>
            {
                if (!Directory.Exists(Application.streamingAssetsPath))
                {
                    Directory.CreateDirectory(Application.streamingAssetsPath);
                    AssetDatabase.Refresh();
                }
                EditorUtility.RevealInFinder(Application.streamingAssetsPath);
            });
        }

        private bool TryGetConfigPath(out string fullPath, out string relativePath, out string error)
        {
            fullPath = string.Empty;
            relativePath = string.Empty;
            error = string.Empty;

            string fileName = string.IsNullOrWhiteSpace(fileNameProperty.stringValue)
                ? DefaultFileName
                : fileNameProperty.stringValue.Trim();

            try
            {
                if (Path.IsPathRooted(fileName))
                {
                    error = "配置文件名必须是 StreamingAssets 目录下的相对路径。";
                    return false;
                }

                string streamingAssetsPath = Path.GetFullPath(Application.streamingAssetsPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                fullPath = Path.GetFullPath(Path.Combine(streamingAssetsPath, fileName));
                string requiredPrefix = streamingAssetsPath + Path.DirectorySeparatorChar;
                if (!fullPath.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    error = "配置文件必须位于 StreamingAssets 目录内。";
                    fullPath = string.Empty;
                    return false;
                }

                relativePath = "Assets/StreamingAssets/" + fileName.Replace('\\', '/');
                return true;
            }
            catch (Exception exception)
            {
                error = $"配置文件名无效：{exception.Message}";
                fullPath = string.Empty;
                return false;
            }
        }

        private void RefreshConfigFileState(string fullPath)
        {
            double currentTime = EditorApplication.timeSinceStartup;
            if (string.Equals(cachedConfigPath, fullPath, StringComparison.OrdinalIgnoreCase)
                && currentTime < nextConfigFileRefreshTime)
            {
                return;
            }

            cachedConfigPath = fullPath;
            try
            {
                FileInfo fileInfo = new FileInfo(fullPath);
                cachedConfigExists = fileInfo.Exists;
                cachedConfigLength = cachedConfigExists ? fileInfo.Length : 0L;
            }
            catch (Exception)
            {
                cachedConfigExists = false;
                cachedConfigLength = 0L;
            }
            nextConfigFileRefreshTime = currentTime + ConfigFileRefreshInterval;
        }

        private void InvalidateConfigFileState()
        {
            cachedConfigPath = string.Empty;
            cachedConfigExists = false;
            cachedConfigLength = 0L;
            nextConfigFileRefreshTime = 0d;
            Repaint();
        }

        private static void PingFile(string fullPath)
        {
            string relativePath = GetRelativeAssetPath(fullPath);
            if (string.IsNullOrEmpty(relativePath)) return;

            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(relativePath);
            if (asset != null)
            {
                EditorGUIUtility.PingObject(asset);
                Selection.activeObject = asset;
            }
        }

        private static void RevealDirectory(string fullPath)
        {
            string dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                AssetDatabase.Refresh();
            }

            if (!string.IsNullOrEmpty(dir))
            {
                EditorUtility.RevealInFinder(File.Exists(fullPath) ? fullPath : dir);
            }
        }

        private static string GetRelativeAssetPath(string fullPath)
        {
            string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/').TrimEnd('/') + "/";
            string normalizedFullPath = Path.GetFullPath(fullPath).Replace('\\', '/');
            if (normalizedFullPath.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase))
            {
                return normalizedFullPath.Substring(projectPath.Length);
            }
            return null;
        }

        private void CreateDefaultConfigFile(string fullPath)
        {
            try
            {
                if (File.Exists(fullPath))
                {
                    Debug.LogWarning($"多语言配置文件已存在，未覆盖：{fullPath}");
                    InvalidateConfigFileState();
                    return;
                }

                string dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                MultilingualTextTable defaultTable = new MultilingualTextTable
                {
                    nextId = 2,
                    items = new System.Collections.Generic.List<MultilingualTextItem>
                    {
                        new MultilingualTextItem
                        {
                            id = 1,
                            translations = new System.Collections.Generic.List<MultilingualTextTranslation>
                            {
                                new MultilingualTextTranslation
                                {
                                    language = SupportedLanguage.ChineseSimplified,
                                    content = "你好，世界！"
                                },
                                new MultilingualTextTranslation
                                {
                                    language = SupportedLanguage.English,
                                    content = "Hello, World!"
                                }
                            }
                        }
                    }
                };

                string json = JsonUtility.ToJson(defaultTable, true);
                File.WriteAllText(fullPath, json, new System.Text.UTF8Encoding(false));
                AssetDatabase.Refresh();
                InvalidateConfigFileState();
                Debug.Log($"已成功创建默认多语言配置文件：{fullPath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"创建默认配置文件失败: {ex.Message}");
            }
        }
    }
}
