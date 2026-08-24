using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Cowart.MultilingualText.Editor
{
    [CustomEditor(typeof(MultilingualTextManager))]
    [CanEditMultipleObjects]
    public class MultilingualTextManagerEditor : UnityEditor.Editor
    {
        private const string PrefKeyConfigFoldout = "Cowart.MultilingualText.Manager.ConfigFoldout";
        private const string PrefKeyLanguageFoldout = "Cowart.MultilingualText.Manager.LanguageFoldout";
        private const string PrefKeyPrefsFoldout = "Cowart.MultilingualText.Manager.PrefsFoldout";
        private const string PrefKeyStatusFoldout = "Cowart.MultilingualText.Manager.StatusFoldout";
        private const string DefaultFileName = "multilingual_text.json";
        private const double ConfigFileRefreshInterval = 0.5d;

        private SerializedProperty fileNameProperty;
        private SerializedProperty currentLanguageProperty;
        private SerializedProperty fallbackLanguageProperty;
        private SerializedProperty rememberLanguageProperty;
        private SerializedProperty languagePreferenceKeyProperty;
        private SerializedProperty loadStateProperty;
        private SerializedProperty lastLoadErrorProperty;

        private bool showConfigGroup = true;
        private bool showLanguageGroup = true;
        private bool showPrefsGroup = true;
        private bool showStatusGroup = true;
        private string cachedConfigPath = string.Empty;
        private bool cachedConfigExists;
        private long cachedConfigLength;
        private double nextConfigFileRefreshTime;

        private static class Styles
        {
            public static GUIStyle HeaderTitle;
            public static GUIStyle StatusLabel;
            public static GUIStyle LoadedStatus;
            public static GUIStyle LoadingStatus;
            public static GUIStyle FailedStatus;
            public static GUIStyle NotLoadedStatus;
            public static GUIStyle SectionBox;
            public static GUIStyle PathLabel;
            private static bool isInitialized;

            public static void EnsureInitialized()
            {
                if (isInitialized) return;
                isInitialized = true;

                bool isDark = EditorGUIUtility.isProSkin;

                HeaderTitle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 13,
                    margin = new RectOffset(0, 0, 0, 0),
                    padding = new RectOffset(0, 0, 0, 0),
                    normal = { textColor = isDark ? new Color(0.92f, 0.95f, 1f) : new Color(0.15f, 0.2f, 0.25f) }
                };

                StatusLabel = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    fontSize = 11,
                    alignment = TextAnchor.MiddleLeft
                };

                LoadedStatus = CreateStatusStyle(new Color(0.3f, 0.85f, 0.4f));
                LoadingStatus = CreateStatusStyle(new Color(1f, 0.75f, 0.2f));
                FailedStatus = CreateStatusStyle(new Color(1f, 0.35f, 0.35f));
                NotLoadedStatus = CreateStatusStyle(isDark
                    ? new Color(0.7f, 0.7f, 0.7f)
                    : new Color(0.4f, 0.4f, 0.4f));

                SectionBox = new GUIStyle(EditorStyles.helpBox)
                {
                    padding = new RectOffset(10, 10, 8, 8),
                    margin = new RectOffset(0, 0, 4, 6)
                };

                PathLabel = new GUIStyle(EditorStyles.miniLabel)
                {
                    fontSize = 10,
                    wordWrap = true,
                    normal = { textColor = isDark ? new Color(0.55f, 0.8f, 0.55f) : new Color(0.15f, 0.55f, 0.2f) }
                };

            }

            private static GUIStyle CreateStatusStyle(Color textColor)
            {
                return new GUIStyle(EditorStyles.boldLabel)
                {
                    normal = { textColor = textColor }
                };
            }
        }

        private void OnEnable()
        {
            fileNameProperty = serializedObject.FindProperty("fileName");
            currentLanguageProperty = serializedObject.FindProperty("currentLanguage");
            fallbackLanguageProperty = serializedObject.FindProperty("fallbackLanguage");
            rememberLanguageProperty = serializedObject.FindProperty("rememberLanguage");
            languagePreferenceKeyProperty = serializedObject.FindProperty("languagePreferenceKey");
            loadStateProperty = serializedObject.FindProperty("loadState");
            lastLoadErrorProperty = serializedObject.FindProperty("lastLoadError");

            showConfigGroup = EditorPrefs.GetBool(PrefKeyConfigFoldout, true);
            showLanguageGroup = EditorPrefs.GetBool(PrefKeyLanguageFoldout, true);
            showPrefsGroup = EditorPrefs.GetBool(PrefKeyPrefsFoldout, true);
            showStatusGroup = EditorPrefs.GetBool(PrefKeyStatusFoldout, true);
            EditorApplication.projectChanged += InvalidateConfigFileState;
        }

        private void OnDisable()
        {
            EditorPrefs.SetBool(PrefKeyConfigFoldout, showConfigGroup);
            EditorPrefs.SetBool(PrefKeyLanguageFoldout, showLanguageGroup);
            EditorPrefs.SetBool(PrefKeyPrefsFoldout, showPrefsGroup);
            EditorPrefs.SetBool(PrefKeyStatusFoldout, showStatusGroup);
            EditorApplication.projectChanged -= InvalidateConfigFileState;
        }

        public override void OnInspectorGUI()
        {
            Styles.EnsureInitialized();
            serializedObject.Update();

            DrawHeaderBanner();

            EditorGUILayout.Space(2f);

            DrawConfigurationSection();
            DrawLanguageSection();
            DrawPreferencesSection();
            DrawRuntimeStatusSection();

            EditorGUILayout.Space(4f);
            DrawActionButtons();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawHeaderBanner()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar, GUILayout.Height(26f)))
            {
                GUIContent icon = EditorGUIUtility.IconContent("TextAsset Icon");
                GUILayout.Label(icon, GUILayout.Width(20f), GUILayout.Height(20f));

                GUILayout.Label("多语言管理器 (Multilingual Text Manager)", Styles.HeaderTitle);
                GUILayout.FlexibleSpace();

                if (Application.isPlaying)
                {
                    GUIContent playBadge = new GUIContent("● 运行中", "游戏正在运行，修改语言将即时生效");
                    GUILayout.Label(playBadge, Styles.StatusLabel);
                }
            }
        }

        private void DrawConfigurationSection()
        {
            showConfigGroup = EditorGUILayout.BeginFoldoutHeaderGroup(showConfigGroup, " 基础配置 (Configuration)");
            if (showConfigGroup)
            {
                using (new EditorGUI.IndentLevelScope(1))
                {
                    EditorGUILayout.Space(2f);

                    bool hasMixedFileNames = fileNameProperty.hasMultipleDifferentValues;
                    string fullPath = string.Empty;
                    string relativePath = string.Empty;
                    string pathError = string.Empty;
                    bool hasValidPath = !hasMixedFileNames && TryGetConfigPath(out fullPath, out relativePath, out pathError);
                    if (hasValidPath)
                    {
                        RefreshConfigFileState(fullPath);
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.PropertyField(fileNameProperty, new GUIContent("配置文件名", "StreamingAssets 目录下的多语言 JSON 文件名"));

                        using (new EditorGUI.DisabledScope(!hasValidPath || !cachedConfigExists))
                        {
                            if (GUILayout.Button(new GUIContent("定位", EditorGUIUtility.IconContent("Search Icon").image, "在 Project 视窗中高亮该文件"), EditorStyles.miniButton, GUILayout.Width(56f), GUILayout.Height(18f)))
                            {
                                PingFile(fullPath);
                            }
                        }

                        using (new EditorGUI.DisabledScope(!hasValidPath))
                        {
                            if (GUILayout.Button(new GUIContent("浏览", EditorGUIUtility.IconContent("Folder Icon").image, "在资源管理器中查看文件所在目录"), EditorStyles.miniButton, GUILayout.Width(56f), GUILayout.Height(18f)))
                            {
                                RevealDirectory(fullPath);
                            }
                        }
                    }

                    if (hasMixedFileNames)
                    {
                        EditorGUILayout.HelpBox("所选管理器的配置文件名不一致，请分别编辑或先统一文件名。", MessageType.Info);
                    }
                    else if (!hasValidPath)
                    {
                        EditorGUILayout.HelpBox(pathError, MessageType.Error);
                    }
                    else if (cachedConfigExists)
                    {
                        EditorGUILayout.LabelField(GUIContent.none, new GUIContent($"✓ 路径: {relativePath}  ({cachedConfigLength / 1024f:0.0} KB)", EditorGUIUtility.IconContent("Installed").image), Styles.PathLabel);
                    }
                    else
                    {
                        EditorGUILayout.HelpBox($"未找到配置文件: {relativePath}\n请确保文件已放置在 StreamingAssets 目录下。", MessageType.Warning);
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.FlexibleSpace();
                            if (GUILayout.Button("➕ 自动创建默认配置文件", GUILayout.Height(22f), GUILayout.Width(180f)))
                            {
                                CreateDefaultConfigFile(fullPath);
                            }
                        }
                    }

                    EditorGUILayout.Space(2f);
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawLanguageSection()
        {
            showLanguageGroup = EditorGUILayout.BeginFoldoutHeaderGroup(showLanguageGroup, " 语言设置 (Language Settings)");
            if (showLanguageGroup)
            {
                using (new EditorGUI.IndentLevelScope(1))
                {
                    EditorGUILayout.Space(2f);

                    EditorGUILayout.PropertyField(currentLanguageProperty, new GUIContent("当前语言", "游戏界面当前显示的目标语言"));
                    EditorGUILayout.PropertyField(fallbackLanguageProperty, new GUIContent("缺省回退语言", "当某一词条缺少目标语言翻译时，自动回退显示的语言"));

                    EditorGUILayout.Space(2f);
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawPreferencesSection()
        {
            showPrefsGroup = EditorGUILayout.BeginFoldoutHeaderGroup(showPrefsGroup, " 偏好持久化 (Preferences)");
            if (showPrefsGroup)
            {
                using (new EditorGUI.IndentLevelScope(1))
                {
                    EditorGUILayout.Space(2f);

                    EditorGUILayout.PropertyField(rememberLanguageProperty, new GUIContent("记住用户语言", "切换语言时是否自动保存到 PlayerPrefs 本地存储"));

                    if (rememberLanguageProperty.boolValue)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.PropertyField(languagePreferenceKeyProperty, new GUIContent("存储键名 (Key)", "保存在 PlayerPrefs 中的唯一键名"));
                            if (GUILayout.Button("重置", EditorStyles.miniButton, GUILayout.Width(42f)))
                            {
                                languagePreferenceKeyProperty.stringValue = "MultilingualText.CurrentLanguage";
                            }
                        }
                    }
                    else
                    {
                        using (new EditorGUI.DisabledScope(true))
                        {
                            EditorGUILayout.PropertyField(languagePreferenceKeyProperty, new GUIContent("存储键名 (Key)", "未启用记住语言"));
                        }
                    }

                    EditorGUILayout.Space(2f);
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawRuntimeStatusSection()
        {
            showStatusGroup = EditorGUILayout.BeginFoldoutHeaderGroup(showStatusGroup, " 运行状态与诊断 (Runtime Status)");
            if (showStatusGroup)
            {
                using (new EditorGUI.IndentLevelScope(1))
                {
                    EditorGUILayout.Space(2f);

                    MultilingualTextLoadState state = Application.isPlaying
                        ? MultilingualTextManager.LoadState
                        : (MultilingualTextLoadState)loadStateProperty.intValue;

                    string stateText;
                    string iconName;
                    GUIStyle statusStyle;

                    switch (state)
                    {
                        case MultilingualTextLoadState.Loaded:
                            stateText = "● 已加载 (Loaded)";
                            iconName = "TestPassed";
                            statusStyle = Styles.LoadedStatus;
                            break;
                        case MultilingualTextLoadState.Loading:
                            stateText = "● 加载中 (Loading...)";
                            iconName = "Loading";
                            statusStyle = Styles.LoadingStatus;
                            break;
                        case MultilingualTextLoadState.Failed:
                            stateText = "● 加载失败 (Failed)";
                            iconName = "TestFailed";
                            statusStyle = Styles.FailedStatus;
                            break;
                        default:
                            stateText = "○ 未加载 (Not Loaded)";
                            iconName = "TestNormal";
                            statusStyle = Styles.NotLoadedStatus;
                            break;
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.PrefixLabel("加载状态");
                        GUILayout.Label(new GUIContent(stateText, EditorGUIUtility.IconContent(iconName).image), statusStyle);

                        GUILayout.FlexibleSpace();

                        if (Application.isPlaying)
                        {
                            if (GUILayout.Button(new GUIContent(" 重新加载", EditorGUIUtility.IconContent("TreeEditor.Refresh").image), EditorStyles.miniButton, GUILayout.Height(18f)))
                            {
                                MultilingualTextManager.Reload();
                            }
                        }
                    }

                    string errorMsg = Application.isPlaying ? MultilingualTextManager.LastLoadError : lastLoadErrorProperty.stringValue;
                    if (!string.IsNullOrWhiteSpace(errorMsg))
                    {
                        EditorGUILayout.HelpBox($"最后加载错误: {errorMsg}", MessageType.Error);
                    }

                    EditorGUILayout.Space(2f);
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawActionButtons()
        {
            using (new EditorGUILayout.VerticalScope(Styles.SectionBox))
            {
                bool cannotOpen = fileNameProperty == null || fileNameProperty.hasMultipleDifferentValues;

                using (new EditorGUI.DisabledScope(cannotOpen))
                {
                    GUIContent editorBtnContent = new GUIContent("  打开多语言可视化编辑器 (Visual Editor)", EditorGUIUtility.IconContent("TextAsset Icon").image);
                    if (GUILayout.Button(editorBtnContent, GUILayout.Height(30f)))
                    {
                        MultilingualTextEditorWindow.OpenManagerFile(fileNameProperty.stringValue);
                    }
                }

                EditorGUILayout.Space(2f);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent(" 百度自动翻译", EditorGUIUtility.IconContent("d_SettingsIcon").image), EditorStyles.miniButton, GUILayout.Height(22f)))
                    {
                        BaiduTranslationEditorWindow.Open(null);
                    }

                    if (GUILayout.Button(new GUIContent(" 打开 StreamingAssets 目录", EditorGUIUtility.IconContent("Folder Icon").image), EditorStyles.miniButton, GUILayout.Height(22f)))
                    {
                        if (!Directory.Exists(Application.streamingAssetsPath))
                        {
                            Directory.CreateDirectory(Application.streamingAssetsPath);
                            AssetDatabase.Refresh();
                        }
                        EditorUtility.RevealInFinder(Application.streamingAssetsPath);
                    }
                }
            }
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
