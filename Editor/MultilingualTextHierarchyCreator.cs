using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Cowart.MultilingualText.Editor
{
    /// <summary>
    /// 提供在 Hierarchy 中定位或创建多语言文本的菜单。
    /// </summary>
    public static class MultilingualTextHierarchyCreator
    {
        private const string MenuPath = "GameObject/Cowart/语言管理/创建多语言文本";
        private const string ObjectName = "[多语言文本]";
        private const int MenuPriority = 100;

        /// <summary>
        /// 定位目标场景中的现有管理器；不存在时在场景根节点创建一个。
        /// </summary>
        /// <param name="menuCommand">Unity 菜单命令上下文。</param>
        [MenuItem(MenuPath, false, MenuPriority)]
        private static void LocateOrCreate(MenuCommand menuCommand)
        {
            Scene targetScene = GetTargetScene(menuCommand.context as GameObject);
            if (!targetScene.IsValid() || !targetScene.isLoaded)
            {
                return;
            }

            MultilingualTextManager existingManager = FindManager(targetScene);
            if (existingManager != null)
            {
                SelectAndPing(existingManager.gameObject);
                return;
            }

            GameObject managerObject = new GameObject(ObjectName, typeof(MultilingualTextManager));
            if (managerObject.scene != targetScene)
            {
                SceneManager.MoveGameObjectToScene(managerObject, targetScene);
            }

            Undo.RegisterCreatedObjectUndo(managerObject, "Create Multilingual Text");
            SelectAndPing(managerObject);
            EditorSceneManager.MarkSceneDirty(targetScene);
        }

        /// <summary>获取右键上下文物体所在场景，未提供上下文时使用当前活动场景。</summary>
        private static Scene GetTargetScene(GameObject contextObject)
        {
            return contextObject != null && contextObject.scene.IsValid()
                ? contextObject.scene
                : SceneManager.GetActiveScene();
        }

        /// <summary>查找指定场景中包含未激活物体在内的首个多语言文本管理器。</summary>
        private static MultilingualTextManager FindManager(Scene scene)
        {
            GameObject[] rootObjects = scene.GetRootGameObjects();
            for (int i = 0; i < rootObjects.Length; i++)
            {
                MultilingualTextManager manager = rootObjects[i].GetComponentInChildren<MultilingualTextManager>(true);
                if (manager != null)
                {
                    return manager;
                }
            }

            return null;
        }

        /// <summary>在 Hierarchy 中选中并高亮指定物体。</summary>
        private static void SelectAndPing(GameObject targetObject)
        {
            Selection.activeGameObject = targetObject;
            EditorGUIUtility.PingObject(targetObject);
        }
    }
}
