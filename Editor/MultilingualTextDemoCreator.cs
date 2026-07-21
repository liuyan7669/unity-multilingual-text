using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Cowart.MultilingualText.Editor
{
    internal static class MultilingualTextDemoCreator
    {
        private const string FileName = "multilingual_text.json";

        private const string DemoJson =
@"{
    ""nextId"": 3,
    ""items"": [
        {
            ""id"": 1,
            ""translations"": [
                {
                    ""language"": 0,
                    ""content"": ""开始"",
                    ""audioPath"": """"
                },
                {
                    ""language"": 1,
                    ""content"": ""Start"",
                    ""audioPath"": """"
                }
            ]
        },
        {
            ""id"": 2,
            ""translations"": [
                {
                    ""language"": 0,
                    ""content"": ""第一段中文提示。\n第二段中文提示。"",
                    ""audioPath"": ""Audio/Tips/Chinese/tip_001""
                },
                {
                    ""language"": 1,
                    ""content"": ""First English tip.\nSecond English tip."",
                    ""audioPath"": ""Audio/Tips/English/tip_001""
                }
            ]
        }
    ]
}";

        [MenuItem("Tools/多语言文本/创建示例配置")]
        private static void CreateDemoJson()
        {
            string folderPath = Path.Combine(Application.dataPath, "StreamingAssets");
            Directory.CreateDirectory(folderPath);

            string filePath = Path.Combine(folderPath, FileName);
            if (File.Exists(filePath))
            {
                Debug.LogWarning($"示例文件已存在，未覆盖：{filePath}");
                return;
            }

            File.WriteAllText(filePath, DemoJson, new UTF8Encoding(false));
            AssetDatabase.Refresh();
            Debug.Log($"已创建多语言文本示例文件：{filePath}");
        }
    }
}
