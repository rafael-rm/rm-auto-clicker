using RM.AutoClicker.Core.Models;

namespace RM.AutoClicker.Core.Tests;

public sealed class HotkeyGestureTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void AcceptsEverySupportedModifierCombination(uint modifiers)
    {
        new HotkeyGesture(0x41, (HotkeyModifiers)modifiers).Validate();
    }

    [Theory]
    [InlineData(0x30)]
    [InlineData(0x39)]
    [InlineData(0x5A)]
    [InlineData(0x70)]
    [InlineData(0x87)]
    [InlineData(0x60)]
    [InlineData(0x69)]
    [InlineData(0xBA)]
    [InlineData(0xE2)]
    [InlineData(0xB3)]
    public void AcceptsKeysOutsideTheOldFunctionKeyList(uint key)
    {
        new HotkeyGesture(key).Validate();
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0x100, 0)]
    [InlineData(0x10, 0)]
    [InlineData(0xA2, 0)]
    [InlineData(0x5B, 0)]
    [InlineData(0xE5, 0)]
    [InlineData(0xE7, 0)]
    [InlineData(0x7B, 0)]
    [InlineData(0x7B, 2)]
    [InlineData(0x41, 8)]
    [InlineData(0x41, 16)]
    [InlineData(0x79, 6)]
    [InlineData(0x73, 1)]
    [InlineData(0x09, 1)]
    [InlineData(0x09, 5)]
    [InlineData(0x09, 3)]
    [InlineData(0x73, 5)]
    [InlineData(0x1B, 2)]
    [InlineData(0x1B, 6)]
    [InlineData(0x2E, 3)]
    [InlineData(0x2E, 7)]
    public void RejectsInvalidOrReservedGestures(uint key, uint modifiers)
    {
        Assert.Throws<AppValidationException>(() => new HotkeyGesture(key, (HotkeyModifiers)modifiers).Validate());
    }

    [Theory]
    [InlineData(0x77, 0, "F8")]
    [InlineData(0x41, 3, "Ctrl+Alt+A")]
    [InlineData(0x31, 6, "Ctrl+Shift+1")]
    [InlineData(0x0D, 4, "Shift+Enter")]
    [InlineData(0x60, 0, "Num 0")]
    [InlineData(0x2E, 2, "Ctrl+Delete")]
    public void FormatsDistinctKeysAndModifiers(uint key, uint modifiers, string name)
    {
        Assert.Equal(name, new HotkeyGesture(key, (HotkeyModifiers)modifiers).ToString());
    }
}
