using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using MeowField.Domain;

namespace MeowField.App;

public static class LocalizationService
{
    public static string TranslateDynamic(object? value, bool english)
    {
        if (value is InputMode inputMode) return inputMode switch
        {
            InputMode.SendInput => english ? "SendInput" : "前台模拟按键",
            InputMode.WindowMessage => english ? "Window message" : "窗口消息（后台）",
            _ => inputMode.ToString(),
        };
        if (value is InstrumentKind instrument) return instrument switch
        {
            InstrumentKind.Piano => english ? "Piano" : "钢琴",
            InstrumentKind.Drums => english ? "Drums" : "架子鼓",
            InstrumentKind.Microphone => english ? "Microphone" : "麦克风",
            _ => instrument.ToString(),
        };
        if (value is ChordMode chordMode) return chordMode switch
        {
            ChordMode.Off => english ? "Off" : "关闭",
            ChordMode.Prefer => english ? "Prefer chords" : "优先和弦",
            ChordMode.Melody => english ? "Melody first" : "优先旋律",
            ChordMode.Smart => english ? "Smart" : "智能识别",
            _ => chordMode.ToString(),
        };
        if (value is NearestWhiteDirection direction) return direction switch
        {
            NearestWhiteDirection.Up => english ? "Map upward" : "向上映射",
            _ => english ? "Map downward" : "向下映射",
        };
        if (value is CollisionStrategy collisionStrategy) return collisionStrategy switch
        {
            CollisionStrategy.OriginalFold => english ? "Original fold" : "原版折叠",
            CollisionStrategy.SmartOctaveFold => english ? "Smart octave fold" : "智能八度折叠",
            CollisionStrategy.PerNoteMinimal => english ? "Minimal per-note shift" : "逐音符最小移位",
            _ => collisionStrategy.ToString(),
        };

        if (!english) return value?.ToString() ?? string.Empty;

        var text = value?.ToString() ?? string.Empty;
        var exact = text switch
        {
            "准备就绪" => "Ready",
            "正在解析 MIDI..." => "Parsing MIDI...",
            "未找到可绑定窗口" => "No bindable window found",
            "请先载入 MIDI" => "Load a MIDI first",
            "窗口消息模式需要先绑定目标窗口" => "Bind a target window before using window-message mode",
            "正在播放" => "Playing",
            "已暂停" => "Paused",
            "播放完成" => "Playback complete",
            "已停止" => "Stopped",
            "正在加载曲库..." => "Loading library...",
            "曲库为空" => "Library is empty",
            "扫描已取消" => "Scan cancelled",
            "正在加载配置档案..." => "Loading profiles...",
            "已载入当前键位映射" => "Current key mapping loaded",
            "已恢复 GM 架子鼓映射" => "GM drum mapping restored",
            "已恢复默认键位映射" => "Default key mapping restored",
            "请输入预设名称" => "Enter a preset name",
            "预设已删除" => "Preset deleted",
            "未测量" => "Not measured",
            "尚未设置定时任务" => "No scheduled task",
            "NTP 测量完成" => "NTP measurement complete",
            "NTP 测量失败" => "NTP measurement failed",
            "请先载入有效 MIDI" => "Load a valid MIDI first",
            "时间格式应为 HH:mm 或 HH:mm:ss" => "Time must use HH:mm or HH:mm:ss",
            "定时任务已取消" => "Scheduled task cancelled",
            "请选择有效的音频文件" => "Choose a valid audio file",
            "请选择音频文件" => "Choose an audio file",
            "诊断信息就绪" => "Diagnostics ready",
            "未找到 PianoTrans.exe" => "PianoTrans.exe not found",
            _ => null,
        };
        if (exact is not null) return exact;

        (string Chinese, string English)[] prefixes =
        [
            ("已载入 ", "Loaded "), ("载入失败：", "Load failed: "),
            ("已发现 ", "Found "), ("设置恢复失败：", "Settings restore failed: "),
            ("播放失败：", "Playback failed: "), ("曲库加载失败：", "Library load failed: "),
            ("共 ", "Total: "), ("正在扫描 ", "Scanning "),
            ("扫描完成，新增 ", "Scan complete, added "), ("扫描失败：", "Scan failed: "),
            ("添加失败：", "Add failed: "), ("已添加 ", "Added "),
            ("已应用 ", "Applied "), ("已加载 ", "Loaded "),
            ("配置加载失败：", "Profile load failed: "), ("已应用：", "Applied: "),
            ("已应用预设：", "Preset applied: "), ("已保存预设：", "Preset saved: "),
            ("已覆盖预设：", "Preset overwritten: "), ("已设置：", "Scheduled: "),
            ("定时设置失败：", "Scheduling failed: "), ("定时任务失败：", "Scheduled task failed: "),
            ("转换失败：", "Conversion failed: "), ("已导出：", "Exported: "),
            ("导出失败：", "Export failed: "), ("已删除原文件：", "Source deleted: "),
            ("删除原文件失败：", "Source deletion failed: "),
        ];
        foreach (var prefix in prefixes)
        {
            if (text.StartsWith(prefix.Chinese, StringComparison.Ordinal))
            {
                var translated = prefix.English + text[prefix.Chinese.Length..];
                return translated
                    .Replace(" 个窗口", " windows", StringComparison.Ordinal)
                    .Replace(" 个音符", " notes", StringComparison.Ordinal)
                    .Replace(" 首", " tracks", StringComparison.Ordinal)
                    .Replace(" 个档案", " profiles", StringComparison.Ordinal)
                    .Replace(" 个预设", " presets", StringComparison.Ordinal)
                    .Replace(" 个自定义键位", " custom keys", StringComparison.Ordinal);
            }
        }
        return text;
    }

