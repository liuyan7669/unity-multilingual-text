using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace Cowart.MultilingualText
{
    /// <summary>
    /// 多语言文本配置，负责 JSON 解析、异步加载和按 ID 查询。
    /// </summary>
    [Serializable]
    public class MultilingualTextConfig
    {
        private sealed class LoadRequestState
        {
            public UnityWebRequest request;
            public bool isCancelled;
            public bool invokeCallbackWhenCancelled;
        }

        [SerializeField]
        private List<MultilingualTextItem> items = new List<MultilingualTextItem>();

        private readonly Dictionary<int, Dictionary<SupportedLanguage, MultilingualTextTranslation>> lookup =
            new Dictionary<int, Dictionary<SupportedLanguage, MultilingualTextTranslation>>();

        private LoadRequestState activeLoadRequest;

        public IReadOnlyList<MultilingualTextItem> Items
        {
            get
            {
                EnsureItems();
                return items;
            }
        }

        public int Count => items == null ? 0 : items.Count;
        public bool IsLoading => activeLoadRequest != null;
        public string LastError { get; private set; } = string.Empty;

        /// <summary>
        /// 解析完整 JSON 字符串并重建查询索引。
        /// </summary>
        public bool ReadJson(string content)
        {
            LastError = string.Empty;
            if (string.IsNullOrWhiteSpace(content))
            {
                LastError = "多语言文本 JSON 内容为空。";
                return false;
            }

            try
            {
                MultilingualTextTable table = JsonUtility.FromJson<MultilingualTextTable>(content.TrimStart('\uFEFF'));
                items = table?.items ?? new List<MultilingualTextItem>();
                RebuildLookup();

                if (lookup.Count == 0)
                {
                    LastError = "多语言文本 JSON 中没有可用记录。";
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                items = new List<MultilingualTextItem>();
                lookup.Clear();
                LastError = $"多语言文本 JSON 解析失败：{exception.Message}";
                Debug.LogError(LastError);
                return false;
            }
        }

        /// <summary>
        /// 从本地路径或 URL 异步加载多语言文本 JSON。
        /// </summary>
        public void ReadJsonFileAsync(string filePath, Action<bool> onCompleted)
        {
            LastError = string.Empty;
            if (string.IsNullOrWhiteSpace(filePath))
            {
                LastError = "多语言文本 JSON 文件路径不能为空。";
                Debug.LogError(LastError);
                onCompleted?.Invoke(false);
                return;
            }

            CancelActiveLoad(false);

            string requestPath;
            try
            {
                requestPath = filePath.Contains("://") || filePath.StartsWith("jar:", StringComparison.OrdinalIgnoreCase)
                    ? filePath
                    : new Uri(filePath).AbsoluteUri;
            }
            catch (Exception exception)
            {
                LastError = $"多语言文本文件路径无效：{exception.Message}";
                Debug.LogError(LastError);
                onCompleted?.Invoke(false);
                return;
            }

            UnityWebRequest request = UnityWebRequest.Get(requestPath);
            LoadRequestState requestState = new LoadRequestState { request = request };
            activeLoadRequest = requestState;

            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            operation.completed += _ =>
            {
                try
                {
                    if (requestState.isCancelled)
                    {
                        if (requestState.invokeCallbackWhenCancelled)
                        {
                            onCompleted?.Invoke(false);
                        }

                        return;
                    }

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        LastError = $"多语言文本文件加载失败：{request.error}\n{filePath}";
                        Debug.LogError(LastError);
                        onCompleted?.Invoke(false);
                        return;
                    }

                    onCompleted?.Invoke(ReadJson(request.downloadHandler.text));
                }
                finally
                {
                    if (ReferenceEquals(activeLoadRequest, requestState))
                    {
                        activeLoadRequest = null;
                    }

                    request.Dispose();
                }
            };
        }

        public void ReloadJsonFileAsync(string filePath, Action<bool> onCompleted)
        {
            ReadJsonFileAsync(filePath, onCompleted);
        }

        public void StopLoading()
        {
            CancelActiveLoad(true);
        }

        public bool TryGetTranslation(
            int id,
            SupportedLanguage language,
            SupportedLanguage fallbackLanguage,
            out MultilingualTextTranslation translation)
        {
            translation = null;
            if (id <= 0)
            {
                return false;
            }

            Dictionary<SupportedLanguage, MultilingualTextTranslation> translations;
            if (!lookup.TryGetValue(id, out translations))
            {
                return false;
            }

            if (translations.TryGetValue(language, out translation))
            {
                return true;
            }

            return language != fallbackLanguage && translations.TryGetValue(fallbackLanguage, out translation);
        }

        public bool TryGetText(
            int id,
            SupportedLanguage language,
            SupportedLanguage fallbackLanguage,
            out string text)
        {
            MultilingualTextTranslation translation;
            bool found = TryGetTranslation(id, language, fallbackLanguage, out translation);
            text = found ? translation.GetText() : string.Empty;
            return found;
        }

        public bool TryGetAudioPath(
            int id,
            SupportedLanguage language,
            SupportedLanguage fallbackLanguage,
            out string audioPath)
        {
            MultilingualTextTranslation translation;
            bool found = TryGetTranslation(id, language, fallbackLanguage, out translation);
            audioPath = found ? translation.GetAudioPath() : string.Empty;
            return found;
        }

        private void RebuildLookup()
        {
            lookup.Clear();
            EnsureItems();

            for (int i = 0; i < items.Count; i++)
            {
                MultilingualTextItem item = items[i];
                if (item == null || item.id <= 0 || item.translations == null || lookup.ContainsKey(item.id))
                {
                    continue;
                }

                Dictionary<SupportedLanguage, MultilingualTextTranslation> translations =
                    new Dictionary<SupportedLanguage, MultilingualTextTranslation>();

                for (int translationIndex = 0; translationIndex < item.translations.Count; translationIndex++)
                {
                    MultilingualTextTranslation translation = item.translations[translationIndex];
                    if (translation == null
                        || !Enum.IsDefined(typeof(SupportedLanguage), translation.language)
                        || translations.ContainsKey(translation.language))
                    {
                        continue;
                    }

                    translations.Add(translation.language, translation);
                }

                if (translations.Count > 0)
                {
                    lookup.Add(item.id, translations);
                }
            }
        }

        private void CancelActiveLoad(bool invokeCallback)
        {
            LoadRequestState requestState = activeLoadRequest;
            if (requestState == null)
            {
                return;
            }

            activeLoadRequest = null;
            requestState.isCancelled = true;
            requestState.invokeCallbackWhenCancelled = invokeCallback;
            requestState.request.Abort();
        }

        private void EnsureItems()
        {
            if (items == null)
            {
                items = new List<MultilingualTextItem>();
            }
        }
    }
}
