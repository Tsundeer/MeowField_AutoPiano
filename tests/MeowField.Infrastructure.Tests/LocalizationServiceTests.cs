using MeowField.App;
using MeowField.Domain;

namespace MeowField.Infrastructure.Tests;

public sealed class LocalizationServiceTests
{
    [Theory]
    [InlineData(InputMode.SendInput, "前台模拟按键")]
    [InlineData(InputMode.WindowMessage, "窗口消息（后台）")]
    public void TranslateDynamic_UsesChineseLabelsForInputModes(InputMode value, string expected)
    {
        Assert.Equal(expected, LocalizationService.TranslateDynamic(value, english: false));
    }

    [Theory]
    [InlineData(ChordMode.Off, "关闭")]
    [InlineData(ChordMode.Prefer, "优先和弦")]
    [InlineData(ChordMode.Melody, "优先旋律")]
    [InlineData(ChordMode.Smart, "智能识别")]
    public void TranslateDynamic_UsesChineseLabelsForChordModes(ChordMode value, string expected)
    {
        Assert.Equal(expected, LocalizationService.TranslateDynamic(value, english: false));
    }

    [Theory]
    [InlineData(CollisionStrategy.OriginalFold, "原版折叠")]
    [InlineData(CollisionStrategy.SmartOctaveFold, "智能八度折叠")]
    [InlineData(CollisionStrategy.PerNoteMinimal, "逐音符最小移位")]
    public void TranslateDynamic_UsesChineseLabelsForCollisionStrategies(CollisionStrategy value, string expected)
    {
        Assert.Equal(expected, LocalizationService.TranslateDynamic(value, english: false));
    }
}
