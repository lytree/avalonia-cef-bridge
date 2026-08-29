using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Declarative;
using Avalonia.Media;
using Avalonia.Styling;
using Tarui.Shell;
using Path = Avalonia.Controls.Shapes.Path;

namespace Demo;

/// <summary>
/// Applies the WinUI 3 / Win11 Fluent window chrome ported from the LYBox AvaloniaTemplate
/// (F:\Code\Dotnet\AvaloniaTemplate) — built entirely with Avalonia.Markup.Declarative in C#,
/// no AXAML. Includes: a 48px drawn title bar, caption buttons (minimize / maximize-restore /
/// close) with WinUI-style branches (close-button hover red, restore-glyph swap when maximized,
/// inactive-state fading), a 1px frame that collapses when maximized, Mica transparency with a
/// theme-matched fallback, and a title-bar theme toggle (light / dark) like the template's
/// ThemeToggleButton.
/// </summary>
public sealed class DemoWindowChromeExtension : IShellWindowExtension
{
    private const double TitleBarHeight = 48;
    private const double CaptionButtonWidth = 46;
    private const double GlyphSize = 16;

    // 亮/暗双主题调色板（模板 FluentDesign/Light.axaml 与 Dark.axaml 的实际色值）
    private sealed record ChromePalette(
        Color TitleBar,
        Color Border,
        Color Text,
        Color InactiveTitleBar,
        Color InactiveBorder,
        Color Hover,
        Color Pressed,
        Color CloseHover,
        Color ClosePressed,
        Color CloseGlyph);

    private static readonly ChromePalette LightPalette = new(
        TitleBar: Color.Parse("#F3F3F3"),
        Border: Color.Parse("#E5E5E5"),
        Text: Color.Parse("#3D3D3D"),
        InactiveTitleBar: Color.Parse("#F9F9F9"),
        InactiveBorder: Color.Parse("#EAEAEA"),
        Hover: Color.Parse("#0A000000"),
        Pressed: Color.Parse("#0F000000"),
        CloseHover: Color.Parse("#A4262A"),
        ClosePressed: Color.Parse("#8A1A1E"),
        CloseGlyph: Color.FromUInt32(0xFFFFFFFF));

    private static readonly ChromePalette DarkPalette = new(
        TitleBar: Color.Parse("#202020"),
        Border: Color.Parse("#3D3D3D"),
        Text: Color.Parse("#E6FFFFFF"),
        InactiveTitleBar: Color.Parse("#272727"),
        InactiveBorder: Color.Parse("#2E2E2E"),
        Hover: Color.Parse("#29FFFFFF"),
        Pressed: Color.Parse("#33FFFFFF"),
        CloseHover: Color.Parse("#FFBCBC"),
        ClosePressed: Color.Parse("#FFDEDE"),
        CloseGlyph: Color.FromUInt32(0xFFFFFFFF));

    // Fluent 系统图标（模板 Icons/_index.axaml 原样移植）
    private static readonly StreamGeometry GlyphMinimize = StreamGeometry.Parse(
        "M3.75 12a.75.75 0 0 1 .75-.75h15.5a.75.75 0 0 1 0 1.5H4.5a.75.75 0 0 1-.75-.75Z");
    private static readonly StreamGeometry GlyphMaximize = StreamGeometry.Parse(
        "M6.25 3A3.25 3.25 0 0 0 3 6.25v11.5A3.25 3.25 0 0 0 6.25 21h11.5A3.25 3.25 0 0 0 21 17.75V6.25C21 4.45 19.54 3 17.75 3H6.25ZM4.5 6.25c0-.97.78-1.75 1.75-1.75h11.5c.97 0 1.75.78 1.75 1.75v11.5c0 .97-.78 1.75-1.75 1.75H6.25c-.97 0-1.75-.78-1.75-1.75V6.25Z");
    private static readonly StreamGeometry GlyphRestore = StreamGeometry.Parse(
        "M4 4 L14 4 L14 6 L6 6 L6 14 L4 14 Z M8 8 L20 8 L20 20 L8 20 Z");
    private static readonly StreamGeometry GlyphClose = StreamGeometry.Parse(
        "M4.22 4.22a.75.75 0 0 1 1.06 0L12 10.94l6.72-6.72a.75.75 0 1 1 1.06 1.06L13.06 12l6.72 6.72a.75.75 0 1 1-1.06 1.06L12 13.06l-6.72 6.72a.75.75 0 0 1-1.06-1.06L10.94 12 4.22 5.28a.75.75 0 0 1 0-1.06Z");

