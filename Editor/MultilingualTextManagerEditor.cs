using UnityEditor;
using UnityEngine;

namespace Cowart.MultilingualText.Editor
{
    [CustomEditor(typeof(MultilingualTextManager))]
    [CanEditMultipleObjects]
    public class MultilingualTextManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space(8f);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("多语言文本编辑器", EditorStyles.boldLabel);
                GUIContent icon = EditorGUIUtility.IconContent("TextAsset Icon");
                if (GUILayout.Button(new GUIContent(" 打开多语言文本编辑器", icon.image), GUILayout.Height(30f)))
                {
                    MultilingualTextEditorWindow.Open();
                }
            }
        }
    }
}
