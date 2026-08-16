using Avalonia;
using Avalonia.Media;
using System;

namespace ShowWrite
{
    public enum ThemeType
    {
        Dark,
        Light,
        LightMinimal,
        NoBackground
    }

    public class ThemeColors
    {
        public string Name { get; set; } = "";
        public Color Background { get; set; }
        public Color Surface { get; set; }
        public Color SurfaceVariant { get; set; }
        public Color Primary { get; set; }
        public Color PrimaryHover { get; set; }
        public Color TextPrimary { get; set; }
        public Color TextSecondary { get; set; }
        public Color TextTertiary { get; set; }
        public Color Border { get; set; }
        public Color HoverBackground { get; set; }
        public Color PressedBackground { get; set; }
        public Color Danger { get; set; }
        public Color DangerHover { get; set; }
        public Color IconColor { get; set; }
        public Color IconColorChecked { get; set; }
        public Color VideoBackground { get; set; }
        public bool ShowButtonText { get; set; } = true;
        public double IconSize { get; set; } = 28;
        public double ButtonSize { get; set; } = 56;
        public double ButtonGroupCornerRadius { get; set; } = 12;
        public bool ShowButtonShadow { get; set; } = false;
        public double SliderIndicatorSize { get; set; } = 56;
        public double SliderIndicatorCornerRadius { get; set; } = 8;
        public double ButtonCornerRadius { get; set; } = 8;
        public double ButtonPadding { get; set; } = 4;
        public double ButtonGroupPadding { get; set; } = 8;
        public bool ShowButtonGroupBackground { get; set; } = true;
    }

    public static class ThemeManager
    {
        public static readonly ThemeColors DarkTheme = new ThemeColors
        {
            Name = "深色",
            Background = Color.Parse("#1E1E1E"),
            Surface = Color.Parse("#2D2D2D"),
            SurfaceVariant = Color.Parse("#3D3D3D"),
            Primary = Color.Parse("#0078D4"),
            PrimaryHover = Color.Parse("#1E90FF"),
            TextPrimary = Colors.White,
            TextSecondary = Color.Parse("#CCCCCC"),
            TextTertiary = Color.Parse("#888888"),
            Border = Color.Parse("#555555"),
            HoverBackground = Color.Parse("#3D3D3D"),
            PressedBackground = Color.Parse("#555555"),
            Danger = Color.Parse("#E53935"),
            DangerHover = Color.Parse("#EF5350"),
            IconColor = Colors.White,
            IconColorChecked = Colors.White,
            VideoBackground = Colors.Black,
            ShowButtonText = true,
            IconSize = 28,
            ButtonSize = 56,
            ButtonGroupCornerRadius = 12,
            ShowButtonShadow = false
        };

        public static readonly ThemeColors LightTheme = new ThemeColors
        {
            Name = "浅色",
            Background = Color.Parse("#F5F5F5"),
            Surface = Color.Parse("#FFFFFF"),
            SurfaceVariant = Color.Parse("#E8E8E8"),
            Primary = Color.Parse("#0078D4"),
            PrimaryHover = Color.Parse("#1E90FF"),
            TextPrimary = Color.Parse("#1E1E1E"),
            TextSecondary = Color.Parse("#555555"),
            TextTertiary = Color.Parse("#888888"),
            Border = Color.Parse("#CCCCCC"),
            HoverBackground = Color.Parse("#E8E8E8"),
            PressedBackground = Color.Parse("#D0D0D0"),
            Danger = Color.Parse("#E53935"),
            DangerHover = Color.Parse("#EF5350"),
            IconColor = Color.Parse("#1E1E1E"),
            IconColorChecked = Colors.White,
            VideoBackground = Colors.White,
            ShowButtonText = true,
            IconSize = 28,
            ButtonSize = 56,
            ButtonGroupCornerRadius = 12,
            ShowButtonShadow = false
        };

        public static readonly ThemeColors LightMinimalTheme = new ThemeColors
        {
            Name = "白色-简约",
            Background = Color.Parse("#F5F5F5"),
            Surface = Color.Parse("#FFFFFF"),
            SurfaceVariant = Color.Parse("#E8E8E8"),
            Primary = Color.Parse("#0078D4"),
            PrimaryHover = Color.Parse("#1E90FF"),
            TextPrimary = Color.Parse("#1E1E1E"),
            TextSecondary = Color.Parse("#555555"),
            TextTertiary = Color.Parse("#888888"),
            Border = Color.Parse("#CCCCCC"),
            HoverBackground = Color.Parse("#E8E8E8"),
            PressedBackground = Color.Parse("#D0D0D0"),
            Danger = Color.Parse("#E53935"),
            DangerHover = Color.Parse("#EF5350"),
            IconColor = Color.Parse("#1E1E1E"),
            IconColorChecked = Colors.White,
            VideoBackground = Colors.White,
            ShowButtonText = false,
            IconSize = 26,
            ButtonSize = 44,
            ButtonGroupCornerRadius = 22,
            ShowButtonShadow = true,
            SliderIndicatorSize = 44,
            SliderIndicatorCornerRadius = 22,
            ButtonCornerRadius = 22,
            ButtonPadding = 2,
            ButtonGroupPadding = 12
        };