    // 主题切换图标（Fluent 风格的太阳/月亮）
    private static readonly StreamGeometry GlyphLightMode = StreamGeometry.Parse(
        "M12 6.5a5.5 5.5 0 1 0 0 11 5.5 5.5 0 0 0 0-11Zm0 9.5a4 4 0 1 1 0-8 4 4 0 0 1 0 8Zm0-13.25a.75.75 0 0 1 .75.75v1a.75.75 0 0 1-1.5 0V3a.75.75 0 0 1 .75-.75Zm0 14.25a.75.75 0 0 1 .75.75v1a.75.75 0 0 1-1.5 0v-1a.75.75 0 0 1 .75-.75Zm-8.75-6.75a.75.75 0 0 1 0-1.5h1a.75.75 0 0 1 0 1.5h-1Zm16.5 0a.75.75 0 0 1 0-1.5h1a.75.75 0 0 1 0 1.5h-1Zm-13.3-5.66a.75.75 0 0 1 1.06 0l.7.71a.75.75 0 0 1-1.06 1.06l-.7-.7a.75.75 0 0 1 0-1.07Zm9.54 9.54a.75.75 0 0 1 1.06 0l.7.7a.75.75 0 0 1-1.06 1.07l-.7-.7a.75.75 0 0 1 0-1.07Zm-8.48 0a.75.75 0 0 1 0 1.07l-.7.7a.75.75 0 0 1-1.07-1.06l.71-.7a.75.75 0 0 1 1.06 0Zm9.54-9.54a.75.75 0 0 1 0 1.07l-.7.7a.75.75 0 0 1-1.07-1.06l.71-.7a.75.75 0 0 1 1.06 0Z");
    private static readonly StreamGeometry GlyphDarkMode = StreamGeometry.Parse(
        "M21.75 14.29a8.75 8.75 0 1 1-12.04-12.04.75.75 0 0 1 .95 1 7.25 7.25 0 0 0 10.09 10.09.75.75 0 0 1 1 .95Z");

    private readonly Dictionary<string, object> _state = new();
    private ChromePalette _palette = LightPalette;
    private bool _inactive;
    private bool _closeHover;

