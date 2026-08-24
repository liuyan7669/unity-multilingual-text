using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Cowart.MultilingualText.Editor
{
    /// <summary>
    /// 编辑 multilingual_text.json 的可视化窗口。
    /// </summary>
    public class MultilingualTextEditorWindow : EditorWindow
    {
        private enum ViewMode
        {
            Table,
            Detail
        }

        private const string DefaultPath = "Assets/StreamingAssets/multilingual_text.json";
        private const string ViewModeKey = "Cowart.MultilingualText.Editor.ViewMode";
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false, true);

        [SerializeField] private MultilingualTextTable table = new MultilingualTextTable();
        [SerializeField] private string savedJson = string.Empty;
        [SerializeField] private string filePath = DefaultPath;
        [SerializeField] private string searchText = string.Empty;
        [SerializeField] private string findText = string.Empty;
        [SerializeField] private string replaceText = string.Empty;
        [SerializeField] private bool ignoreCase;
        [SerializeField] private bool showFindReplace;
        [SerializeField] private bool detailStackedView;
        [SerializeField] private int activeLanguageIndex;
        [SerializeField] private int selectedIndex;
        [SerializeField] private SupportedLanguage previewLanguage = SupportedLanguage.ChineseSimplified;
        [SerializeField] private ViewMode viewMode;
        [SerializeField] private Vector2 tableScroll;
        [SerializeField] private Vector2 sidebarScroll;
        [SerializeField] private Vector2 detailScroll;

        private string statusMessage = string.Empty;
        private MessageType statusType = MessageType.Info;
        private int undoRecordedKeyboardControl;
        private bool validationDirty = true;
        private string cachedValidationMessage = string.Empty;
        private MessageType cachedValidationType = MessageType.Info;
        private readonly Dictionary<string, AudioClip> audioClipCache = new Dictionary<string, AudioClip>(StringComparer.Ordinal);

        #region Styles & Theme Colors
        private static class UIStyles
        {
            public static GUIStyle HeaderBar;
            public static GUIStyle ToolBar;
            public static GUIStyle ToolbarLabel;
            public static GUIStyle SidebarItemTitle;
            public static GUIStyle SidebarItemSub;
            public static GUIStyle TableHeaderLabel;
            public static GUIStyle TextAreaStyle;
            public static GUIStyle StatusBarStyle;
            public static GUIStyle BadgeStyle;
            public static GUIStyle UnsavedBadgeStyle;
            public static GUIStyle SyncedBadgeStyle;
            public static GUIStyle SectionHeaderLabel;

            private static bool isInitialized;

            public static void EnsureInitialized()
            {
                if (isInitialized) return;
                isInitialized = true;

                bool isDark = EditorGUIUtility.isProSkin;

                HeaderBar = new GUIStyle(EditorStyles.toolbar)
                {
                    padding = new RectOffset(8, 8, 3, 3),
                    fixedHeight = 30f
                };

                ToolBar = new GUIStyle(EditorStyles.toolbar)
                {
                    padding = new RectOffset(8, 8, 3, 3),
                    fixedHeight = 28f
                };

                ToolbarLabel = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleLeft,
                    fontSize = 11,
                    fixedHeight = 20f,
                    margin = new RectOffset(0, 4, 0, 0),
                    padding = new RectOffset(0, 0, 0, 0),
                    normal = { textColor = isDark ? new Color(0.85f, 0.85f, 0.85f) : new Color(0.2f, 0.2f, 0.2f) }
                };

                SidebarItemTitle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 12,
                    margin = new RectOffset(0, 0, 0, 0),
                    padding = new RectOffset(0, 0, 0, 0)
                };

                SidebarItemSub = new GUIStyle(EditorStyles.miniLabel)
                {
                    fontSize = 10,
                    normal = { textColor = isDark ? new Color(0.72f, 0.72f, 0.72f) : new Color(0.38f, 0.38f, 0.38f) },
                    margin = new RectOffset(0, 0, 0, 0),
                    padding = new RectOffset(0, 0, 0, 0)
                };

                TableHeaderLabel = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleLeft,
                    fontSize = 11,
                    normal = { textColor = isDark ? new Color(0.88f, 0.88f, 0.88f) : new Color(0.18f, 0.18f, 0.18f) }
                };

                SectionHeaderLabel = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 12,
                    alignment = TextAnchor.MiddleLeft,
                    normal = { textColor = isDark ? new Color(0.9f, 0.92f, 0.95f) : new Color(0.15f, 0.18f, 0.22f) }
                };

                TextAreaStyle = new GUIStyle(EditorStyles.textArea)
                {
                    wordWrap = true,
                    padding = new RectOffset(6, 6, 6, 6),
                    fontSize = 12
                };

                StatusBarStyle = new GUIStyle(EditorStyles.toolbar)
                {
                    fixedHeight = 24f,
                    padding = new RectOffset(8, 8, 2, 2)
                };

                BadgeStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 10,
                    fontStyle = FontStyle.Bold,
                    padding = new RectOffset(6, 6, 2, 2),
                    normal = { textColor = isDark ? new Color(0.85f, 0.85f, 0.85f) : new Color(0.25f, 0.25f, 0.25f) }
                };

                UnsavedBadgeStyle = new GUIStyle(BadgeStyle)
                {
                    normal = { textColor = new Color(1f, 0.65f, 0.15f) }
                };

                SyncedBadgeStyle = new GUIStyle(BadgeStyle)
                {
                    normal = { textColor = new Color(0.3f, 0.85f, 0.4f) }
                };
            }
        }
        #endregion

        [MenuItem("Cowart/语言管理/打开编辑器")]
        public static void Open()
        {
            OpenWindow();
        }

        /// <summary>打开管理器当前配置的多语言文本文件。</summary>
        /// <param name="fileName">相对于 StreamingAssets 的配置文件名。</param>
        public static void OpenManagerFile(string fileName)
        {
            string effectiveFileName = string.IsNullOrWhiteSpace(fileName)
                ? Path.GetFileName(DefaultPath)
                : fileName;
            MultilingualTextEditorWindow window = OpenWindow();
            window.LoadManagerFile(Path.Combine(Application.streamingAssetsPath, effectiveFileName));
        }

        private static MultilingualTextEditorWindow OpenWindow()
        {
            MultilingualTextEditorWindow window = GetWindow<MultilingualTextEditorWindow>();
            window.titleContent = new GUIContent("多语言文本", GetIcon("TextAsset Icon"));
            window.minSize = new Vector2(920f, 580f);
            window.Show();
            window.Focus();
            return window;
        }

        private static Texture GetIcon(string name)
        {
            GUIContent content = EditorGUIUtility.IconContent(name);
            return content != null ? content.image : null;
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("多语言文本", GetIcon("TextAsset Icon"));
            minSize = new Vector2(920f, 580f);
            saveChangesMessage = "多语言文本尚未保存，是否保存？";
            viewMode = (ViewMode)Mathf.Clamp(EditorPrefs.GetInt(ViewModeKey, 0), 0, 1);
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
            EditorApplication.projectChanged += ClearAudioClipCache;
            EnsureData();

            if (table.items.Count == 0 && File.Exists(ResolvePath(filePath)))
            {
                LoadFile(filePath, false);
            }
            else if (string.IsNullOrEmpty(savedJson))
            {
                savedJson = JsonUtility.ToJson(table, true);
                hasUnsavedChanges = false;
            }
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            EditorApplication.projectChanged -= ClearAudioClipCache;
        }

        private void OnUndoRedoPerformed()
        {
            EnsureData();
            ClampSelection();
            validationDirty = true;
            UpdateUnsavedState();
            SetStatus("已执行撤销或重做。", MessageType.Info);
        }

        private void OnGUI()
        {
            UIStyles.EnsureInitialized();
            EnsureData();

            // 文本编辑期间只记录一次 Undo，避免每次按键或点击都序列化整张语言表。
            if (Event.current.type == EventType.KeyDown
                && EditorGUIUtility.editingTextField
                && GUIUtility.keyboardControl != 0
                && undoRecordedKeyboardControl != GUIUtility.keyboardControl)
            {
                RecordUndo("编辑多语言文本");
                undoRecordedKeyboardControl = GUIUtility.keyboardControl;
            }

            // Clear text field focus on mouse click or Escape / Enter
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                GUI.FocusControl(null);
                undoRecordedKeyboardControl = 0;
            }
            else if (Event.current.type == EventType.KeyDown)
            {
                if (Event.current.keyCode == KeyCode.Escape)
                {
                    GUI.FocusControl(null);
                    undoRecordedKeyboardControl = 0;
                    Repaint();
                }
                else if ((Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) && !Event.current.shift)
                {
                    GUI.FocusControl(null);
                    undoRecordedKeyboardControl = 0;
                    Repaint();
                }
            }

            DrawHeaderToolbar();
            DrawSearchAndControlsBar();
            if (showFindReplace)
            {
                DrawFindReplacePanel();
            }

            EditorGUILayout.Space(2f);

            if (viewMode == ViewMode.Table)
            {
                DrawTableView();
            }
            else
            {
                DrawDetailView();
            }

            DrawStatusBar();
        }

        #region Header Toolbar
        private void DrawHeaderToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(UIStyles.HeaderBar))
            {
                GUILayout.Label(new GUIContent(GetIcon("TextAsset Icon")), GUILayout.Width(20f), GUILayout.Height(20f));
                GUILayout.Label("配置文件:", UIStyles.ToolbarLabel, GUILayout.Width(60f));

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField(filePath, EditorStyles.toolbarTextField, GUILayout.MinWidth(220f), GUILayout.Height(20f));
                }

                if (GUILayout.Button(new GUIContent(GetIcon("Folder Icon"), "选择 JSON 文件"), EditorStyles.toolbarButton, GUILayout.Width(28f), GUILayout.Height(20f)))
                {
                    SelectFile();
                }

                if (GUILayout.Button(new GUIContent(GetIcon("Refresh"), "重新加载文件"), EditorStyles.toolbarButton, GUILayout.Width(28f), GUILayout.Height(20f)))
                {
                    ReloadFile();
                }

                using (new GUIColorScope(hasUnsavedChanges ? new Color(1f, 0.75f, 0.3f) : Color.white))
                {
                    if (GUILayout.Button(new GUIContent(GetIcon("SaveActive"), "保存文件 (Ctrl+S)"), EditorStyles.toolbarButton, GUILayout.Width(28f), GUILayout.Height(20f)))
                    {
                        SaveFile();
                    }
                }

                if (GUILayout.Button(new GUIContent(GetIcon("Project"), "在资源管理器中显示"), EditorStyles.toolbarButton, GUILayout.Width(28f), GUILayout.Height(20f)))
                {
                    RevealFile();
                }

                if (GUILayout.Button(new GUIContent(GetIcon("Toolbar Plus"), "创建示例文件"), EditorStyles.toolbarButton, GUILayout.Width(28f), GUILayout.Height(20f)))
                {
                    CreateExampleFile();
                }

                if (GUILayout.Button(new GUIContent("翻译配置", "配置百度翻译 APP ID、密钥和原始语言"), EditorStyles.toolbarButton, GUILayout.Width(68f), GUILayout.Height(20f)))
                {
                    BaiduTranslationEditorWindow.Open(this);
                }

                if (GUILayout.Button(new GUIContent("翻译整表", "使用已配置的原始语言翻译整表中所有已添加的目标语言"), EditorStyles.toolbarButton, GUILayout.Width(72f), GUILayout.Height(20f)))
                {
                    GUI.FocusControl(null);
                    BaiduTranslationEditorWindow.TranslateTable(this);
                }

                GUILayout.FlexibleSpace();

                GUILayout.Label($"{table.items.Count} 条记录", UIStyles.BadgeStyle, GUILayout.Width(65f));

                if (hasUnsavedChanges)
                {
                    GUILayout.Label("● 未保存", UIStyles.UnsavedBadgeStyle, GUILayout.Width(60f));
                }
                else
                {
                    GUILayout.Label("● 已同步", UIStyles.SyncedBadgeStyle, GUILayout.Width(60f));
                }
            }
        }
        #endregion

        #region Search & Controls Bar
        private void DrawSearchAndControlsBar()
        {
            using (new EditorGUILayout.HorizontalScope(UIStyles.ToolBar))
            {
                GUILayout.Label("搜索:", UIStyles.ToolbarLabel, GUILayout.Width(35f));
                
                string newSearch = GUILayout.TextField(
                    searchText, 
                    GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.textField, 
                    GUILayout.Width(190f),
                    GUILayout.Height(20f));

                if (!string.IsNullOrEmpty(searchText))
                {
                    if (GUILayout.Button("", GUI.skin.FindStyle("ToolbarSearchCancelButton") ?? EditorStyles.miniButton, GUILayout.Height(20f)))
                    {
                        newSearch = string.Empty;
                        GUI.FocusControl(null);
                    }
                }
                else
                {
                    GUILayout.Box("", GUI.skin.FindStyle("ToolbarSearchCancelButtonEmpty") ?? GUIStyle.none, GUILayout.Height(20f));
                }

                if (newSearch != searchText)
                {
                    searchText = newSearch;
                    EnsureValidSelectionForSearch();
                    Repaint();
                }

                GUILayout.Space(12f);

                int currentMode = (int)viewMode;
                GUIContent[] modes = new[]
                {
                    new GUIContent(" ☰ 表格视图 ", GetIcon("d_HorizontalLayoutGroup Icon")),
                    new GUIContent(" 📑 详情视图 ", GetIcon("d_VerticalLayoutGroup Icon"))
                };
                int newMode = GUILayout.Toolbar(currentMode, modes, EditorStyles.toolbarButton, GUILayout.Width(200f), GUILayout.Height(20f));
                if (newMode != currentMode)
                {
                    viewMode = (ViewMode)newMode;
                    EditorPrefs.SetInt(ViewModeKey, newMode);
                }

                GUILayout.Space(14f);

                GUILayout.Label("预览语言:", UIStyles.ToolbarLabel, GUILayout.Width(60f));
                previewLanguage = DrawLanguageDropdown(previewLanguage, GUILayout.Width(170f), GUILayout.Height(20f));

                GUILayout.FlexibleSpace();

                showFindReplace = GUILayout.Toggle(
                    showFindReplace,
                    showFindReplace ? "隐藏替换 ▲" : "查找替换 ▼",
                    EditorStyles.toolbarButton,
                    GUILayout.Width(85f),
                    GUILayout.Height(20f));

                GUILayout.Space(6f);

                using (new GUIColorScope(EditorGUIUtility.isProSkin ? new Color(0.25f, 0.6f, 0.95f) : new Color(0.85f, 0.93f, 1f)))
                {
                    if (GUILayout.Button(new GUIContent(" + 新增记录", GetIcon("CreateAddNew")), EditorStyles.toolbarButton, GUILayout.Width(92f), GUILayout.Height(20f)))
                    {
                        AddItem();
                    }
                }
            }
        }
        #endregion

        #region Find & Replace Panel
        private void DrawFindReplacePanel()
        {
            using (new EditorGUILayout.HorizontalScope(UIStyles.ToolBar))
            {
                GUILayout.Space(4f);
                GUILayout.Label("查找:", UIStyles.ToolbarLabel, GUILayout.Width(32f));
                findText = EditorGUILayout.TextField(findText, EditorStyles.toolbarTextField, GUILayout.MinWidth(120f), GUILayout.Height(20f));
                GUILayout.Space(10f);

                GUILayout.Label("替换:", UIStyles.ToolbarLabel, GUILayout.Width(32f));
                replaceText = EditorGUILayout.TextField(replaceText, EditorStyles.toolbarTextField, GUILayout.MinWidth(120f), GUILayout.Height(20f));
                GUILayout.Space(10f);

                ignoreCase = GUILayout.Toggle(ignoreCase, "忽略大小写", EditorStyles.toolbarButton, GUILayout.Width(85f), GUILayout.Height(20f));

                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(findText)))
                {
                    if (GUILayout.Button("查找下一个", EditorStyles.toolbarButton, GUILayout.Width(76f), GUILayout.Height(20f)))
                    {
                        SelectNextMatch();
                    }

                    if (GUILayout.Button("替换当前", EditorStyles.toolbarButton, GUILayout.Width(68f), GUILayout.Height(20f)))
                    {
                        ReplaceCurrent();
                    }

                    if (GUILayout.Button("全部替换", EditorStyles.toolbarButton, GUILayout.Width(68f), GUILayout.Height(20f)))
                    {
                        ReplaceAll();
                    }
                }
                GUILayout.Space(4f);
            }
        }
        #endregion

        #region Table View
        private void CalculateTableColumnWidths(float totalWidth, out float idWidth, out float textWidth, out float audioWidth, out float opWidth)
        {
            opWidth = 110f; // Action buttons width: Copy (50px) + Del (50px) + Gap (10px)
            float available = Mathf.Max(totalWidth - opWidth - 32f, 400f);
            idWidth = Mathf.Clamp(available * 0.24f, 160f, 260f);
            audioWidth = Mathf.Clamp(available * 0.28f, 180f, 280f);
            textWidth = Mathf.Max(available - idWidth - audioWidth, 200f);
        }

        private void DrawTableView()
        {
            float containerWidth = Mathf.Max(position.width - 16f, 860f);
            CalculateTableColumnWidths(containerWidth, out float idWidth, out float textWidth, out float audioWidth, out float opWidth);

            // Table Header Bar
            Rect headerRect = EditorGUILayout.GetControlRect(false, 26f);
            Color headerBg = EditorGUIUtility.isProSkin ? new Color(0.16f, 0.16f, 0.17f, 1f) : new Color(0.85f, 0.85f, 0.87f, 1f);
            EditorGUI.DrawRect(headerRect, headerBg);

            Rect rIDHeader = new Rect(headerRect.x + 8f, headerRect.y + 4f, idWidth, 18f);
            Rect rTextHeader = new Rect(rIDHeader.xMax + 8f, headerRect.y + 4f, textWidth, 18f);
            Rect rAudioHeader = new Rect(rTextHeader.xMax + 8f, headerRect.y + 4f, audioWidth, 18f);
            Rect rOpHeader = new Rect(rAudioHeader.xMax + 8f, headerRect.y + 4f, opWidth, 18f);

            GUI.Label(rIDHeader, "ID 标识", UIStyles.TableHeaderLabel);
            GUI.Label(rTextHeader, $"文本内容 ({GetLanguageSimpleName(previewLanguage)})", UIStyles.TableHeaderLabel);
            GUI.Label(rAudioHeader, "音频路径", UIStyles.TableHeaderLabel);
            GUI.Label(rOpHeader, "操作", UIStyles.TableHeaderLabel);

            using (EditorGUILayout.ScrollViewScope scroll = new EditorGUILayout.ScrollViewScope(tableScroll))
            {
                tableScroll = scroll.scrollPosition;
                int visibleIndex = 0;

                for (int i = 0; i < table.items.Count; i++)
                {
                    MultilingualTextItem item = table.items[i];
                    if (item == null || !MatchesSearch(item))
                    {
                        continue;
                    }

                    visibleIndex++;
                    MultilingualTextTranslation translation = FindTranslation(item, previewLanguage);
                    string content = translation != null ? translation.content : string.Empty;
                    string audioPath = translation != null ? translation.audioPath : string.Empty;

                    Rect rowRect = EditorGUILayout.GetControlRect(false, 30f);
                    bool isSelected = (i == selectedIndex);

                    Color rowBg = isSelected
                        ? (EditorGUIUtility.isProSkin ? new Color(0.18f, 0.28f, 0.40f, 1f) : new Color(0.82f, 0.90f, 1f, 1f))
                        : (visibleIndex % 2 == 0
                            ? (EditorGUIUtility.isProSkin ? new Color(0.21f, 0.21f, 0.22f, 1f) : new Color(0.96f, 0.96f, 0.96f, 1f))
                            : (EditorGUIUtility.isProSkin ? new Color(0.24f, 0.24f, 0.25f, 1f) : new Color(0.91f, 0.91f, 0.91f, 1f)));

                    EditorGUI.DrawRect(rowRect, rowBg);
                    if (isSelected)
                    {
                        EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, 4f, rowRect.height), new Color(0.23f, 0.53f, 1f, 1f));
                    }

                    if (Event.current.type == EventType.MouseDown && rowRect.Contains(Event.current.mousePosition))
                    {
                        selectedIndex = i;
                        GUI.FocusControl(null);
                        Repaint();
                    }

                    Rect rID = new Rect(rowRect.x + 8f, rowRect.y + 4f, idWidth, 22f);
                    Rect rText = new Rect(rID.xMax + 8f, rowRect.y + 4f, textWidth, 22f);
                    Rect rAudio = new Rect(rText.xMax + 8f, rowRect.y + 4f, audioWidth, 22f);
                    Rect rCopy = new Rect(rAudio.xMax + 8f, rowRect.y + 4f, 50f, 22f);
                    Rect rDel = new Rect(rCopy.xMax + 4f, rowRect.y + 4f, 50f, 22f);

                    int newId = EditorGUI.IntField(rID, item.id);

                    EditorGUI.BeginChangeCheck();
                    content = EditorGUI.TextField(rText, content);
                    audioPath = EditorGUI.TextField(rAudio, audioPath);
                    if (EditorGUI.EndChangeCheck())
                    {
                        translation = GetOrCreateTranslation(item, previewLanguage);
                        translation.content = content;
                        translation.audioPath = audioPath;
                        MarkChanged();
                    }

                    if (newId != item.id)
                    {
                        item.id = newId;
                        MarkChanged();
                    }

                    if (GUI.Button(rCopy, new GUIContent("复制", GetIcon("TreeEditor.Duplicate")), EditorStyles.miniButtonLeft))
                    {
                        GUI.FocusControl(null);
                        DuplicateItem(i);
                        GUIUtility.ExitGUI();
                    }

                    if (GUI.Button(rDel, new GUIContent("删除", GetIcon("TreeEditor.Trash")), EditorStyles.miniButtonRight))
                    {
                        GUI.FocusControl(null);
                        DeleteItem(i);
                        GUIUtility.ExitGUI();
                    }
                }

                if (visibleIndex == 0)
                {
                    EditorGUILayout.Space(20f);
                    EditorGUILayout.HelpBox("没有匹配的多语言记录。", MessageType.Info);
                }
            }
        }
        #endregion

        #region Detail View
        private void DrawDetailView()
        {
            ClampSelection();
            using (new EditorGUILayout.HorizontalScope())
            {
                float sidebarWidth = Mathf.Clamp(position.width * 0.28f, 230f, 300f);
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(sidebarWidth)))
                {
                    Rect sbHeaderRect = EditorGUILayout.GetControlRect(false, 26f);
                    Color sbHeaderBg = EditorGUIUtility.isProSkin ? new Color(0.18f, 0.18f, 0.19f, 1f) : new Color(0.86f, 0.86f, 0.88f, 1f);
                    EditorGUI.DrawRect(sbHeaderRect, sbHeaderBg);
                    GUI.Label(new Rect(sbHeaderRect.x + 8f, sbHeaderRect.y + 4f, 120f, 18f), "记录列表", EditorStyles.boldLabel);

                    using (EditorGUILayout.ScrollViewScope scroll = new EditorGUILayout.ScrollViewScope(sidebarScroll))
                    {
                        sidebarScroll = scroll.scrollPosition;
                        for (int i = 0; i < table.items.Count; i++)
                        {
                            MultilingualTextItem item = table.items[i];
                            if (item == null || !MatchesSearch(item))
                            {
                                continue;
                            }

                            string preview = GetPreview(item, previewLanguage);
                            Rect itemRect = EditorGUILayout.GetControlRect(false, 44f);
                            bool isSelected = (i == selectedIndex);

                            Color itemBg = isSelected
                                ? (EditorGUIUtility.isProSkin ? new Color(0.18f, 0.28f, 0.40f, 1f) : new Color(0.82f, 0.90f, 1f, 1f))
                                : (i % 2 == 0
                                    ? (EditorGUIUtility.isProSkin ? new Color(0.22f, 0.22f, 0.23f, 1f) : new Color(0.96f, 0.96f, 0.96f, 1f))
                                    : (EditorGUIUtility.isProSkin ? new Color(0.25f, 0.25f, 0.26f, 1f) : new Color(0.91f, 0.91f, 0.91f, 1f)));

                            EditorGUI.DrawRect(itemRect, itemBg);
                            if (isSelected)
                            {
                                EditorGUI.DrawRect(new Rect(itemRect.x, itemRect.y, 4f, itemRect.height), new Color(0.23f, 0.53f, 1f, 1f));
                            }

                            Rect rTitle = new Rect(itemRect.x + 10f, itemRect.y + 4f, itemRect.width - 16f, 18f);
                            Rect rSub = new Rect(rTitle.x, rTitle.y + 18f, rTitle.width, 16f);

                            GUI.Label(rTitle, item.id <= 0 ? "(无效 ID)" : item.id.ToString(), UIStyles.SidebarItemTitle);
                            GUI.Label(rSub, string.IsNullOrEmpty(preview) ? "(无文本)" : preview, UIStyles.SidebarItemSub);

                            if (Event.current.type == EventType.MouseDown && itemRect.Contains(Event.current.mousePosition))
                            {
                                selectedIndex = i;
                                GUI.FocusControl(null);
                                Repaint();
                            }
                        }
                    }
                }

                Rect divRect = EditorGUILayout.GetControlRect(false, GUILayout.Width(2f), GUILayout.ExpandHeight(true));
                EditorGUI.DrawRect(divRect, EditorGUIUtility.isProSkin ? new Color(0.15f, 0.15f, 0.15f, 1f) : new Color(0.75f, 0.75f, 0.75f, 1f));

                int matchingCount = 0;
                for (int i = 0; i < table.items.Count; i++)
                {
                    if (table.items[i] != null && MatchesSearch(table.items[i]))
                    {
                        matchingCount++;
                    }
                }

                if (matchingCount == 0)
                {
                    using (new EditorGUILayout.VerticalScope())
                    {
                        EditorGUILayout.Space(20f);
                        EditorGUILayout.HelpBox(
                            string.IsNullOrEmpty(searchText)
                                ? "当前没有记录，请点击顶部“+ 新增记录”。"
                                : $"未找到匹配“{searchText}”的多语言记录。", 
                            MessageType.Info);
                    }
                    return;
                }

                MultilingualTextItem selected = table.items[selectedIndex];
                if (selected == null)
                {
                    selected = new MultilingualTextItem();
                    table.items[selectedIndex] = selected;
                    MarkChanged();
                }

                using (new EditorGUILayout.VerticalScope())
                using (EditorGUILayout.ScrollViewScope scroll = new EditorGUILayout.ScrollViewScope(detailScroll))
                {
                    detailScroll = scroll.scrollPosition;

                    using (new EditorGUILayout.HorizontalScope(UIStyles.ToolBar))
                    {
                        GUILayout.Label(new GUIContent(GetIcon("TextAsset Icon")), GUILayout.Width(20f), GUILayout.Height(20f));
                        GUILayout.Label("记录详情", UIStyles.SectionHeaderLabel, GUILayout.Width(75f));
                        GUILayout.Space(12f);

                        GUILayout.Label("ID:", UIStyles.ToolbarLabel, GUILayout.Width(24f));
                        int newSelectedId = EditorGUILayout.IntField(selected.id, EditorStyles.toolbarTextField, GUILayout.MinWidth(180f), GUILayout.Height(20f));
                        if (newSelectedId != selected.id)
                        {
                            selected.id = newSelectedId;
                            MarkChanged();
                        }

                        GUILayout.FlexibleSpace();

                        if (GUILayout.Button(new GUIContent("复制记录", GetIcon("TreeEditor.Duplicate")), EditorStyles.toolbarButton, GUILayout.Width(82f), GUILayout.Height(20f)))
                        {
                            GUI.FocusControl(null);
                            DuplicateItem(selectedIndex);
                            GUIUtility.ExitGUI();
                        }

                        if (GUILayout.Button(new GUIContent("删除记录", GetIcon("TreeEditor.Trash")), EditorStyles.toolbarButton, GUILayout.Width(82f), GUILayout.Height(20f)))
                        {
                            GUI.FocusControl(null);
                            DeleteItem(selectedIndex);
                            GUIUtility.ExitGUI();
                        }
                    }

                    EditorGUILayout.Space(4f);
                    DrawTranslationsDetail(selected);
                }
            }
        }

        private void DrawLanguageTabBar(MultilingualTextItem item)
        {
            if (item.translations == null || item.translations.Count == 0)
            {
                return;
            }

            activeLanguageIndex = Mathf.Clamp(activeLanguageIndex, 0, item.translations.Count - 1);

            using (new EditorGUILayout.HorizontalScope(UIStyles.ToolBar))
            {
                GUILayout.Label("语言页签:", UIStyles.ToolbarLabel, GUILayout.Width(60f));

                for (int i = 0; i < item.translations.Count; i++)
                {
                    MultilingualTextTranslation t = item.translations[i];
                    if (t == null) continue;

                    bool isActive = (i == activeLanguageIndex && !detailStackedView);
                    string tabName = GetLanguageSimpleName(t.language);

                    using (new GUIColorScope(isActive ? (EditorGUIUtility.isProSkin ? new Color(0.25f, 0.6f, 0.95f) : new Color(0.75f, 0.88f, 1f)) : Color.white))
                    {
                        if (GUILayout.Button(tabName, EditorStyles.toolbarButton, GUILayout.MinWidth(75f), GUILayout.Height(20f)))
                        {
                            activeLanguageIndex = i;
                            detailStackedView = false;
                            GUI.FocusControl(null);
                        }
                    }
                }

                Rect addBtnRect = EditorGUILayout.GetControlRect(false, 20f, GUILayout.Width(88f));
                if (GUI.Button(addBtnRect, "＋ 添加语言 ▾", EditorStyles.toolbarButton))
                {
                    GUI.FocusControl(null);
                    ShowAddLanguageMenu(item, addBtnRect);
                }

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("翻译记录", EditorStyles.toolbarButton, GUILayout.Width(72f), GUILayout.Height(20f)))
                {
                    GUI.FocusControl(null);
                    BaiduTranslationEditorWindow.TranslateItem(this, item);
                }

                GUILayout.Space(4f);

                string modeLabel = detailStackedView ? "📜 平铺视图" : "📑 页签视图";
                if (GUILayout.Button(modeLabel, EditorStyles.toolbarButton, GUILayout.Width(82f), GUILayout.Height(20f)))
                {
                    detailStackedView = !detailStackedView;
                    GUI.FocusControl(null);
                }
            }
        }

        private void ShowAddLanguageMenu(MultilingualTextItem item, Rect buttonRect)
        {
            GenericMenu menu = new GenericMenu();
            List<SupportedLanguage> missing = GetMissingLanguages(item);

            if (missing.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("已包含所有支持的语言"));
            }
            else
            {
                for (int i = 0; i < missing.Count; i++)
                {
                    SupportedLanguage lang = missing[i];
                    menu.AddItem(new GUIContent(GetLanguageDisplayName(lang)), false, () =>
                    {
                        RecordUndo("添加语言");
                        if (item.translations == null)
                        {
                            item.translations = new List<MultilingualTextTranslation>();
                        }
                        item.translations.Add(new MultilingualTextTranslation
                        {
                            language = lang,
                            content = string.Empty
                        });
                        activeLanguageIndex = item.translations.Count - 1;
                        MarkChanged();
                    });
                }
            }

            if (buttonRect.width > 0 && buttonRect.height > 0)
            {
                menu.DropDown(buttonRect);
            }
            else
            {
                menu.ShowAsContext();
            }
        }

        private static List<SupportedLanguage> GetMissingLanguages(MultilingualTextItem item)
        {
            List<SupportedLanguage> list = new List<SupportedLanguage>();
            Array values = Enum.GetValues(typeof(SupportedLanguage));
            for (int i = 0; i < values.Length; i++)
            {
                SupportedLanguage lang = (SupportedLanguage)values.GetValue(i);
                if (item.translations == null || !item.translations.Exists(t => t != null && t.language == lang))
                {
                    list.Add(lang);
                }
            }
            return list;
        }

        private void DrawTranslationsDetail(MultilingualTextItem item)
        {
            if (item.translations == null || item.translations.Count == 0)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.Space(10f);
                    EditorGUILayout.HelpBox("该记录暂无语言翻译数据。", MessageType.Info);
                    Rect addBtnRect = EditorGUILayout.GetControlRect(false, 28f);
                    if (GUI.Button(addBtnRect, "＋ 添加语言翻译 ▾", GUI.skin.button))
                    {
                        GUI.FocusControl(null);
                        ShowAddLanguageMenu(item, addBtnRect);
                    }
                }
                return;
            }

            DrawLanguageTabBar(item);
            EditorGUILayout.Space(6f);

            if (detailStackedView)
            {
                for (int i = 0; i < item.translations.Count; i++)
                {
                    DrawSingleLanguageCard(item, i);
                    EditorGUILayout.Space(8f);
                }
            }
            else
            {
                activeLanguageIndex = Mathf.Clamp(activeLanguageIndex, 0, item.translations.Count - 1);
                DrawSingleLanguageCard(item, activeLanguageIndex);
            }
        }

        private void DrawSingleLanguageCard(MultilingualTextItem item, int index)
        {
            if (index < 0 || index >= item.translations.Count) return;
            MultilingualTextTranslation translation = item.translations[index];
            if (translation == null) return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope(UIStyles.ToolBar))
                {
                    Rect headerRect = EditorGUILayout.GetControlRect(false, 22f);
                    Rect rLang = new Rect(headerRect.x, headerRect.y + 1f, 240f, 20f);
                    Rect rDelLang = new Rect(headerRect.xMax - 80f, headerRect.y + 1f, 80f, 20f);
                    Rect rTranslate = new Rect(rDelLang.x - 84f, headerRect.y + 1f, 80f, 20f);

                    SupportedLanguage newLanguage = DrawLanguageDropdownRect(rLang, translation.language);
                    if (newLanguage != translation.language)
                    {
                        RecordUndo("修改翻译语言");
                        translation.language = newLanguage;
                        MarkChanged();
                    }

                    bool isSourceLanguage = BaiduTranslationEditorWindow.IsSourceLanguage(translation.language);
                    if (!isSourceLanguage)
                    {
                        if (GUI.Button(rTranslate, new GUIContent("翻译", "只重新生成当前语言的文本内容"), EditorStyles.toolbarButton))
                        {
                            GUI.FocusControl(null);
                            BaiduTranslationEditorWindow.TranslateLanguage(this, item, translation.language);
                        }
                    }

                    if (GUI.Button(rDelLang, new GUIContent("删除语言", GetIcon("TreeEditor.Trash")), EditorStyles.toolbarButton))
                    {
                        GUI.FocusControl(null);
                        RecordUndo("删除语言");
                        item.translations.RemoveAt(index);
                        activeLanguageIndex = Mathf.Clamp(activeLanguageIndex - 1, 0, Mathf.Max(0, item.translations.Count - 1));
                        MarkChanged();
                        GUIUtility.ExitGUI();
                    }
                }

                EditorGUILayout.Space(6f);

                string newAudioPath = DrawAudioPathField(translation.audioPath);
                if (!string.Equals(newAudioPath, translation.audioPath, StringComparison.Ordinal))
                {
                    translation.audioPath = newAudioPath;
                    MarkChanged();
                }

                EditorGUILayout.Space(8f);

                GUILayout.Label("文本内容 (Content):", UIStyles.SectionHeaderLabel);

                EditorGUILayout.Space(4f);

                string newContent = EditorGUILayout.TextArea(
                    translation.content ?? string.Empty,
                    UIStyles.TextAreaStyle,
                    GUILayout.MinHeight(80f));
                if (!string.Equals(newContent, translation.content, StringComparison.Ordinal))
                {
                    translation.content = newContent;
                    MarkChanged();
                }

                EditorGUILayout.Space(4f);
            }
        }

        private string DrawAudioPathField(string currentPath)
        {
            string newPath;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("音频路径:", UIStyles.ToolbarLabel, GUILayout.Width(64f));
                newPath = EditorGUILayout.TextField(currentPath, GUILayout.MinWidth(140f), GUILayout.Height(20f));

                AudioClip clip = null;
                if (!string.IsNullOrEmpty(newPath))
                {
                    clip = GetCachedAudioClip(newPath);
                }

                EditorGUI.BeginChangeCheck();
                AudioClip newClip = (AudioClip)EditorGUILayout.ObjectField(clip, typeof(AudioClip), false, GUILayout.Width(140f), GUILayout.Height(20f));
                if (EditorGUI.EndChangeCheck() && newClip != null)
                {
                    string path = AssetDatabase.GetAssetPath(newClip);
                    if (path.StartsWith("Assets/Resources/"))
                    {
                        path = path.Substring("Assets/Resources/".Length);
                        int extIdx = path.LastIndexOf('.');
                        if (extIdx > 0) path = path.Substring(0, extIdx);
                        newPath = path;
                    }
                    else
                    {
                        SetStatus($"提示：选中的音频“{newClip.name}”需放置在 Resources 目录下在运行时加载。", MessageType.Warning);
                        int extIdx = path.LastIndexOf('.');
                        if (extIdx > 0) path = path.Substring(0, extIdx);
                        newPath = path;
                    }
                }
            }

            return newPath;
        }
        #endregion

        #region Language Display Helpers
        private static SupportedLanguage DrawLanguageDropdown(SupportedLanguage current, params GUILayoutOption[] options)
        {
            Array values = Enum.GetValues(typeof(SupportedLanguage));
            string[] names = new string[values.Length];
            int currentIndex = 0;

            for (int i = 0; i < values.Length; i++)
            {
                SupportedLanguage lang = (SupportedLanguage)values.GetValue(i);
                names[i] = GetLanguageDisplayName(lang);
                if (lang == current) currentIndex = i;
            }

            int newIndex = EditorGUILayout.Popup(currentIndex, names, EditorStyles.toolbarPopup, options);
            return (SupportedLanguage)values.GetValue(newIndex);
        }

        private static SupportedLanguage DrawLanguageDropdownRect(Rect position, SupportedLanguage current)
        {
            Array values = Enum.GetValues(typeof(SupportedLanguage));
            string[] names = new string[values.Length];
            int currentIndex = 0;

            for (int i = 0; i < values.Length; i++)
            {
                SupportedLanguage lang = (SupportedLanguage)values.GetValue(i);
                names[i] = GetLanguageDisplayName(lang);
                if (lang == current) currentIndex = i;
            }

            int newIndex = EditorGUI.Popup(position, currentIndex, names);
            return (SupportedLanguage)values.GetValue(newIndex);
        }

        private static string GetLanguageDisplayName(SupportedLanguage language)
        {
            switch (language)
            {
                case SupportedLanguage.ChineseSimplified: return "🇨🇳 简体中文 (ChineseSimplified)";
                case SupportedLanguage.English: return "🇺🇸 English (英语)";
                case SupportedLanguage.Japanese: return "🇯🇵 日本語 (日语)";
                case SupportedLanguage.Korean: return "🇰🇷 한국어 (韩语)";
                case SupportedLanguage.French: return "🇫🇷 Français (法语)";
                case SupportedLanguage.Spanish: return "Español (西班牙语)";
                case SupportedLanguage.Russian: return "🇷🇺 Русский (俄语)";
                case SupportedLanguage.German: return "🇩🇪 Deutsch (德语)";
                case SupportedLanguage.Italian: return "🇮🇹 Italiano (意大利语)";
                case SupportedLanguage.Portuguese: return "🇵🇹 Português (葡萄牙语)";
                default: return language.ToString();
            }
        }

        private static string GetLanguageSimpleName(SupportedLanguage language)
        {
            switch (language)
            {
                case SupportedLanguage.ChineseSimplified: return "简体中文";
                case SupportedLanguage.English: return "English";
                case SupportedLanguage.Japanese: return "日本語";
                case SupportedLanguage.Korean: return "한국어";
                case SupportedLanguage.French: return "Français";
                case SupportedLanguage.Spanish: return "Español";
                case SupportedLanguage.Russian: return "Русский";
                case SupportedLanguage.German: return "Deutsch";
                case SupportedLanguage.Italian: return "Italiano";
                case SupportedLanguage.Portuguese: return "Português";
                default: return language.ToString();
            }
        }
        #endregion

        #region Status Bar
        private void DrawStatusBar()
        {
            using (new EditorGUILayout.HorizontalScope(UIStyles.StatusBarStyle))
            {
                string validationMessage = BuildValidationMessage(out MessageType validationType);
                MessageType displayType = string.IsNullOrEmpty(statusMessage) ? validationType : (MessageType)Mathf.Max((int)statusType, (int)validationType);
                string textToShow = string.IsNullOrEmpty(statusMessage) ? validationMessage : (statusMessage + "   |   " + validationMessage);

                Texture icon = null;
                switch (displayType)
                {
                    case MessageType.Error: icon = GetIcon("console.erroricon.sml"); break;
                    case MessageType.Warning: icon = GetIcon("console.warnicon.sml"); break;
                    default: icon = GetIcon("console.infoicon.sml"); break;
                }

                if (icon != null)
                {
                    GUILayout.Label(icon, GUILayout.Width(16f), GUILayout.Height(16f));
                }

                GUILayout.Label(textToShow, EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                GUILayout.Label($"Total: {table.items.Count}", EditorStyles.miniLabel);
            }
        }
        #endregion

        #region Operations & File Logic
        private void CreateExampleFile()
        {
            string selectedPath = EditorUtility.SaveFilePanel(
                "创建多语言文本示例",
                Application.streamingAssetsPath,
                "multilingual_text",
                "json");
            if (string.IsNullOrEmpty(selectedPath))
            {
                return;
            }

            CreateExampleFile(selectedPath, true, true);
        }

        private void CreateExampleFile(string selectedPath, bool confirmOverwrite, bool confirmDiscard)
        {
            if (confirmOverwrite
                && File.Exists(selectedPath)
                && !EditorUtility.DisplayDialog("覆盖文件", "目标文件已经存在，是否覆盖？", "覆盖", "取消"))
            {
                return;
            }

            MultilingualTextTable example = CreateExampleTable();
            try
            {
                string directory = Path.GetDirectoryName(selectedPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(selectedPath, JsonUtility.ToJson(example, true), Utf8NoBom);
                AssetDatabase.Refresh();
                if (!confirmDiscard || ConfirmDiscard())
                {
                    table = example;
                    filePath = ToProjectPath(selectedPath);
                    selectedIndex = 0;
                    savedJson = JsonUtility.ToJson(table, true);
                    hasUnsavedChanges = false;
                }

                SetStatus("示例文件创建成功。", MessageType.Info);
            }
            catch (Exception exception)
            {
                SetStatus($"示例文件创建失败：{exception.Message}", MessageType.Error);
            }
        }

        private static MultilingualTextTable CreateExampleTable()
        {
            MultilingualTextTable example = new MultilingualTextTable { nextId = 2 };
            MultilingualTextItem item = new MultilingualTextItem { id = 1 };
            item.translations.Add(new MultilingualTextTranslation
            {
                language = SupportedLanguage.ChineseSimplified,
                content = "开始",
                audioPath = string.Empty
            });
            item.translations.Add(new MultilingualTextTranslation
            {
                language = SupportedLanguage.English,
                content = "Start",
                audioPath = string.Empty
            });
            example.items.Add(item);
            return example;
        }

        private void SelectFile()
        {
            if (!ConfirmDiscard())
            {
                return;
            }

            string selected = EditorUtility.OpenFilePanel("选择多语言文本 JSON", Application.streamingAssetsPath, "json");
            if (!string.IsNullOrEmpty(selected))
            {
                LoadFile(selected, true);
                GUIUtility.ExitGUI();
            }
        }

        private void LoadManagerFile(string path)
        {
            string fullPath = ResolvePath(path);
            bool isCurrentFile = string.Equals(fullPath, ResolvePath(filePath), StringComparison.OrdinalIgnoreCase);
            if (isCurrentFile && File.Exists(fullPath))
            {
                return;
            }

            if (!ConfirmDiscard())
            {
                return;
            }

            if (LoadFile(fullPath, false))
            {
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "配置文件不存在",
                    $"未找到当前管理器配置的文件：\n{fullPath}\n\n是否在该位置创建示例配置？",
                    "创建",
                    "取消"))
            {
                SetStatus($"文件不存在：{fullPath}", MessageType.Warning);
                return;
            }

            CreateExampleFile(fullPath, false, false);
        }

        private void ReloadFile()
        {
            if (ConfirmDiscard())
            {
                LoadFile(filePath, true);
                GUIUtility.ExitGUI();
            }
        }

        private bool LoadFile(string path, bool reportMissing)
        {
            string fullPath = ResolvePath(path);
            if (!File.Exists(fullPath))
            {
                if (reportMissing)
                {
                    SetStatus($"文件不存在：{fullPath}", MessageType.Error);
                }

                return false;
            }

            try
            {
                string json = File.ReadAllText(fullPath, Utf8NoBom).TrimStart('\uFEFF');
                MultilingualTextTable candidate = JsonUtility.FromJson<MultilingualTextTable>(json);
                if (candidate == null || candidate.items == null)
                {
                    SetStatus("JSON 缺少有效的 items 数据。", MessageType.Error);
                    return false;
                }

                table = candidate;
                filePath = ToProjectPath(fullPath);
                selectedIndex = 0;
                savedJson = JsonUtility.ToJson(table, true);
                hasUnsavedChanges = false;
                validationDirty = true;
                SetStatus("文件加载成功。", MessageType.Info);
                return true;
            }
            catch (Exception exception)
            {
                SetStatus($"文件加载失败：{exception.Message}", MessageType.Error);
                return false;
            }
        }

        private bool SaveFile()
        {
            string fullPath = ResolvePath(filePath);
            try
            {
                string directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonUtility.ToJson(table, true);
                File.WriteAllText(fullPath, json, Utf8NoBom);
                AssetDatabase.Refresh();
                savedJson = json;
                hasUnsavedChanges = false;
                SetStatus("文件保存成功。", MessageType.Info);
                return true;
            }
            catch (Exception exception)
            {
                SetStatus($"文件保存失败：{exception.Message}", MessageType.Error);
                return false;
            }
        }

        private void RevealFile()
        {
            string fullPath = ResolvePath(filePath);
            if (File.Exists(fullPath))
            {
                EditorUtility.RevealInFinder(fullPath);
            }
            else
            {
                SetStatus($"文件不存在：{fullPath}", MessageType.Error);
            }
        }

        private void AddItem()
        {
            RecordUndo("新增多语言文本记录");
            int id = AllocateNextId();
            if (id <= 0)
            {
                return;
            }

            MultilingualTextItem item = new MultilingualTextItem { id = id };
            item.translations.Add(new MultilingualTextTranslation { language = previewLanguage, content = string.Empty });
            table.items.Add(item);
            selectedIndex = table.items.Count - 1;
            MarkChanged();
        }

        private void DuplicateItem(int index)
        {
            RecordUndo("复制多语言文本记录");
            int id = AllocateNextId();
            if (id <= 0)
            {
                return;
            }

            string json = JsonUtility.ToJson(table.items[index]);
            MultilingualTextItem copy = JsonUtility.FromJson<MultilingualTextItem>(json);
            copy.id = id;
            table.items.Insert(index + 1, copy);
            selectedIndex = index + 1;
            MarkChanged();
        }

        private void DeleteItem(int index)
        {
            if (!EditorUtility.DisplayDialog("删除记录", $"确定删除“{table.items[index].id}”吗？", "删除", "取消"))
            {
                return;
            }

            RecordUndo("删除多语言文本记录");
            table.items.RemoveAt(index);
            ClampSelection();
            MarkChanged();
        }

        private void SelectNextMatch()
        {
            if (table.items.Count == 0 || string.IsNullOrEmpty(findText))
            {
                return;
            }

            for (int offset = 1; offset <= table.items.Count; offset++)
            {
                int index = (selectedIndex + offset) % table.items.Count;
                if (ItemContains(table.items[index], findText))
                {
                    selectedIndex = index;
                    viewMode = ViewMode.Detail;
                    EditorPrefs.SetInt(ViewModeKey, (int)viewMode);
                    SetStatus($"已定位到记录“{table.items[index].id}”。", MessageType.Info);
                    return;
                }
            }

            SetStatus($"没有找到“{findText}”。", MessageType.Warning);
        }

        private void ReplaceCurrent()
        {
            if (table.items.Count == 0 || string.IsNullOrEmpty(findText))
            {
                return;
            }

            ClampSelection();
            RecordUndo("替换当前多语言文本");
            int replacementCount = ReplaceInItem(table.items[selectedIndex], findText, replaceText, true);
            if (replacementCount > 0)
            {
                MarkChanged();
                SetStatus($"已在当前记录替换 {replacementCount} 处。", MessageType.Info);
            }
            else
            {
                SetStatus("当前记录没有可替换的内容。", MessageType.Warning);
            }
        }

        private void ReplaceAll()
        {
            RecordUndo("全部替换多语言文本");
            int replacementCount = 0;
            for (int i = 0; i < table.items.Count; i++)
            {
                replacementCount += ReplaceInItem(table.items[i], findText, replaceText, false);
            }

            if (replacementCount > 0)
            {
                MarkChanged();
                SetStatus($"全部替换完成，共替换 {replacementCount} 处。", MessageType.Info);
            }
            else
            {
                SetStatus($"没有找到“{findText}”。", MessageType.Warning);
            }
        }

        private bool ItemContains(MultilingualTextItem item, string value)
        {
            if (item == null || item.translations == null)
            {
                return false;
            }

            StringComparison comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            for (int translationIndex = 0; translationIndex < item.translations.Count; translationIndex++)
            {
                MultilingualTextTranslation translation = item.translations[translationIndex];
                if (translation == null || string.IsNullOrEmpty(translation.content))
                {
                    continue;
                }

                if (translation.content.IndexOf(value, comparison) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private int ReplaceInItem(MultilingualTextItem item, string oldValue, string newValue, bool firstOnly)
        {
            if (item == null || item.translations == null || string.IsNullOrEmpty(oldValue))
            {
                return 0;
            }

            int count = 0;
            StringComparison comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            for (int translationIndex = 0; translationIndex < item.translations.Count; translationIndex++)
            {
                MultilingualTextTranslation translation = item.translations[translationIndex];
                if (translation == null)
                {
                    continue;
                }

                string source = translation.content ?? string.Empty;
                int index = source.IndexOf(oldValue, comparison);
                while (index >= 0)
                {
                    source = source.Substring(0, index) + newValue + source.Substring(index + oldValue.Length);
                    count++;
                    if (firstOnly)
                    {
                        translation.content = source;
                        return count;
                    }

                    index = source.IndexOf(oldValue, index + newValue.Length, comparison);
                }

                translation.content = source;
            }

            return count;
        }

        private bool MatchesSearch(MultilingualTextItem item)
        {
            if (item == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(searchText))
            {
                return true;
            }

            string term = searchText.Trim();

            if (item.id.ToString().IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (item.translations != null)
            {
                for (int i = 0; i < item.translations.Count; i++)
                {
                    MultilingualTextTranslation translation = item.translations[i];
                    if (translation == null) continue;

                    if (!string.IsNullOrEmpty(translation.audioPath) &&
                        translation.audioPath.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }

                    if (!string.IsNullOrEmpty(translation.content)
                        && translation.content.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void EnsureValidSelectionForSearch()
        {
            if (table == null || table.items == null || table.items.Count == 0)
            {
                return;
            }

            if (selectedIndex >= 0 && selectedIndex < table.items.Count)
            {
                MultilingualTextItem current = table.items[selectedIndex];
                if (current != null && MatchesSearch(current))
                {
                    return;
                }
            }

            for (int i = 0; i < table.items.Count; i++)
            {
                if (table.items[i] != null && MatchesSearch(table.items[i]))
                {
                    selectedIndex = i;
                    return;
                }
            }
        }

        private static string GetPreview(MultilingualTextItem item, SupportedLanguage language)
        {
            if (item.translations == null)
            {
                return string.Empty;
            }

            MultilingualTextTranslation translation = item.translations.Find(value => value != null && value.language == language);
            string text = translation != null ? translation.GetText() : string.Empty;
            text = text.Replace('\n', ' ');
            return text.Length <= 28 ? text : text.Substring(0, 28) + "...";
        }

        private static MultilingualTextTranslation GetOrCreateTranslation(MultilingualTextItem item, SupportedLanguage language)
        {
            if (item.translations == null)
            {
                item.translations = new List<MultilingualTextTranslation>();
            }

            MultilingualTextTranslation translation = item.translations.Find(value => value != null && value.language == language);
            if (translation == null)
            {
                translation = new MultilingualTextTranslation { language = language };
                item.translations.Add(translation);
            }

            return translation;
        }

        private static MultilingualTextTranslation FindTranslation(MultilingualTextItem item, SupportedLanguage language)
        {
            return item.translations == null
                ? null
                : item.translations.Find(value => value != null && value.language == language);
        }

        private static bool TryFindMissingLanguage(MultilingualTextItem item, out SupportedLanguage missingLanguage)
        {
            Array languages = Enum.GetValues(typeof(SupportedLanguage));
            for (int i = 0; i < languages.Length; i++)
            {
                SupportedLanguage language = (SupportedLanguage)languages.GetValue(i);
                if (item.translations == null || !item.translations.Exists(value => value != null && value.language == language))
                {
                    missingLanguage = language;
                    return true;
                }
            }

            missingLanguage = SupportedLanguage.ChineseSimplified;
            return false;
        }

        private int AllocateNextId()
        {
            EnsureNextId();
            if (table.nextId <= 0 || table.nextId == int.MaxValue)
            {
                EditorUtility.DisplayDialog("无法新增记录", "多语言文本 ID 已达到 int 最大值。", "确定");
                return 0;
            }

            int id = table.nextId;
            table.nextId++;
            return id;
        }

        private void EnsureNextId()
        {
            int maxId = 0;
            for (int i = 0; i < table.items.Count; i++)
            {
                MultilingualTextItem item = table.items[i];
                if (item != null && item.id > maxId)
                {
                    maxId = item.id;
                }
            }

            if (table.nextId <= maxId)
            {
                table.nextId = maxId == int.MaxValue ? int.MaxValue : maxId + 1;
            }
        }

        private void EnsureData()
        {
            if (table == null)
            {
                table = new MultilingualTextTable();
            }

            if (table.items == null)
            {
                table.items = new List<MultilingualTextItem>();
            }

            EnsureNextId();
        }

        private void ClampSelection()
        {
            selectedIndex = table.items.Count == 0 ? 0 : Mathf.Clamp(selectedIndex, 0, table.items.Count - 1);
        }

        private bool ConfirmDiscard()
        {
            return !hasUnsavedChanges
                || EditorUtility.DisplayDialog("放弃未保存更改", "当前编辑内容尚未保存，是否继续？", "继续", "取消");
        }

        private void MarkChanged()
        {
            hasUnsavedChanges = true;
            validationDirty = true;
            Repaint();
        }

        private void UpdateUnsavedState()
        {
            hasUnsavedChanges = !string.Equals(
                JsonUtility.ToJson(table, true),
                savedJson,
                StringComparison.Ordinal);
        }

        private void RecordUndo(string operationName)
        {
            Undo.RegisterCompleteObjectUndo(this, operationName);
        }

        private void SetStatus(string message, MessageType type)
        {
            statusMessage = message;
            statusType = type;
            Repaint();
        }

        private string BuildValidationMessage(out MessageType messageType)
        {
            if (!validationDirty)
            {
                messageType = cachedValidationType;
                return cachedValidationMessage;
            }

            int errorCount = 0;
            int warningCount = 0;
            HashSet<int> ids = new HashSet<int>();

            for (int i = 0; i < table.items.Count; i++)
            {
                MultilingualTextItem item = table.items[i];
                if (item == null || item.id <= 0)
                {
                    errorCount++;
                    continue;
                }

                if (!ids.Add(item.id))
                {
                    errorCount++;
                }

                if (item.translations == null || item.translations.Count == 0)
                {
                    warningCount++;
                    continue;
                }

                HashSet<SupportedLanguage> languages = new HashSet<SupportedLanguage>();
                for (int translationIndex = 0; translationIndex < item.translations.Count; translationIndex++)
                {
                    MultilingualTextTranslation translation = item.translations[translationIndex];
                    if (translation == null || !Enum.IsDefined(typeof(SupportedLanguage), translation.language))
                    {
                        errorCount++;
                        continue;
                    }

                    if (!languages.Add(translation.language))
                    {
                        errorCount++;
                    }

                    if (string.IsNullOrEmpty(translation.content))
                    {
                        warningCount++;
                    }
                }
            }

            if (errorCount > 0)
            {
                cachedValidationType = MessageType.Error;
                cachedValidationMessage = $"数据校验：{errorCount} 个错误，{warningCount} 个警告。请检查空 ID、重复 ID 或重复语言。";
            }
            else if (warningCount > 0)
            {
                cachedValidationType = MessageType.Warning;
                cachedValidationMessage = $"数据校验：无错误，{warningCount} 个警告。部分记录没有翻译内容。";
            }
            else
            {
                cachedValidationType = MessageType.Info;
                cachedValidationMessage = $"数据校验通过：{table.items.Count} 条记录。";
            }

            validationDirty = false;
            messageType = cachedValidationType;
            return cachedValidationMessage;
        }

        private AudioClip GetCachedAudioClip(string path)
        {
            if (audioClipCache.TryGetValue(path, out AudioClip clip))
            {
                return clip;
            }

            clip = Resources.Load<AudioClip>(path);
            if (clip == null)
            {
                string assetPath = "Assets/Resources/" + path;
                clip = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath + ".mp3") ??
                       AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath + ".wav") ??
                       AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath + ".ogg");
            }

            audioClipCache[path] = clip;
            return clip;
        }

        private void ClearAudioClipCache()
        {
            audioClipCache.Clear();
            Repaint();
        }

        internal MultilingualTextTable GetTranslationTable()
        {
            EnsureData();
            return table;
        }

        internal void BeginGeneratedTranslationUndo()
        {
            RecordUndo("百度翻译生成多语言文本");
        }

        internal bool ApplyGeneratedTranslation(
            MultilingualTextItem item,
            SupportedLanguage language,
            string generatedContent)
        {
            if (item == null || generatedContent == null || !table.items.Contains(item))
            {
                return false;
            }

            MultilingualTextTranslation translation = FindTranslation(item, language);
            if (translation == null)
            {
                return false;
            }

            translation.content = generatedContent;
            MarkChanged();
            return true;
        }

        private static string ResolvePath(string path)
        {
            return Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), path));
        }

        private static string ToProjectPath(string fullPath)
        {
            string projectPath = Path.GetFullPath(Directory.GetCurrentDirectory()).Replace('\\', '/').TrimEnd('/');
            string normalized = Path.GetFullPath(fullPath).Replace('\\', '/');
            return normalized.StartsWith(projectPath + "/", StringComparison.OrdinalIgnoreCase)
                ? normalized.Substring(projectPath.Length + 1)
                : normalized;
        }

        public override void SaveChanges()
        {
            if (SaveFile())
            {
                base.SaveChanges();
            }
        }

        public override void DiscardChanges()
        {
            LoadFile(filePath, false);
            hasUnsavedChanges = false;
            base.DiscardChanges();
        }
        #endregion
    }

    /// <summary>
    /// Color scope helper for GUI color tinting.
    /// </summary>
    public struct GUIColorScope : IDisposable
    {
        private readonly Color previousColor;

        public GUIColorScope(Color newColor)
        {
            previousColor = GUI.color;
            GUI.color = newColor;
        }

        public void Dispose()
        {
            GUI.color = previousColor;
        }
    }
}
