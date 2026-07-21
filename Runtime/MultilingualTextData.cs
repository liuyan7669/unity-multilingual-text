using System;
using System.Collections.Generic;

namespace Cowart.MultilingualText
{
    /// <summary>
    /// 系统支持的语言类型。枚举值会直接保存到 JSON 中，不应随意调整。
    /// </summary>
    public enum SupportedLanguage
    {
        ChineseSimplified = 0,
        English = 1,
        Japanese = 2,
        Korean = 3,
        French = 4,
        Spanish = 5,
        Russian = 6,
        German = 7,
        Italian = 8,
        Portuguese = 9
    }

    /// <summary>
    /// 多语言文本 JSON 根节点。
    /// </summary>
    [Serializable]
    public class MultilingualTextTable
    {
        public int nextId = 1;
        public List<MultilingualTextItem> items = new List<MultilingualTextItem>();
    }

    /// <summary>
    /// 一条可通过 ID 查询的多语言文本。
    /// </summary>
    [Serializable]
    public class MultilingualTextItem
    {
        public int id;
        public List<MultilingualTextTranslation> translations = new List<MultilingualTextTranslation>();
    }

    /// <summary>
    /// 指定语言下的文本内容和提示音频。
    /// </summary>
    [Serializable]
    public class MultilingualTextTranslation
    {
        public SupportedLanguage language = SupportedLanguage.ChineseSimplified;
        public string content;
        public string audioPath;

        /// <summary>
        /// 获取当前语言的完整文本内容。
        /// </summary>
        public string GetText()
        {
            return content ?? string.Empty;
        }

        /// <summary>
        /// 获取提示音频的 Resources 相对路径。
        /// </summary>
        public string GetAudioPath()
        {
            return string.IsNullOrEmpty(audioPath) ? string.Empty : audioPath;
        }
    }
}
