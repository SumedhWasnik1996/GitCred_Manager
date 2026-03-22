HOW TO USE CUSTOM ICONS
========================

1. APP ICON (title bar + taskbar + tray)
   Replace: Resources/Icons/app.ico
   Format:  .ico file (multi-size: 16x16, 32x32, 48x48, 256x256)
   After replacing, in GitCredMan.App.csproj set:
     <ApplicationIcon>Resources\Icons\app.ico</ApplicationIcon>

2. NAV BAR ICONS (Accounts, Repos, Apply, Theme buttons)
   Place PNG or SVG files here:
     Resources/Icons/nav_accounts.png   (32x32 recommended)
     Resources/Icons/nav_repos.png
     Resources/Icons/nav_apply.png
     Resources/Icons/nav_theme_dark.png
     Resources/Icons/nav_theme_light.png

   Then in MainWindow.xaml replace the emoji TextBlocks:
     <TextBlock Text="🔑" .../>
   With an Image element:
     <Image Source="/Resources/Icons/nav_accounts.png" Width="20" Height="20"/>

   Or use a Path element if you have SVG path data for crisp vector icons.

3. QUICK WAY — Use Segoe Fluent Icons (built into Windows 11)
   In SharedStyles.xaml or inline, use:
     <TextBlock FontFamily="Segoe Fluent Icons" Text="&#xE8D4;"/>
   Icon codes: https://docs.microsoft.com/en-us/windows/apps/design/style/segoe-fluent-icons-font

ICON SOURCES
============
- Free: https://icons8.com   https://feathericons.com   https://lucide.dev
- Segoe MDL2 Assets (built into Windows 10+)
- Segoe Fluent Icons (built into Windows 11)
