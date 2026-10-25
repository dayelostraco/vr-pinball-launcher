using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VRLauncher.Tests
{
    public class RuntimeDiagnosticsTests
    {
        [Test]
        public void SceneInventoryReportsCameraBootstrapAndUiWithoutStartingLauncher()
        {
            var root = new GameObject("Diagnostic fixture");
            var cameraObject = new GameObject("Diagnostic camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            var messages = new List<string>();
            void Receive(string message, string stack, LogType type) { messages.Add(message); }
            Application.logMessageReceived += Receive;
            try
            {
                root.AddComponent<LauncherBootstrap>();
                var canvas = new GameObject("Diagnostic canvas", typeof(Canvas));
                canvas.transform.SetParent(root.transform);
                canvas.GetComponent<Canvas>().worldCamera = cameraObject.GetComponent<Camera>();
                new GameObject("Diagnostic image", typeof(Image)).transform.SetParent(canvas.transform);
                new GameObject("Diagnostic text", typeof(Text)).transform.SetParent(canvas.transform);
                new GameObject("Diagnostic TMP", typeof(TextMeshProUGUI)).transform.SetParent(canvas.transform);
                root.AddComponent<SceneDiagnostic>().LogScene();
                Assert.IsTrue(messages.Exists(m => m.Contains("Main Camera found")));
                Assert.IsTrue(messages.Exists(m => m.Contains("LauncherBootstrap found")));
                Assert.IsTrue(messages.Exists(m => m.Contains("Canvas: Diagnostic canvas")));
                Assert.IsTrue(messages.Exists(m => m.Contains("Image on: Diagnostic image")));
                Assert.IsTrue(messages.Exists(m => m.Contains("Text on: Diagnostic text")));
                Assert.IsTrue(messages.Exists(m => m.Contains("TMP on: Diagnostic TMP")));
                Assert.IsNull(root.GetComponent<TableLauncher>(), "Inventory started launcher components");
            }
            finally
            {
                Application.logMessageReceived -= Receive;
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(cameraObject);
            }
        }
    }
}
