using SephiriaBuildOverlay.Plugin;

namespace SephiriaBuildOverlay.Tests
{
    public sealed class ControllerBridgeTests
    {
        [Fact]
        public void ReadsOnlyPairedPadAndNativeLabelsNotGlobalCurrentDevice()
        {
            var bridge = new ControllerInputBridge(_ => typeof(FakeHandler));
            var local = new UnityEngine.InputSystem.Gamepad { deviceId = 17 };
            local.selectButton.isPressed = true; local.dpad.right.isPressed = true;
            local.selectButton.shortDisplayName = "Share";
            var remote = new UnityEngine.InputSystem.Gamepad { deviceId = 999 };
            UnityEngine.InputSystem.Gamepad.current = remote;
            FakeHandler.Current = new FakeHandler { PlayerInput = new FakePlayer { currentControlScheme = "Gamepad", devices = new object[] { local } } };
            bridge.Capture("selectButton");
            Assert.True(bridge.GamepadMode); Assert.Equal("17", bridge.DeviceId);
            Assert.Equal(PadButtons.Modifier | PadButtons.Right, bridge.Buttons);
            Assert.Equal("Share+→", bridge.Prompt("→"));
            FakeHandler.Current.PlayerInput.currentControlScheme = "Keyboard&Mouse";
            bridge.Capture("selectButton");
            Assert.False(bridge.GamepadMode); Assert.Null(bridge.DeviceId); Assert.Equal(PadButtons.None, bridge.Buttons);
            Assert.Equal(1, bridge.PairedGamepads);
        }

        [Fact]
        public void AmbiguousPairingDisconnectedPadUnknownModifierOrMissingHandlerFailClosed()
        {
            var local = new UnityEngine.InputSystem.Gamepad();
            var second = new UnityEngine.InputSystem.Gamepad { deviceId = 2 };
            var player = new FakePlayer { currentControlScheme = "Gamepad", devices = new object[] { local, second } };
            FakeHandler.Current = new FakeHandler { PlayerInput = player };
            var bridge = new ControllerInputBridge(_ => typeof(FakeHandler));
            bridge.Capture("selectButton"); Assert.Null(bridge.DeviceId); Assert.Equal(2, bridge.PairedGamepads);
            player.devices = new object[] { local }; local.added = false;
            bridge.Capture("selectButton"); Assert.Null(bridge.DeviceId);
            local.added = true;
            bridge.Capture("badControl"); Assert.Null(bridge.DeviceId);
            var missing = new ControllerInputBridge(_ => null);
            missing.Capture("selectButton"); Assert.False(missing.GamepadMode); Assert.Null(missing.DeviceId);
        }

        [Fact]
        public void SupportedModifierAndDirectionControlsAreReadByCachedReflection()
        {
            var pad = new UnityEngine.InputSystem.Gamepad();
            pad.leftStickButton.isPressed = true; pad.dpad.up.isPressed = true;
            pad.buttonSouth.isPressed = true; pad.buttonEast.isPressed = true;
            FakeHandler.Current = new FakeHandler { PlayerInput = new FakePlayer { currentControlScheme = "Gamepad", devices = new object[] { pad } } };
            var bridge = new ControllerInputBridge(_ => typeof(FakeHandler));
            bridge.Capture("leftStickButton");
            Assert.Equal(PadButtons.Modifier | PadButtons.Up | PadButtons.Submit | PadButtons.Cancel, bridge.Buttons);
            pad.dpad.up.isPressed = false; pad.dpad.left.isPressed = true;
            bridge.Capture("leftStickButton"); Assert.True((bridge.Buttons & PadButtons.Left) != 0);
        }
    }
    public sealed class FakeHandler
    {
        public static FakeHandler Current { get; set; } = new();
        public FakePlayer PlayerInput { get; set; } = new();
    }
    public sealed class FakePlayer
    {
        public string currentControlScheme { get; set; } = string.Empty;
        public object[] devices { get; set; } = Array.Empty<object>();
    }
}

// Read-only bridge fixtures, not runtime InputSystem replacement or device injection.
namespace UnityEngine.InputSystem
{
    public class Gamepad
    {
        public static Gamepad? current { get; set; }
        public bool added { get; set; } = true;
        public int deviceId { get; set; } = 1;
        public Button selectButton { get; } = new();
        public Button leftStickButton { get; } = new();
        public Button rightStickButton { get; } = new();
        public Button buttonSouth { get; } = new();
        public Button buttonEast { get; } = new();
        public Dpad dpad { get; } = new();
    }
    public sealed class Button
    {
        public bool isPressed { get; set; }
        public string shortDisplayName { get; set; } = "Button";
    }
    public sealed class Dpad
    {
        public Button up { get; } = new(); public Button down { get; } = new();
        public Button left { get; } = new(); public Button right { get; } = new();
    }
}
