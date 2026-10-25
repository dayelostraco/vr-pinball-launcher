using NUnit.Framework;

namespace VRLauncher.Tests
{
    public class MenuInputStateTests
    {
        private MenuInputState state;
        [SetUp] public void SetUp() { state = new MenuInputState(); Tick(default); }
        private MenuActions Tick(MenuInputFrame frame, bool enabled = true, float dt = 0.02f) => state.Update(frame, enabled, dt);
        private MenuActions Press(MenuButtons buttons) => Tick(new MenuInputFrame { Buttons = buttons });

        [TestCase(MenuButtons.LeftShift, MenuActions.Previous)]
        [TestCase(MenuButtons.LeftArrow, MenuActions.Previous)]
        [TestCase(MenuButtons.RightShift, MenuActions.Next)]
        [TestCase(MenuButtons.RightArrow, MenuActions.Next)]
        [TestCase(MenuButtons.Enter, MenuActions.Launch)]
        [TestCase(MenuButtons.Space, MenuActions.Launch)]
        [TestCase(MenuButtons.A, MenuActions.Launch)]
        [TestCase(MenuButtons.Escape, MenuActions.Quit)]
        [TestCase(MenuButtons.Favorite, MenuActions.Favorite)]
        [TestCase(MenuButtons.B, MenuActions.Favorite)]
        [TestCase(MenuButtons.View, MenuActions.NextView)]
        [TestCase(MenuButtons.X, MenuActions.NextView)]
        [TestCase(MenuButtons.Surprise, MenuActions.Surprise)]
        [TestCase(MenuButtons.RightStickClick, MenuActions.Surprise)]
        [TestCase(MenuButtons.Details, MenuActions.Details)]
        [TestCase(MenuButtons.LeftStickClick, MenuActions.Details)]
        [TestCase(MenuButtons.Setup, MenuActions.Setup)]
        public void PressFiresOnceAndRearmsAfterRelease(MenuButtons button, MenuActions action)
        {
            Assert.AreEqual(action, Press(button));
            Assert.AreEqual(MenuActions.None, Press(button));
            Tick(default);
            Assert.AreEqual(action, Press(button));
        }

        [Test]
        public void SetupOwnsFrameAndTypingCannotDriveMenu()
        {
            Assert.AreEqual(MenuActions.Setup, Press(MenuButtons.Setup | MenuButtons.Enter | MenuButtons.Escape | MenuButtons.Favorite));
            var typing = new MenuInputFrame { Buttons = MenuButtons.Space | MenuButtons.Surprise | MenuButtons.Escape };
            Assert.AreEqual(MenuActions.None, Tick(typing, false));
            Assert.AreEqual(MenuActions.None, Tick(typing));
            Assert.AreEqual(MenuActions.None, Tick(typing));
            Tick(default);
            Assert.AreEqual(MenuActions.Launch, Press(MenuButtons.Space));
        }

        [Test]
        public void SurpriseWinsOverSimultaneousLaunchAndOppositeKeysCancel()
        {
            Assert.AreEqual(MenuActions.Surprise, Press(MenuButtons.Surprise | MenuButtons.A));
            Assert.AreEqual(MenuActions.None, Press(MenuButtons.LeftArrow | MenuButtons.RightShift));
        }

        [TestCase(0f, -1f, MenuActions.NextView)]
        [TestCase(0f, 1f, MenuActions.PreviousView)]
        [TestCase(-1f, 0f, MenuActions.Previous)]
        [TestCase(1f, 0f, MenuActions.Next)]
        [TestCase(0.7f, -1f, MenuActions.NextView)]
        public void LeftStickSelectsTablesOrViewsOnDominantAxis(float x, float y, MenuActions expected)
        {
            Assert.AreEqual(expected, Tick(new MenuInputFrame { LeftX = x, LeftY = y }));
        }

        [TestCase(0f, -1f, MenuActions.NextLetter)]
        [TestCase(0f, 1f, MenuActions.PreviousLetter)]
        [TestCase(-1f, 0f, MenuActions.Previous)]
        [TestCase(1f, 0f, MenuActions.Next)]
        public void RightStickSelectsTablesOrLetters(float x, float y, MenuActions expected)
        {
            Assert.AreEqual(expected, Tick(new MenuInputFrame { RightX = x, RightY = y }));
        }

        [Test]
        public void HeldControlsOnReturnRequireNeutralBeforeAnyAction()
        {
            Tick(default, false);
            var held = new MenuInputFrame { Buttons = MenuButtons.Y | MenuButtons.A, LeftX = 1f, RightY = -1f, LeftTrigger = 1f, RightTrigger = 1f };
            Assert.AreEqual(MenuActions.None, Tick(held));
            Assert.AreEqual(MenuActions.None, Tick(held, dt: 5f));
            Assert.IsFalse(state.QuitHeld);
            Assert.AreEqual(0f, state.QuitHoldProgress);
            Tick(default);
            Assert.AreEqual(MenuActions.Next | MenuActions.NextLetter | MenuActions.Launch, Tick(held));
        }

        [Test]
        public void TriggersRearmAndCancelWhenPressedTogether()
        {
            Assert.AreEqual(MenuActions.Previous, Tick(new MenuInputFrame { LeftTrigger = 1f }));
            Assert.AreEqual(MenuActions.None, Tick(new MenuInputFrame { LeftTrigger = 1f }));
            Tick(default);
            Assert.AreEqual(MenuActions.Next, Tick(new MenuInputFrame { RightTrigger = 1f }));
            Tick(default);
            Assert.AreEqual(MenuActions.None, Tick(new MenuInputFrame { LeftTrigger = 1f, RightTrigger = 1f }));
        }

        [Test]
        public void QuitHoldResetsOnSuspensionAndFiresOncePerHold()
        {
            var held = new MenuInputFrame { Buttons = MenuButtons.Y };
            Assert.AreEqual(MenuActions.None, Tick(held, dt: 1f));
            Assert.IsTrue(state.QuitHeld);
            Assert.AreEqual(0.5f, state.QuitHoldProgress);
            Assert.AreEqual(MenuActions.Quit, Tick(held, dt: 1f));
            Assert.AreEqual(MenuActions.None, Tick(held, dt: 1f));
            Tick(default, false);
            Assert.IsFalse(state.QuitHeld);
            Tick(default);
            Assert.AreEqual(MenuActions.Quit, Tick(held, dt: 2f));
        }

        [Test]
        public void HorizontalStickRepeatsButVerticalGestureDoesNot()
        {
            var right = new MenuInputFrame { LeftX = 1f };
            Assert.AreEqual(MenuActions.Next, Tick(right));
            Assert.AreEqual(MenuActions.Next, Tick(right, dt: 0.5f));
            Tick(default);
            var down = new MenuInputFrame { LeftY = -1f };
            Assert.AreEqual(MenuActions.NextView, Tick(down));
            Assert.AreEqual(MenuActions.None, Tick(down, dt: 5f));
        }
    }
}
