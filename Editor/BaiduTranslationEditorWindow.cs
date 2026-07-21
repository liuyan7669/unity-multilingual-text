using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Cowart.MultilingualText.Editor
{
    /// <summary>
    /// 使用百度通用文本翻译 API 为已经添加的语言生成文本。
    /// </summary>
    public class BaiduTranslationEditorWindow : EditorWindow
    {
        [Serializable]
        private sealed class BaiduTranslationResponse
        {
            public BaiduTranslationResult[] trans_result;
            public string error_code;
            public string error_msg;
        }

        [Serializable]
        private sealed class BaiduTranslationResult
        {
            public string dst;
        }

        private sealed class TranslationJob
        {
            public MultilingualTextItem item;
            public SupportedLanguage targetLanguage;
            public string sourceContent;
        }

        private const string Endpoint = "https://fanyi-api.baidu.com/api/trans/vip/translate";
        private const string AppIdKey = "Cowart.MultilingualText.Baidu.AppId";
        private const string SecretKey = "Cowart.MultilingualText.Baidu.Secret";
        private const string RememberSecretKey = "Cowart.MultilingualText.Baidu.RememberSecret";
        private const string SourceLanguageKey = "Cowart.MultilingualText.Baidu.SourceLanguage";
        private const double RequestIntervalSeconds = 1.05d;
        private const int MaxQueryUtf8Bytes = 6000;

        [SerializeField] private MultilingualTextEditorWindow owner;
        [SerializeField] private string appId = string.Empty;
        [NonSerialized] private string secret = string.Empty;
        [SerializeField] private bool rememberSecret;
        [SerializeField] private SupportedLanguage sourceLanguage = SupportedLanguage.ChineseSimplified;

        private readonly List<TranslationJob> jobs = new List<TranslationJob>();
        private UnityWebRequest activeRequest;
        private int currentJobIndex;
        private int completedRequestCount;
        private int totalRequestCount;
        private int totalCharacterCount;
        private double nextRequestTime;
        private string statusMessage = string.Empty;
        private MessageType statusType = MessageType.Info;
        private bool isBackgroundRunner;
        private string progressTitle = "多语言文本翻译";

        private bool IsRunning => jobs.Count > 0;

        public static void Open(MultilingualTextEditorWindow ownerWindow)
        {
            BaiduTranslationEditorWindow window = GetOrCreateWindow(ownerWindow);
            window.Show();
            window.Focus();
        }

        public static void TranslateItem(MultilingualTextEditorWindow ownerWindow, MultilingualTextItem item)
        {
            BaiduTranslationEditorWindow runner = CreateBackgroundRunner(ownerWindow);
            runner.progressTitle = "翻译记录";
            runner.StartTranslation(runner.BuildItemJobs(item));
        }

        public static void TranslateTable(MultilingualTextEditorWindow ownerWindow)
        {
            BaiduTranslationEditorWindow runner = CreateBackgroundRunner(ownerWindow);
            runner.progressTitle = "翻译整表";
            runner.StartTranslation(runner.BuildTableJobs());
        }

        public static void TranslateLanguage(
            MultilingualTextEditorWindow ownerWindow,
            MultilingualTextItem item,
            SupportedLanguage targetLanguage)
        {
            BaiduTranslationEditorWindow runner = CreateBackgroundRunner(ownerWindow);
            runner.progressTitle = "翻译单个语言";
            runner.StartTranslation(runner.BuildLanguageJob(item, targetLanguage));
        }

        public static bool IsSourceLanguage(SupportedLanguage language)
        {
            BaiduTranslationEditorWindow[] windows = Resources.FindObjectsOfTypeAll<BaiduTranslationEditorWindow>();
            for (int i = 0; i < windows.Length; i++)
            {
                if (!windows[i].isBackgroundRunner)
                {
                    return windows[i].sourceLanguage == language;
                }
            }

            SupportedLanguage configuredSource = (SupportedLanguage)Mathf.Clamp(
                EditorPrefs.GetInt(SourceLanguageKey, (int)SupportedLanguage.ChineseSimplified),
                0,
                Enum.GetValues(typeof(SupportedLanguage)).Length - 1);
            return configuredSource == language;
        }

        private static BaiduTranslationEditorWindow GetOrCreateWindow(MultilingualTextEditorWindow ownerWindow)
        {
            BaiduTranslationEditorWindow window = GetWindow<BaiduTranslationEditorWindow>();
            window.owner = ownerWindow;
            window.titleContent = new GUIContent("百度翻译配置");
            window.minSize = new Vector2(520f, 280f);
            return window;
        }

        private static BaiduTranslationEditorWindow CreateBackgroundRunner(MultilingualTextEditorWindow ownerWindow)
        {
            BaiduTranslationEditorWindow runner = CreateInstance<BaiduTranslationEditorWindow>();
            runner.isBackgroundRunner = true;
            runner.owner = ownerWindow;

            BaiduTranslationEditorWindow[] windows = Resources.FindObjectsOfTypeAll<BaiduTranslationEditorWindow>();
            for (int i = 0; i < windows.Length; i++)
            {
                BaiduTranslationEditorWindow configWindow = windows[i];
                if (configWindow == runner || configWindow.isBackgroundRunner)
                {
                    continue;
                }

                runner.appId = configWindow.appId;
                runner.secret = configWindow.secret;
                runner.rememberSecret = configWindow.rememberSecret;
                runner.sourceLanguage = configWindow.sourceLanguage;
                break;
            }

            return runner;
        }

        private void OnEnable()
        {
            appId = EditorPrefs.GetString(AppIdKey, appId);
            rememberSecret = EditorPrefs.GetBool(RememberSecretKey, false);
            secret = rememberSecret ? EditorPrefs.GetString(SecretKey, secret) : string.Empty;
            sourceLanguage = (SupportedLanguage)Mathf.Clamp(
                EditorPrefs.GetInt(SourceLanguageKey, (int)SupportedLanguage.ChineseSimplified),
                0,
                Enum.GetValues(typeof(SupportedLanguage)).Length - 1);
            EditorApplication.update += UpdateTranslation;
        }

        private void OnDisable()
        {
            EditorApplication.update -= UpdateTranslation;
            CancelTranslation();
        }

        private void OnGUI()
        {
            DrawConnectionSettings();
        }

        private void DrawConnectionSettings()
        {
            EditorGUILayout.LabelField("百度翻译配置", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(IsRunning))
            {
                appId = EditorGUILayout.TextField("APP ID", appId);
                secret = EditorGUILayout.PasswordField("密钥", secret);
                rememberSecret = EditorGUILayout.ToggleLeft("在本机 EditorPrefs 中记住密钥", rememberSecret);
                sourceLanguage = (SupportedLanguage)EditorGUILayout.EnumPopup("原始语言", sourceLanguage);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("保存本机配置", GUILayout.Width(110f)))
                    {
                        SaveSettings();
                    }

                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("百度翻译控制台", GUILayout.Width(120f)))
                    {
                        Application.OpenURL("https://fanyi-api.baidu.com/manage/developer");
                    }
                }
            }

            EditorGUILayout.HelpBox(
                "配置保存在本机 Unity EditorPrefs 中，不会写入项目文件。未勾选记住密钥时，密钥不会保存。",
                MessageType.Info);
        }

        private void StartTranslation(List<TranslationJob> requestedJobs)
        {
            if (IsRunning)
            {
                RejectTranslation("已有翻译任务正在执行。", MessageType.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(secret))
            {
                RejectTranslation("请先在“翻译配置”中填写百度翻译 APP ID 和密钥。", MessageType.Error);
                return;
            }

            int requestCount = 0;
            int characterCount = 0;
            for (int i = 0; i < requestedJobs.Count; i++)
            {
                string content = requestedJobs[i].sourceContent ?? string.Empty;
                if (content.Length == 0)
                {
                    continue;
                }

                if (Encoding.UTF8.GetByteCount(content) > MaxQueryUtf8Bytes)
                {
                    RejectTranslation($"存在超过 {MaxQueryUtf8Bytes} UTF-8 字节的文本内容，请缩短后再生成。", MessageType.Error);
                    return;
                }

                requestCount++;
                characterCount += content.Length;
            }

            if (requestCount == 0)
            {
                RejectTranslation("没有可翻译的非空文本，或没有已添加的目标语言。", MessageType.Warning);
                return;
            }

            string confirmation =
                $"即将覆盖 {requestedJobs.Count} 条目标翻译，调用接口 {requestCount} 次，预计计费字符 {characterCount} 个。\n\n是否继续？";
            if (!EditorUtility.DisplayDialog("确认百度翻译生成", confirmation, "开始生成", "取消"))
            {
                ReleaseBackgroundRunner();
                return;
            }

            SaveSettings();
            owner.BeginGeneratedTranslationUndo();
            jobs.Clear();
            jobs.AddRange(requestedJobs);
            currentJobIndex = 0;
            completedRequestCount = 0;
            totalRequestCount = requestCount;
            totalCharacterCount = characterCount;
            nextRequestTime = 0d;
            SetStatus("正在生成，请勿关闭主窗口。", MessageType.Info);
        }

        private void UpdateTranslation()
        {
            if (!IsRunning)
            {
                return;
            }

            if (DisplayProgressAndCheckCancellation())
            {
                CancelTranslation();
                Debug.LogWarning("[MultilingualText] 用户取消了翻译任务；已完成的目标语言结果会保留。");
                ReleaseBackgroundRunner();
                return;
            }

            if (owner == null)
            {
                FailTranslation("主多语言文本窗口已关闭，生成已停止。");
                return;
            }

            if (activeRequest != null)
            {
                if (!activeRequest.isDone)
                {
                    return;
                }

                CompleteRequest();
                return;
            }

            if (EditorApplication.timeSinceStartup < nextRequestTime)
            {
                return;
            }

            if (currentJobIndex >= jobs.Count)
            {
                FinishTranslation();
                return;
            }

            TranslationJob job = jobs[currentJobIndex];
            activeRequest = CreateRequest(job.sourceContent, sourceLanguage, job.targetLanguage);
            activeRequest.SendWebRequest();
        }

        private void CompleteRequest()
        {
            UnityWebRequest request = activeRequest;
            activeRequest = null;
            try
            {
                if (request.result != UnityWebRequest.Result.Success)
                {
                    FailTranslation($"网络请求失败：{request.error}");
                    return;
                }

                BaiduTranslationResponse response = JsonUtility.FromJson<BaiduTranslationResponse>(request.downloadHandler.text);
                if (response == null)
                {
                    FailTranslation("百度翻译返回了无法解析的数据。");
                    return;
                }

                if (!string.IsNullOrEmpty(response.error_code))
                {
                    FailTranslation($"百度翻译错误 {response.error_code}：{response.error_msg}");
                    return;
                }

                if (response.trans_result == null || response.trans_result.Length == 0)
                {
                    FailTranslation("百度翻译没有返回译文。");
                    return;
                }

                StringBuilder translatedText = new StringBuilder();
                for (int i = 0; i < response.trans_result.Length; i++)
                {
                    if (i > 0)
                    {
                        translatedText.Append('\n');
                    }

                    translatedText.Append(response.trans_result[i]?.dst ?? string.Empty);
                }

                TranslationJob job = jobs[currentJobIndex];
                if (!owner.ApplyGeneratedTranslation(job.item, job.targetLanguage, translatedText.ToString()))
                {
                    FailTranslation("目标记录或目标语言已被删除，生成已停止。");
                    return;
                }

                currentJobIndex++;
                completedRequestCount++;
                nextRequestTime = EditorApplication.timeSinceStartup + RequestIntervalSeconds;
                if (currentJobIndex >= jobs.Count)
                {
                    FinishTranslation();
                }
                Repaint();
            }
            catch (Exception exception)
            {
                FailTranslation($"处理百度翻译结果失败：{exception.Message}");
            }
            finally
            {
                request.Dispose();
            }
        }

        private void FinishTranslation()
        {
            int generatedCount = jobs.Count;
            jobs.Clear();
            string message = $"生成完成：更新 {generatedCount} 条翻译，调用 {completedRequestCount} 次，共 {totalCharacterCount} 个源字符。请返回主窗口检查并保存。";
            EditorUtility.ClearProgressBar();
            SetStatus(message, MessageType.Info);
            Debug.Log($"[MultilingualText] {message}");
            ReleaseBackgroundRunner();
        }

        private UnityWebRequest CreateRequest(string query, SupportedLanguage from, SupportedLanguage to)
        {
            string salt = DateTime.UtcNow.Ticks.ToString();
            string sign = CalculateMd5(appId + query + salt + secret);
            WWWForm form = new WWWForm();
            form.AddField("q", query);
            form.AddField("from", GetBaiduLanguageCode(from));
            form.AddField("to", GetBaiduLanguageCode(to));
            form.AddField("appid", appId);
            form.AddField("salt", salt);
            form.AddField("sign", sign);
            return UnityWebRequest.Post(Endpoint, form);
        }

        private List<TranslationJob> BuildItemJobs(MultilingualTextItem item)
        {
            List<TranslationJob> result = new List<TranslationJob>();
            MultilingualTextTranslation source = FindTranslation(item, sourceLanguage);
            if (!HasSourceContent(source) || item?.translations == null)
            {
                return result;
            }

            for (int i = 0; i < item.translations.Count; i++)
            {
                MultilingualTextTranslation target = item.translations[i];
                if (target != null && target.language != sourceLanguage)
                {
                    result.Add(CreateJob(item, target.language, source));
                }
            }

            return result;
        }

        private List<TranslationJob> BuildLanguageJob(
            MultilingualTextItem item,
            SupportedLanguage targetLanguage)
        {
            List<TranslationJob> result = new List<TranslationJob>();
            if (targetLanguage == sourceLanguage || FindTranslation(item, targetLanguage) == null)
            {
                return result;
            }

            MultilingualTextTranslation source = FindTranslation(item, sourceLanguage);
            if (HasSourceContent(source))
            {
                result.Add(CreateJob(item, targetLanguage, source));
            }

            return result;
        }

        private List<TranslationJob> BuildTableJobs()
        {
            List<TranslationJob> result = new List<TranslationJob>();
            if (owner == null)
            {
                return result;
            }

            MultilingualTextTable table = owner.GetTranslationTable();
            for (int i = 0; i < table.items.Count; i++)
            {
                result.AddRange(BuildItemJobs(table.items[i]));
            }

            return result;
        }

        private static TranslationJob CreateJob(
            MultilingualTextItem item,
            SupportedLanguage targetLanguage,
            MultilingualTextTranslation source)
        {
            return new TranslationJob
            {
                item = item,
                targetLanguage = targetLanguage,
                sourceContent = source.content
            };
        }

        private static MultilingualTextTranslation FindTranslation(
            MultilingualTextItem item,
            SupportedLanguage language)
        {
            return item?.translations?.Find(value => value != null && value.language == language);
        }

        private static bool HasSourceContent(MultilingualTextTranslation translation)
        {
            return !string.IsNullOrEmpty(translation?.content);
        }

        private void SaveSettings()
        {
            appId = appId.Trim();
            EditorPrefs.SetString(AppIdKey, appId.Trim());
            EditorPrefs.SetBool(RememberSecretKey, rememberSecret);
            EditorPrefs.SetInt(SourceLanguageKey, (int)sourceLanguage);
            if (rememberSecret)
            {
                EditorPrefs.SetString(SecretKey, secret);
            }
            else
            {
                EditorPrefs.DeleteKey(SecretKey);
            }

            SetStatus("本机配置已保存。", MessageType.Info);
            if (!isBackgroundRunner)
            {
                ShowNotification(new GUIContent("本机配置已保存"));
            }
        }

        private void CancelTranslation()
        {
            if (activeRequest != null)
            {
                activeRequest.Abort();
                activeRequest.Dispose();
                activeRequest = null;
            }

            jobs.Clear();
            if (isBackgroundRunner)
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private bool DisplayProgressAndCheckCancellation()
        {
            if (!isBackgroundRunner)
            {
                return false;
            }

            float progress = totalRequestCount == 0
                ? 0f
                : Mathf.Clamp01((float)completedRequestCount / totalRequestCount);
            int displayRequest = Mathf.Min(completedRequestCount + 1, totalRequestCount);
            string info = $"正在翻译 {displayRequest}/{totalRequestCount}，已完成 {currentJobIndex}/{jobs.Count} 条目标翻译";
            return EditorUtility.DisplayCancelableProgressBar(progressTitle, info, progress);
        }

        private void FailTranslation(string message)
        {
            CancelTranslation();
            SetStatus(message, MessageType.Error);
            Debug.LogError($"[MultilingualText] {message}");
            ReleaseBackgroundRunner();
        }

        private void RejectTranslation(string message, MessageType type)
        {
            SetStatus(message, type);
            if (type == MessageType.Error)
            {
                Debug.LogError($"[MultilingualText] {message}");
            }
            else
            {
                Debug.LogWarning($"[MultilingualText] {message}");
            }

            ReleaseBackgroundRunner();
        }

        private void ReleaseBackgroundRunner()
        {
            if (!isBackgroundRunner)
            {
                return;
            }

            EditorUtility.ClearProgressBar();

            EditorApplication.delayCall += () =>
            {
                if (this != null)
                {
                    DestroyImmediate(this);
                }
            };
        }

        private void SetStatus(string message, MessageType type)
        {
            statusMessage = message;
            statusType = type;
            Repaint();
        }

        private static string CalculateMd5(string value)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(value));
                StringBuilder builder = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    builder.Append(hash[i].ToString("x2"));
                }

                return builder.ToString();
            }
        }

        private static string GetBaiduLanguageCode(SupportedLanguage language)
        {
            switch (language)
            {
                case SupportedLanguage.ChineseSimplified: return "zh";
                case SupportedLanguage.English: return "en";
                case SupportedLanguage.Japanese: return "jp";
                case SupportedLanguage.Korean: return "kor";
                case SupportedLanguage.French: return "fra";
                case SupportedLanguage.Spanish: return "spa";
                case SupportedLanguage.Russian: return "ru";
                case SupportedLanguage.German: return "de";
                case SupportedLanguage.Italian: return "it";
                case SupportedLanguage.Portuguese: return "pt";
                default: throw new ArgumentOutOfRangeException(nameof(language), language, null);
            }
        }

    }
}
