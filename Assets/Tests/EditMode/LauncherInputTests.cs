using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR;

namespace VRLauncher.Tests
{
    public class LauncherInputTests
    {
        private GameObject owner;
        private LauncherInput input;
        private MenuInputFrame frame;
        private List<string> events;

        [SetUp]
        public void SetUp()
        {
            owner = new GameObject("Input fixture");
            input = owner.AddComponent<LauncherInput>();
            input.ReadInput = () => frame;
            events = new List<string>();
            input.Previous += () => events.Add("previous");
            input.Next += () => events.Add("next");
            input.Launch += () => events.Add("launch");
            input.ToggleFavorite += () => events.Add("favorite");
            input.CycleView += () => events.Add("next view");
            input.PreviousView += () => events.Add("previous view");
            input.JumpLetter += step => events.Add("letter " + step);
            input.SurpriseMe += () => events.Add("surprise");
            input.ToggleDetails += () => events.Add("details");
            input.OpenSetup += () => events.Add("setup");
            input.Quit += () => events.Add("quit");
            frame = default;
            input.Tick(0.02f);
        }

        [TearDown] public void TearDown() => Object.DestroyImmediate(owner);

        private sealed class Controller : IMenuController
        {
            public bool IsValid { get; set; } = true;
            public float Trigger { get; set; }
            public Vector2 Axis { get; set; }
            public bool Buttons;
            public bool Pressed(InputFeatureUsage<bool> feature) => Buttons;
        }

        [Test]
        public void SamplingKeepsHandsDistinctAndReacquiresDisconnectedDevices()
        {
            var left = new Controller { Trigger = 0.4f, Axis = new Vector2(-0.6f, 0.7f), Buttons = true };
            var right = new Controller { Trigger = 0.8f, Axis = new Vector2(0.2f, -0.3f), Buttons = true };
            int acquisitions = 0;
            input.KeyPressed = key => key == 0xA0 || key == 0x71;
            input.GetController = node => { acquisitions++; return node == XRNode.LeftHand ? left : right; };
            var sample = input.Sample();
            Assert.AreEqual(MenuButtons.LeftShift | MenuButtons.Setup | MenuButtons.X | MenuButtons.Y |
                MenuButtons.A | MenuButtons.B | MenuButtons.LeftStickClick | MenuButtons.RightStickClick, sample.Buttons);
            Assert.AreEqual(0.4f, sample.LeftTrigger);
            Assert.AreEqual(0.8f, sample.RightTrigger);
            Assert.AreEqual(-0.6f, sample.LeftX);
            Assert.AreEqual(0.7f, sample.LeftY);
            Assert.AreEqual(0.2f, sample.RightX);
            Assert.AreEqual(-0.3f, sample.RightY);
            input.Sample();
            Assert.AreEqual(2, acquisitions, "Valid device handles should be retained");
            left.IsValid = false;
            sample = input.Sample();
            Assert.AreEqual(3, acquisitions);
            Assert.AreEqual(0, sample.LeftTrigger);
            Assert.AreEqual(0, sample.LeftX);
            Assert.AreEqual(MenuButtons.None, sample.Buttons & (MenuButtons.X | MenuButtons.Y | MenuButtons.LeftStickClick));
            left = new Controller();
            right.IsValid = false;
            sample = input.Sample();
            Assert.AreEqual(5, acquisitions);
            Assert.AreEqual(MenuButtons.LeftShift | MenuButtons.Setup, sample.Buttons);
        }

        [Test]
        public void AdapterDispatchesActionsInMenuOrder()
        {
            frame.Buttons = MenuButtons.LeftArrow | MenuButtons.Favorite | MenuButtons.Details | MenuButtons.View | MenuButtons.Enter | MenuButtons.Escape;
            input.Tick(0.02f);
            CollectionAssert.AreEqual(new[] { "previous", "favorite", "details", "next view", "launch", "quit" }, events);
            events.Clear();
            frame = default;
            input.Tick(0.02f);
            frame.Buttons = MenuButtons.RightArrow | MenuButtons.Surprise;
            frame.LeftY = 1f;
            frame.RightY = -1f;
            input.Tick(0.02f);
            CollectionAssert.AreEqual(new[] { "next", "letter 1", "previous view", "surprise" }, events);
            events.Clear();
            frame = default;
            input.Tick(0.02f);
            frame.RightY = 1f;
            input.Tick(0.02f);
            CollectionAssert.AreEqual(new[] { "letter -1" }, events);
        }

        [Test]
        public void SetupCallbackImmediatelySuppressesMenuUntilReleased()
        {
            bool setupOpen = false;
            input.IsSuppressed = () => setupOpen;
            input.OpenSetup += () => setupOpen = true;
            frame.Buttons = MenuButtons.Setup | MenuButtons.Enter;
            input.Tick(0.02f);
            CollectionAssert.AreEqual(new[] { "setup" }, events);
            events.Clear();
            frame.Buttons = MenuButtons.Enter | MenuButtons.Escape;
            input.Tick(0.02f);
            setupOpen = false;
            input.Tick(0.02f);
            input.Tick(0.02f);
            Assert.IsEmpty(events);
            frame = default;
            input.Tick(0.02f);
            frame.Buttons = MenuButtons.Enter;
            input.Tick(0.02f);
            CollectionAssert.AreEqual(new[] { "launch" }, events);
        }

        [Test]
        public void DisabledInputDoesNotSampleHardwareAndClearsQuitProgress()
        {
            frame.Buttons = MenuButtons.Y;
            input.Tick(1f);
            Assert.IsTrue(input.QuitHeld);
            Assert.AreEqual(0.5f, input.QuitHoldProgress);
            input.InputEnabled = false;
            input.ReadInput = () => throw new System.InvalidOperationException("Disabled input sampled hardware");
            input.Tick(1f);
            Assert.IsFalse(input.QuitHeld);
            Assert.AreEqual(0f, input.QuitHoldProgress);
            Assert.IsEmpty(events);
        }
    }
}
