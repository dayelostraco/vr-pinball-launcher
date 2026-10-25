using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace VRLauncher.EditorTools
{
    /// <summary>
    /// One-off migration of VRLauncher.unity from the flat carousel to the arcade room: removes
    /// the carousel canvas and the directional light (the room brings its own lights), swaps
    /// TableCarousel for LauncherBootstrap, and makes sure the shaders the room creates at
    /// runtime are included in builds. Safe to run more than once.
    /// </summary>
    public static class ArcadeSceneSetup
    {
        private const string ScenePath = "Assets/Scenes/VRLauncher.unity";

        [MenuItem("VR Launcher/Set Up Arcade Scene")]
        public static void Run()
        {
            try
            {
                var scene = EditorSceneManager.OpenScene(ScenePath);
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root.name == "Canvas" || root.name == "Directional Light")
                    {
                        UnityEngine.Object.DestroyImmediate(root);
                    }
                }

                GameObject host = scene.GetRootGameObjects().First(g => g.name == "VRLauncherManager");
                foreach (MonoBehaviour behaviour in host.GetComponents<MonoBehaviour>())
                {
                    if (behaviour != null && behaviour.GetType().Name == "TableCarousel")
                    {
                        UnityEngine.Object.DestroyImmediate(behaviour);
                    }
                }
                if (host.GetComponent<LauncherBootstrap>() == null)
                {
                    host.AddComponent<LauncherBootstrap>();
                }

                EditorSceneManager.SaveScene(scene);

                AlwaysInclude("Standard");
                AlwaysInclude("Sprites/Default");
                AssetDatabase.SaveAssets();

                Debug.Log("[unity-batch] OK scene migrated");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[unity-batch] FAILED {ex}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        private static void AlwaysInclude(string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null) throw new InvalidOperationException($"Shader '{shaderName}' not found");

            var settings = AssetDatabase.LoadAssetAtPath<GraphicsSettings>("ProjectSettings/GraphicsSettings.asset");
            var serialized = new SerializedObject(settings);
            SerializedProperty list = serialized.FindProperty("m_AlwaysIncludedShaders");
            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader) return;
            }
            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
            serialized.ApplyModifiedProperties();
        }
    }
}
