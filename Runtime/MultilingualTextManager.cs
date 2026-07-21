using System;
using UnityEngine;

namespace Cowart.MultilingualText
{
    public enum MultilingualTextLoadState
    {
        NotLoaded,
        Loading,
        Loaded,
        Failed
    }

    /// <summary>
    /// 加载多语言文本、管理当前语言并向外提供统一查询接口。
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class MultilingualTextManager : MonoBehaviour
    {
        private const string DefaultFileName = "multilingual_text.json";

        private static MultilingualTextManager instance;
        private static MultilingualTextObserverRegistry pendingObservers = new MultilingualTextObserverRegistry();
        private static SupportedLanguage pendingLanguage = SupportedLanguage.ChineseSimplified;
        private static bool hasPendingLanguage;

        [SerializeField, Header("多语言文本文件名")] private string fileName = DefaultFileName;
        [SerializeField, Header("当前语言")] private SupportedLanguage currentLanguage = SupportedLanguage.ChineseSimplified;
        [SerializeField, Header("缺少翻译时的回退语言")] private SupportedLanguage fallbackLanguage = SupportedLanguage.ChineseSimplified;
        [SerializeField, Header("记住用户选择的语言")] private bool rememberLanguage;
        [SerializeField] private string languagePreferenceKey = "MultilingualText.CurrentLanguage";
        [SerializeField] private MultilingualTextLoadState loadState = MultilingualTextLoadState.NotLoaded;
        [SerializeField] private string lastLoadError;

        private readonly MultilingualTextConfig config = new MultilingualTextConfig();
        private MultilingualTextObserverRegistry observerRegistry;
        private SupportedLanguage lastValidatedLanguage;

        public static Action LoadCompleted;
        public static Action LanguageChanged;
        public static Action<string> LoadFailed;

        public static SupportedLanguage CurrentLanguage
        {
            get { return instance != null ? instance.currentLanguage : pendingLanguage; }
            set
            {
                if (instance == null)
                {
                    pendingLanguage = value;
                    hasPendingLanguage = true;
                    return;
                }

                if (instance.currentLanguage == value)
                {
                    return;
                }

                instance.currentLanguage = value;
                instance.lastValidatedLanguage = value;
                instance.SaveLanguagePreference();
                instance.NotifyLanguageChanged();
            }
        }

        public static SupportedLanguage FallbackLanguage
        {
            get { return instance != null ? instance.fallbackLanguage : SupportedLanguage.ChineseSimplified; }
        }

        public static bool IsLoaded => instance != null && instance.loadState == MultilingualTextLoadState.Loaded;
        public static bool IsLoading => instance != null && instance.config.IsLoading;
        public static MultilingualTextLoadState LoadState => instance != null ? instance.loadState : MultilingualTextLoadState.NotLoaded;
        public static string LastLoadError => instance != null ? instance.lastLoadError : string.Empty;

        private void Reset()
        {
            gameObject.name = "[多语言文本系统]";
            transform.position = Vector3.zero;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            instance = null;
            pendingObservers = new MultilingualTextObserverRegistry();
            pendingLanguage = SupportedLanguage.ChineseSimplified;
            hasPendingLanguage = false;
            LoadCompleted = null;
            LanguageChanged = null;
            LoadFailed = null;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                gameObject.SetActive(false);
                Destroy(gameObject);
                return;
            }

            instance = this;
            observerRegistry = pendingObservers;
            pendingObservers = new MultilingualTextObserverRegistry();

            if (hasPendingLanguage)
            {
                currentLanguage = pendingLanguage;
                hasPendingLanguage = false;
            }
            else
            {
                LoadLanguagePreference();
            }

            lastValidatedLanguage = currentLanguage;
            DontDestroyOnLoad(gameObject);
            BeginLoad();
        }

        private void OnValidate()
        {
            if (!Application.isPlaying || instance != this || lastValidatedLanguage == currentLanguage)
            {
                return;
            }

            lastValidatedLanguage = currentLanguage;
            SaveLanguagePreference();
            NotifyLanguageChanged();
        }

        private void OnDestroy()
        {
            if (instance != this)
            {
                return;
            }

            config.StopLoading();
            pendingObservers = observerRegistry ?? new MultilingualTextObserverRegistry();
            pendingLanguage = currentLanguage;
            hasPendingLanguage = true;
            instance = null;
        }