        public static readonly ThemeColors NoBackgroundTheme = new ThemeColors
        {
            Name = "我眼不瞎",
            Background = Color.Parse("#F5F5F5"),
            Surface = Color.Parse("#00000000"),
            SurfaceVariant = Color.Parse("#00000000"),
            Primary = Color.Parse("#00000000"),
            PrimaryHover = Color.Parse("#00000000"),
            TextPrimary = Color.Parse("#1E1E1E"),
            TextSecondary = Color.Parse("#555555"),
            TextTertiary = Color.Parse("#888888"),
            Border = Color.Parse("#00000000"),
            HoverBackground = Color.Parse("#00000000"),
            PressedBackground = Color.Parse("#00000000"),
            Danger = Color.Parse("#E53935"),
            DangerHover = Color.Parse("#EF5350"),
            IconColor = Color.Parse("#808080"),
            IconColorChecked = Color.Parse("#808080"),
            VideoBackground = Colors.White,
            ShowButtonText = false,
            IconSize = 28,
            ButtonSize = 56,
            ButtonGroupCornerRadius = 0,
            ShowButtonShadow = true,
            SliderIndicatorSize = 44,
            SliderIndicatorCornerRadius = 22,
            ButtonCornerRadius = 8,
            ButtonPadding = 4,
            ButtonGroupPadding = 0,
            ShowButtonGroupBackground = false
        };

        private static ThemeType _currentTheme = ThemeType.Dark;
        public static ThemeType CurrentTheme => _currentTheme;
        public static ThemeColors CurrentColors => _currentTheme switch
        {
            ThemeType.Light => LightTheme,
            ThemeType.LightMinimal => LightMinimalTheme,
            ThemeType.NoBackground => NoBackgroundTheme,
            _ => DarkTheme
        };

        public static event Action<ThemeType>? ThemeChanged;

        public static void SetTheme(ThemeType theme)
        {
            if (_currentTheme == theme) return;

            _currentTheme = theme;
            ThemeChanged?.Invoke(theme);
        }

        public static void ApplyTheme(Application app, ThemeType theme)
        {
            _currentTheme = theme;

            var colors = CurrentColors;

            app.Resources["ThemeBackground"] = new SolidColorBrush(colors.Background);
            app.Resources["ThemeSurface"] = new SolidColorBrush(colors.Surface);
            app.Resources["ThemeSurfaceVariant"] = new SolidColorBrush(colors.SurfaceVariant);
            app.Resources["ThemePrimary"] = new SolidColorBrush(colors.Primary);
            app.Resources["ThemePrimaryHover"] = new SolidColorBrush(colors.PrimaryHover);
            app.Resources["ThemeTextPrimary"] = new SolidColorBrush(colors.TextPrimary);
            app.Resources["ThemeTextSecondary"] = new SolidColorBrush(colors.TextSecondary);
            app.Resources["ThemeTextTertiary"] = new SolidColorBrush(colors.TextTertiary);
            app.Resources["ThemeBorder"] = new SolidColorBrush(colors.Border);
            app.Resources["ThemeHoverBackground"] = new SolidColorBrush(colors.HoverBackground);
            app.Resources["ThemePressedBackground"] = new SolidColorBrush(colors.PressedBackground);
            app.Resources["ThemeDanger"] = new SolidColorBrush(colors.Danger);
            app.Resources["ThemeDangerHover"] = new SolidColorBrush(colors.DangerHover);
            app.Resources["ThemeIconColor"] = new SolidColorBrush(colors.IconColor);
            app.Resources["ThemeIconColorChecked"] = new SolidColorBrush(colors.IconColorChecked);
            app.Resources["ThemeVideoBackground"] = new SolidColorBrush(colors.VideoBackground);
            app.Resources["ThemeShowButtonText"] = colors.ShowButtonText;
            app.Resources["ThemeIconSize"] = colors.IconSize;
            app.Resources["ThemeButtonSize"] = colors.ButtonSize;
            app.Resources["ThemeButtonGroupCornerRadius"] = new CornerRadius(colors.ButtonGroupCornerRadius);
            app.Resources["ThemeShowButtonShadow"] = colors.ShowButtonShadow;
            app.Resources["ThemeSliderIndicatorSize"] = colors.SliderIndicatorSize;
            app.Resources["ThemeSliderIndicatorCornerRadius"] = new CornerRadius(colors.SliderIndicatorCornerRadius);
            app.Resources["ThemeButtonCornerRadius"] = new CornerRadius(colors.ButtonCornerRadius);
            app.Resources["ThemeButtonPadding"] = new Thickness(colors.ButtonPadding);
            app.Resources["ThemeButtonGroupPadding"] = new Thickness(colors.ButtonGroupPadding, 4, colors.ButtonGroupPadding, 4);
            app.Resources["ThemeShowButtonGroupBackground"] = colors.ShowButtonGroupBackground;

            ApplyFluentButtonResources(app, colors);
        }

