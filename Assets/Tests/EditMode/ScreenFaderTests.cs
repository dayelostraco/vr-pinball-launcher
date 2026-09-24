using NUnit.Framework;
using UnityEngine;

namespace VRLauncher.Tests
{
    public class ScreenFaderTests
    {
        private GameObject cameraObject;

        [SetUp]
        public void SetUp() => cameraObject = new GameObject("FaderCamera", typeof(Camera));

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(cameraObject);

        [Test]
        public void SetImmediate_ShowsAndHidesTheOverlay()
        {
            ScreenFader fader = ScreenFader.Create(cameraObject.GetComponent<Camera>());
            fader.SetImmediate(1f, "Loading Congo…");
            Assert.AreEqual(1f, fader.Alpha);
            Assert.AreEqual("Loading Congo…", fader.Text);
            Assert.IsTrue(fader.GetComponentInChildren<MeshRenderer>().enabled);

            fader.SetImmediate(0f);
            Assert.IsFalse(fader.GetComponentInChildren<MeshRenderer>().enabled);
        }

        [Test]
        public void Overlay_SitsInsideTheNearClipAndInFrontOfTheCamera()
        {
            Camera camera = cameraObject.GetComponent<Camera>();
            ScreenFader fader = ScreenFader.Create(camera);
            float distance = fader.transform.localPosition.z;
            Assert.Greater(distance, camera.nearClipPlane);
            Assert.Less(distance, 1f);
        }
    }
}