        /// <summary>
        /// 重新加载当前配置文件。
        /// </summary>
        public static void Reload()
        {
            if (instance == null)
            {
                Debug.LogWarning("多语言文本系统尚未初始化，无法重新加载配置文件。");
                return;
            }

            instance.BeginLoad();
        }

        /// <summary>
        /// 停止当前加载；停止后会进入 Failed 状态并触发 LoadFailed。
        /// </summary>
        public static void StopLoading()
        {
            if (instance != null)
            {
                instance.config.StopLoading();
            }
        }

        public static string GetText(int id, string defaultValue = "")
        {
            string text;
            return TryGetText(id, out text) ? text : defaultValue;
        }

        public static string GetAudioPath(int id, string defaultValue = "")
        {
            string audioPath;
            return TryGetAudioPath(id, out audioPath) ? audioPath : defaultValue;
        }

        public static bool TryGetText(int id, out string text)
        {
            text = string.Empty;
            return instance != null
                && instance.config.TryGetText(id, instance.currentLanguage, instance.fallbackLanguage, out text);
        }

        public static bool TryGetAudioPath(int id, out string audioPath)
        {
            audioPath = string.Empty;
            return instance != null
                && instance.config.TryGetAudioPath(id, instance.currentLanguage, instance.fallbackLanguage, out audioPath);
        }

        public static void RegisterObserver(IMultilingualTextObserver observer)
        {
            if (instance == null)
            {
                pendingObservers.Register(observer);
                return;
            }

            instance.observerRegistry.Register(observer);
        }

        public static void UnregisterObserver(IMultilingualTextObserver observer)
        {
            pendingObservers.Unregister(observer);
            if (instance != null)
            {
                instance.observerRegistry.Unregister(observer);
            }
        }

        private void BeginLoad()
        {
            loadState = MultilingualTextLoadState.Loading;
            lastLoadError = string.Empty;

            string effectiveFileName = string.IsNullOrWhiteSpace(fileName) ? DefaultFileName : fileName;
            string filePath = Application.streamingAssetsPath.TrimEnd('/', '\\') + "/" + effectiveFileName;
            config.ReadJsonFileAsync(filePath, success =>
            {
                if (success)
                {
                    loadState = MultilingualTextLoadState.Loaded;
                    lastLoadError = string.Empty;
                    observerRegistry.Notify(currentLanguage);
                    InvokeSafely(LoadCompleted);
                    return;
                }

                loadState = MultilingualTextLoadState.Failed;
                lastLoadError = config.LastError;
                InvokeSafely(LoadFailed, lastLoadError);
            });
        }

        private void NotifyLanguageChanged()
        {
            InvokeSafely(LanguageChanged);
            observerRegistry.Notify(currentLanguage);
        }

        private void LoadLanguagePreference()
        {
            if (!rememberLanguage || string.IsNullOrWhiteSpace(languagePreferenceKey) || !PlayerPrefs.HasKey(languagePreferenceKey))
            {
                return;
            }

            int savedLanguage = PlayerPrefs.GetInt(languagePreferenceKey);
            if (Enum.IsDefined(typeof(SupportedLanguage), savedLanguage))
            {
                currentLanguage = (SupportedLanguage)savedLanguage;
            }
        }

        private void SaveLanguagePreference()
        {
            if (rememberLanguage && !string.IsNullOrWhiteSpace(languagePreferenceKey))
            {
                PlayerPrefs.SetInt(languagePreferenceKey, (int)currentLanguage);
            }
        }

        private static void InvokeSafely(Action action)
        {
            if (action == null)
            {
                return;
            }

            Delegate[] callbacks = action.GetInvocationList();
            for (int i = 0; i < callbacks.Length; i++)
            {
                try
                {
                    ((Action)callbacks[i]).Invoke();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        private static void InvokeSafely(Action<string> action, string value)
        {
            if (action == null)
            {
                return;
            }

            Delegate[] callbacks = action.GetInvocationList();
            for (int i = 0; i < callbacks.Length; i++)
            {
                try
                {
                    ((Action<string>)callbacks[i]).Invoke(value);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }
    }
}