    // 标题按钮专用模板：仅一个居中的 ContentPresenter，保证图标在 46x48 内严格居中。
    private static readonly FuncControlTemplate<Button> CaptionButtonTemplate =
        new(static (button, _) => new ContentPresenter
        {
            Name = "PART_ContentPresenter",
            [~ContentPresenter.ContentProperty] = button[!Button.ContentProperty],
            [~ContentPresenter.BackgroundProperty] = button[!Button.BackgroundProperty],
            [~ContentPresenter.ForegroundProperty] = button[!Button.ForegroundProperty],
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0),
        });

    public void CreateView(WindowExtensionContext context)
    {
        var window = context.Window;

        // 初始主题：跟随应用实际主题（默认亮色）。
        _palette = Application.Current?.ActualThemeVariant == ThemeVariant.Dark ? DarkPalette : LightPalette;

        // 自绘窗体：扩展客户区到装饰区 + 由托管的 drawn decorations 接管标题栏。
        window.WindowDecorationsTheme = BuildDecorationsTheme(window);
        window.ExtendClientAreaToDecorationsHint = true;
        window.ExtendClientAreaTitleBarHeightHint = TitleBarHeight;
        window.TransparencyLevelHint = [WindowTransparencyLevel.Mica];
        window.Background = Brushes.Transparent;
        window.TransparencyBackgroundFallback = new SolidColorBrush(_palette.TitleBar);

        // 顶部预留标题栏高度，webview 从标题栏下方开始（对应模板 NavigationView 顶部连续条）。
        context.Composition.Chrome.Children.Add(
            new Border().Height(TitleBarHeight).Background(Brushes.Transparent));

        WireWindowState(window);

        // 主题切换：跟随应用级 RequestedThemeVariant 变化。切换后 MainWindowLauncher 会
        // 自动向所有窗口广播 shell://theme-changed，前端订阅该事件联动页面主题。
        if (Application.Current is { } app)
        {
            app.ActualThemeVariantChanged += (_, _) =>
            {
                _palette = app.ActualThemeVariant == ThemeVariant.Dark ? DarkPalette : LightPalette;
                window.TransparencyBackgroundFallback = new SolidColorBrush(_palette.TitleBar);
                ApplyState(window, inactive: _inactive);
            };
        }
    }

    /// <summary>构建 <see cref="WindowDrawnDecorations"/> 的控件主题：底衬（边框+标题栏）与叠层（标题+按钮）。</summary>
    private ControlTheme BuildDecorationsTheme(Window window)
    {
        var titleText = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(_palette.Text),
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Text = window.Title,
        };
        var titleTextPanel = new Panel
        {
            Height = TitleBarHeight,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
        }.Children(titleText);

        var minimizePath = CreateGlyphPath(GlyphMinimize);
        var maximizePath = CreateGlyphPath(GlyphMaximize);
        var closePath = CreateGlyphPath(GlyphClose);
        var themePath = CreateGlyphPath(GlyphLightMode);

        var minimizeButton = CreateCaptionButton(minimizePath, isClose: false,
            () => window.WindowState = WindowState.Minimized);
        var maximizeButton = CreateCaptionButton(maximizePath, isClose: false,
            () => window.WindowState = window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized);
        var closeButton = CreateCaptionButton(closePath, isClose: true, window.Close);

        // 主题切换按钮（对应模板 MainWindow.RightContent 的 ThemeToggleButton）：
        // 亮色显示月亮（点击切暗），暗色显示太阳（点击切亮）。
        var themeButton = CreateCaptionButton(themePath, isClose: false,
            () => Application.Current!.RequestedThemeVariant =
                Application.Current.ActualThemeVariant == ThemeVariant.Dark
                    ? ThemeVariant.Light
                    : ThemeVariant.Dark);

        var overlayPanel = new StackPanel
        {
            Height = TitleBarHeight,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Orientation = Orientation.Horizontal,
        }.Children(themeButton, minimizeButton, maximizeButton, closeButton);

        var overlay = new Panel().Children(titleTextPanel, overlayPanel);

        var windowBorder = new Border
        {
            Background = new SolidColorBrush(_palette.TitleBar),
            BorderBrush = new SolidColorBrush(_palette.Border),
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
        };
        var titleBar = new Panel
        {
            Height = TitleBarHeight,
            VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(_palette.TitleBar),
        };
        titleBar[WindowDecorationProperties.ElementRoleProperty] =
            WindowDecorationsElementRole.TitleBar;
        var underlay = new Panel().Children(windowBorder, titleBar);

        var content = new WindowDrawnDecorationsContent
        {
            Underlay = underlay,
            Overlay = overlay,
        };

        // 状态引用交给状态联动使用。
        _state["border"] = windowBorder;
        _state["titleBar"] = titleBar;
        _state["titleText"] = titleText;
        _state["minimizePath"] = minimizePath;
        _state["maximizePath"] = maximizePath;
        _state["closePath"] = closePath;
        _state["themePath"] = themePath;
        _state["minimizeButton"] = minimizeButton;
        _state["maximizeButton"] = maximizeButton;
        _state["closeButton"] = closeButton;
        _state["themeButton"] = themeButton;

        return new ControlTheme(typeof(WindowDrawnDecorations))
        {
            Setters =
            {
                new Setter(WindowDrawnDecorations.TemplateProperty, new DelegateDecorationsTemplate(content)),
                new Setter(WindowDrawnDecorations.DefaultTitleBarHeightProperty, TitleBarHeight),
                new Setter(WindowDrawnDecorations.DefaultFrameThicknessProperty, new Thickness(1)),
                new Setter(WindowDrawnDecorations.DefaultShadowThicknessProperty, new Thickness(8)),
            },
        };
    }

    private Path CreateGlyphPath(StreamGeometry data) => new()
    {
        Data = data,
        Stretch = Stretch.None,
        Fill = new SolidColorBrush(_palette.Text),
    };

    /// <summary>用 Viewbox 统一缩放并居中字形，避免不同几何体（尤其是横向细条的最小化图标）
    /// 在固定 10×10 里被 Stretch 处理得上下不居中。</summary>
    private static Viewbox CreateGlyphHost(Path glyph) => new()
    {
        Width = GlyphSize,
        Height = GlyphSize,
        Child = glyph,
    };

    private Button CreateCaptionButton(
        Path glyph,
        bool isClose,
        Action onClick)
    {
        var button = new Button
        {
            Content = CreateGlyphHost(glyph),
            Template = CaptionButtonTemplate,
            Width = CaptionButtonWidth,
            Height = TitleBarHeight,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(_palette.Text),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        button.Click += (_, _) => onClick();

        // WinUI 3 caption button 悬停/按下分支（关闭按钮变红、前景变白）。
        button.PointerEntered += (_, _) =>
        {
            _closeHover = isClose;
            button.Background = new SolidColorBrush(isClose ? _palette.CloseHover : _palette.Hover);
            if (isClose)
            {
                glyph.Fill = new SolidColorBrush(_palette.CloseGlyph);
            }
        };
        button.PointerExited += (_, _) =>
        {
            _closeHover = false;
            button.Background = Brushes.Transparent;
            if (isClose)
            {
                glyph.Fill = new SolidColorBrush(_palette.Text);
            }
        };
        button.PointerPressed += (_, _) =>
        {
            button.Background = new SolidColorBrush(isClose ? _palette.ClosePressed : _palette.Pressed);
        };
        button.PointerReleased += (_, _) =>
        {
            button.Background = new SolidColorBrush(isClose ? _palette.CloseHover : _palette.Hover);
        };
        return button;
    }

    /// <summary>最大化/失焦/主题状态联动：边框归零、还原图标、失焦变淡、按钮颜色刷新。</summary>
    private void WireWindowState(Window window)
    {
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Window.WindowStateProperty)
            {
                ApplyState(window, inactive: _inactive);
            }
            else if (e.Property == Window.TitleProperty
                     && _state.TryGetValue("titleText", out var titleValue)
                     && titleValue is TextBlock titleText)
            {
                titleText.Text = window.Title;
            }
        };
        window.Activated += (_, _) => { _inactive = false; ApplyState(window, inactive: false); };
        window.Deactivated += (_, _) => { _inactive = true; ApplyState(window, inactive: true); };
        ApplyState(window, inactive: false);
    }

    private void ApplyState(Window window, bool? inactive = null)
    {
        if (!_state.TryGetValue("border", out var borderValue) || borderValue is not Border border)
        {
            return;
        }

        var maximized = window.WindowState == WindowState.Maximized
            || window.WindowState == WindowState.FullScreen;
        var isInactive = inactive ?? _inactive;
        var palette = _palette;

        border.BorderThickness = maximized ? new Thickness(0) : new Thickness(1);
        border.Background = new SolidColorBrush(isInactive ? palette.InactiveTitleBar : palette.TitleBar);
        border.BorderBrush = new SolidColorBrush(isInactive ? palette.InactiveBorder : palette.Border);
        if (_state.TryGetValue("titleBar", out var titleBarValue) && titleBarValue is Panel titleBar)
        {
            titleBar.Background = new SolidColorBrush(isInactive ? palette.InactiveTitleBar : palette.TitleBar);
        }

        var foreground = isInactive
            ? new SolidColorBrush(palette.Text, 0.5)
            : new SolidColorBrush(palette.Text);
        if (_state.TryGetValue("titleText", out var titleTextValue) && titleTextValue is TextBlock titleText)
        {
            titleText.Foreground = foreground;
        }

        foreach (var key in new[] { "minimizeButton", "maximizeButton", "closeButton", "themeButton" })
        {
            if (_state.TryGetValue(key, out var buttonValue) && buttonValue is Button button)
            {
                button.Foreground = foreground;
            }
        }

        if (_state.TryGetValue("maximizePath", out var pathValue) && pathValue is Path maximizePath)
        {
            maximizePath.Data = maximized ? GlyphRestore : GlyphMaximize;
        }

        // 主题图标：当前亮色显示月亮，当前暗色显示太阳。
        if (_state.TryGetValue("themePath", out var themePathValue) && themePathValue is Path themePath)
        {
            themePath.Data = palette == DarkPalette ? GlyphLightMode : GlyphDarkMode;
        }

        // 所有图标颜色跟随主题（关闭按钮悬停时保持白字）。
        var glyphFill = new SolidColorBrush(_closeHover ? palette.CloseGlyph : palette.Text);
        foreach (var key in new[] { "minimizePath", "maximizePath", "themePath", "closePath" })
        {
            if (_state.TryGetValue(key, out var glyphValue) && glyphValue is Path glyphPath)
            {
                glyphPath.Fill = glyphFill;
            }
        }
    }

    /// <summary>12.1.1 中 <c>WindowDrawnDecorationsTemplate</c> 是内部类型，代码路径需自行实现接口。</summary>
    private sealed class DelegateDecorationsTemplate(WindowDrawnDecorationsContent content)
        : IWindowDrawnDecorationsTemplate
    {
        public TemplateResult<WindowDrawnDecorationsContent> Build() => new(content, new NameScope());

        object ITemplate.Build() => Build();
    }
}