        private static void ApplyFluentButtonResources(Application app, ThemeColors colors)
        {
            var hoverBrush = new SolidColorBrush(colors.HoverBackground);
            var pressedBrush = new SolidColorBrush(colors.PressedBackground);

            app.Resources["ButtonBackgroundPointerOver"] = hoverBrush;
            app.Resources["ButtonBackgroundPressed"] = pressedBrush;
            app.Resources["ToggleButtonBackgroundPointerOver"] = hoverBrush;
            app.Resources["ToggleButtonBackgroundPressed"] = pressedBrush;
            app.Resources["RepeatButtonBackgroundPointerOver"] = hoverBrush;
            app.Resources["RepeatButtonBackgroundPressed"] = pressedBrush;
        }

        public static void InitializeTheme(Application app)
        {
            app.Resources["ThemeBackground"] = new SolidColorBrush(DarkTheme.Background);
            app.Resources["ThemeSurface"] = new SolidColorBrush(DarkTheme.Surface);
            app.Resources["ThemeSurfaceVariant"] = new SolidColorBrush(DarkTheme.SurfaceVariant);
            app.Resources["ThemePrimary"] = new SolidColorBrush(DarkTheme.Primary);
            app.Resources["ThemePrimaryHover"] = new SolidColorBrush(DarkTheme.PrimaryHover);
            app.Resources["ThemeTextPrimary"] = new SolidColorBrush(DarkTheme.TextPrimary);
            app.Resources["ThemeTextSecondary"] = new SolidColorBrush(DarkTheme.TextSecondary);
            app.Resources["ThemeTextTertiary"] = new SolidColorBrush(DarkTheme.TextTertiary);
            app.Resources["ThemeBorder"] = new SolidColorBrush(DarkTheme.Border);
            app.Resources["ThemeHoverBackground"] = new SolidColorBrush(DarkTheme.HoverBackground);
            app.Resources["ThemePressedBackground"] = new SolidColorBrush(DarkTheme.PressedBackground);
            app.Resources["ThemeDanger"] = new SolidColorBrush(DarkTheme.Danger);
            app.Resources["ThemeDangerHover"] = new SolidColorBrush(DarkTheme.DangerHover);
            app.Resources["ThemeIconColor"] = new SolidColorBrush(DarkTheme.IconColor);
            app.Resources["ThemeIconColorChecked"] = new SolidColorBrush(DarkTheme.IconColorChecked);
            app.Resources["ThemeVideoBackground"] = new SolidColorBrush(DarkTheme.VideoBackground);
            app.Resources["ThemeShowButtonText"] = DarkTheme.ShowButtonText;
            app.Resources["ThemeIconSize"] = DarkTheme.IconSize;
            app.Resources["ThemeButtonSize"] = DarkTheme.ButtonSize;
            app.Resources["ThemeButtonGroupCornerRadius"] = new CornerRadius(DarkTheme.ButtonGroupCornerRadius);
            app.Resources["ThemeShowButtonShadow"] = DarkTheme.ShowButtonShadow;
            app.Resources["ThemeSliderIndicatorSize"] = DarkTheme.SliderIndicatorSize;
            app.Resources["ThemeSliderIndicatorCornerRadius"] = new CornerRadius(DarkTheme.SliderIndicatorCornerRadius);
            app.Resources["ThemeButtonCornerRadius"] = new CornerRadius(DarkTheme.ButtonCornerRadius);
            app.Resources["ThemeButtonPadding"] = new Thickness(DarkTheme.ButtonPadding);
            app.Resources["ThemeButtonGroupPadding"] = new Thickness(DarkTheme.ButtonGroupPadding, 4, DarkTheme.ButtonGroupPadding, 4);
            app.Resources["ThemeShowButtonGroupBackground"] = DarkTheme.ShowButtonGroupBackground;

            ApplyFluentButtonResources(app, DarkTheme);
        }
    }
}
