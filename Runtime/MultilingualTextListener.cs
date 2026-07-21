using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Cowart.MultilingualText
{
    /// <summary>
    /// 监听语言变化，并自动更新同一对象上的 Text 或 TMP_Text 组件。
    /// </summary>
    [DisallowMultipleComponent]
    public class MultilingualTextListener : MonoBehaviour, IMultilingualTextObserver
    {
        private const int UnboundTextId = 0;

        [SerializeField, Min(0), Header("多语言文本 ID（0 表示未绑定）")]
        [Tooltip("填写 multilingual_text.json 中大于 0 的内容 ID；0 表示当前组件不绑定任何多语言内容。")]
        private int textId = UnboundTextId;

        [SerializeField, Header("缺少文本时清空组件")]
        [Tooltip("启用后，配置已加载但找不到对应 ID 时会清空 Text/TMP_Text；关闭后保留当前显示内容。")]
        private bool clearTextWhenMissing = true;

        private Text legacyText;
        private TMP_Text tmpText;
        private bool warnedMissingId;

        public Action TextUpdated;

        /// <summary>
        /// 绑定的多语言文本 ID；0 表示未绑定，正整数对应 JSON 中的内容 ID。
        /// </summary>
        public int TextId
        {
            get { return textId; }
            set
            {
                textId = Mathf.Max(UnboundTextId, value);
                warnedMissingId = false;
                RefreshText();
            }
        }

        private void Reset()
        {
            textId = UnboundTextId;
            warnedMissingId = false;
        }

        private void OnValidate()
        {
            if (textId < UnboundTextId)
            {
                textId = UnboundTextId;
                warnedMissingId = false;
            }
        }

        private void Awake()
        {
            CacheTextComponents();
        }

        private void OnEnable()
        {
            MultilingualTextManager.RegisterObserver(this);
            RefreshText();
        }

        private void OnDisable()
        {
            MultilingualTextManager.UnregisterObserver(this);
        }

        public void OnLanguageChanged(SupportedLanguage language)
        {
            RefreshText();
        }

        /// <summary>
        /// 使用当前语言立即刷新绑定文本。
        /// </summary>
        public void RefreshText()
        {
            if (legacyText == null && tmpText == null)
            {
                CacheTextComponents();
            }

            if (textId <= 0)
            {
                if (MultilingualTextManager.IsLoaded && !warnedMissingId)
                {
                    warnedMissingId = true;
                    Debug.LogWarning("MultilingualTextListener 尚未配置文本 ID，已保留当前文本。", this);
                }

                return;
            }

            warnedMissingId = false;
            string value;
            bool found = MultilingualTextManager.TryGetText(textId, out value);

            if (!found && !MultilingualTextManager.IsLoaded)
            {
                return;
            }

            if (!found)
            {
                if (!clearTextWhenMissing)
                {
                    return;
                }

                value = string.Empty;
                Debug.LogWarning($"没有找到多语言文本 ID“{textId}”对应的内容。", this);
            }

            if (legacyText != null)
            {
                legacyText.text = value;
            }

            if (tmpText != null)
            {
                tmpText.text = value;
            }

            TextUpdated?.Invoke();
        }

        private void CacheTextComponents()
        {
            legacyText = GetComponent<Text>();
            tmpText = GetComponent<TMP_Text>();
        }

    }
}