    private sealed class OriginalValues
    {
        public string? Text { get; init; }
        public string? Content { get; init; }
        public string? ToolTip { get; init; }
    }

    private static readonly ConditionalWeakTable<DependencyObject, OriginalValues> Originals = new();
    private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["未绑定目标窗口"] = "No target window",
        ["主题"] = "Theme", ["设置"] = "Settings", ["语言"] = "Language",
        ["播放"] = "Playback", ["曲库"] = "Library", ["定时任务"] = "Scheduler", ["音频转谱"] = "Audio to MIDI", ["键位与预设"] = "Keys & Presets", ["日志与诊断"] = "Logs & Diagnostics",
        ["当前："] = "Current: ",
        ["打开 MIDI"] = "Open MIDI", ["音符预览"] = "Note preview", ["音符"] = "notes", ["事件"] = "events", ["活动键位"] = "Active keys", ["虚拟键盘"] = "Virtual keyboard",
        ["播放参数"] = "Playback parameters", ["目标窗口"] = "Target window", ["刷新窗口"] = "Refresh windows", ["输入方式"] = "Input mode", ["乐器"] = "Instrument", ["乐器 / 档案"] = "Instrument / Profile", ["和弦处理"] = "Chord handling", ["冲突处理"] = "Collision handling", ["速度"] = "Speed", ["转调"] = "Transpose", ["最大复音"] = "Max polyphony", ["链路补偿"] = "Link compensation", ["优先映射最近白键"] = "Prefer nearest white key", ["载入时自动移调"] = "Auto transpose on load", ["播放队列"] = "Play queue", ["自动下一首"] = "Auto play next", ["上一首"] = "Previous", ["下一首"] = "Next", ["移出队列"] = "Remove from queue", ["清空队列"] = "Clear queue", ["上移"] = "Move up", ["下移"] = "Move down", ["停止"] = "Stop",
        ["音域下限"] = "Range low", ["音域上限"] = "Range high", ["适配率"] = "Fit ratio",
        ["音频转谱"] = "Audio to MIDI", ["调用本机 PianoTrans 将音频转换为 MIDI"] = "Convert audio to MIDI with local PianoTrans", ["选择目录"] = "Choose folder", ["输入音频"] = "Input audio", ["浏览"] = "Browse", ["输出 MIDI"] = "Output MIDI", ["开始转换"] = "Convert", ["取消转换"] = "Cancel conversion",
        ["定时任务"] = "Scheduler", ["使用本地时间或 NTP 偏移，在指定时刻启动当前 MIDI"] = "Start the current MIDI at a local time with optional NTP offset", ["校时状态"] = "Clock status", ["服务器与测量结果"] = "Server and measurement", ["测量 NTP"] = "Measure NTP", ["执行时间"] = "Execution time", ["日期"] = "Date", ["时间"] = "Time", ["MIDI 路径（留空使用当前曲目）"] = "MIDI path (blank uses current track)", ["使用最近一次 NTP 偏移"] = "Use latest NTP offset",
        ["曲库"] = "Library", ["扫描、搜索并管理本地 MIDI，原文件不会被自动删除"] = "Scan, search and manage local MIDI files; originals are never deleted", ["添加文件"] = "Add files", ["扫描目录"] = "Scan folder", ["搜索曲名或目录"] = "Search name or folder", ["加载"] = "Load", ["加入队列"] = "Add to queue", ["移出曲库"] = "Remove from library", ["上一页"] = "Previous page", ["下一页"] = "Next page",
        ["取消扫描"] = "Cancel scan", ["曲名"] = "Title", ["目录"] = "Folder", ["时长"] = "Duration", ["路径"] = "Path", ["载入"] = "Load",
        ["删除原文件"] = "Delete source file",
        ["设置任务"] = "Schedule", ["取消任务"] = "Cancel task", ["选择日期"] = "Select date",
        ["键位与预设"] = "Keys & Presets", ["编辑 MIDI 音符到游戏键位的映射，并管理可复用的播放参数"] = "Edit MIDI-to-game key mappings and reusable playback presets", ["自定义键位映射"] = "Custom key mapping", ["留空使用默认映射；同一个游戏键可以对应多个 MIDI 音符"] = "Leave blank for defaults; multiple MIDI notes may share one game key", ["载入当前"] = "Load current", ["应用映射"] = "Apply mapping", ["恢复默认"] = "Restore defaults", ["支持单字符键位；应用后会立即重新生成当前 MIDI 的播放事件"] = "Single-character keys are supported; applying regenerates current playback events", ["游戏配置档案"] = "Game profiles", ["应用档案"] = "Apply profile", ["参数预设"] = "Parameter presets", ["保存当前"] = "Save current", ["应用预设"] = "Apply preset", ["覆盖"] = "Overwrite", ["删除"] = "Delete",
        ["日志与诊断"] = "Logs & Diagnostics", ["查看运行环境并导出故障诊断包"] = "Inspect runtime information and export a diagnostic package", ["运行环境"] = "Runtime environment", ["日志目录"] = "Log directory", ["导出诊断包"] = "Export diagnostics",
        ["MIDI"] = "MIDI", ["音名"] = "Note", ["游戏键"] = "Game key", ["PianoTrans"] = "PianoTrans",
    };

    public static void Apply(DependencyObject root, bool english)
    {
        foreach (var element in Descendants(root))
        {
            if (!Originals.TryGetValue(element, out var original))
            {
                original = new OriginalValues
                {
                    Text = element is TextBlock text && !BindingOperations.IsDataBound(text, TextBlock.TextProperty) ? text.Text : null,
                    Content = element is ContentControl content && !BindingOperations.IsDataBound(content, ContentControl.ContentProperty) && content.Content is string value ? value : null,
                    ToolTip = !BindingOperations.IsDataBound(element, FrameworkElement.ToolTipProperty) && element is FrameworkElement framework && framework.ToolTip is string tip ? tip : null,
                };
                Originals.Add(element, original);
            }

            if (element is TextBlock textBlock && original.Text is not null) textBlock.Text = english ? Translate(original.Text) : original.Text;
            if (element is ContentControl contentControl && original.Content is not null) contentControl.Content = english ? Translate(original.Content) : original.Content;
            if (element is FrameworkElement frameworkElement && original.ToolTip is not null) frameworkElement.ToolTip = english ? Translate(original.ToolTip) : original.ToolTip;
        }
    }

    private static string Translate(string value)
    {
        var direct = value switch
        {
            "\u5c1a\u672a\u8f7d\u5165 MIDI" => "No MIDI loaded",
            "\u8bf7\u9009\u62e9\u6587\u4ef6\uff0c\u6216\u5c06 MIDI \u62d6\u5165\u7a97\u53e3" => "Choose a file or drop a MIDI into the window",
            "\u51c6\u5907\u5c31\u7eea" => "Ready",
            _ => null,
        };
        if (direct is not null) return direct;
        if (English.TryGetValue(value, out var translated)) return translated;
        var legacyKey = Encoding.Default.GetString(Encoding.UTF8.GetBytes(value));
        return English.TryGetValue(legacyKey, out translated) ? translated : value;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
        }
    }
}
