using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace YabiDesktopPet
{
    internal sealed partial class MainWindow : Window
    {
        private const double BaseDisplayScale = 0.64;
        private const int HotkeyToggleVisibility = 1;
        private const int HotkeyToggleClickThrough = 2;
        private const int HotkeyExit = 3;
        private const uint ModifierAlt = 0x0001;
        private const uint ModifierControl = 0x0002;
        private const int GwlExStyle = -20;
        private const int WsExTransparent = 0x00000020;
        private const int WmHotkey = 0x0312;

        private readonly Random _random;
        private readonly PetSettings _settings;
        private readonly PetStateController _controller;
        private readonly AnimationCatalog _catalog;
        private readonly CareController _careController;
        private readonly ReminderController _reminderController;
        private readonly DesktopIntegrationService _desktopIntegration;
        private readonly Grid _visualRoot;
        private readonly Image _imageA;
        private readonly Image _imageB;
        private readonly DispatcherTimer _frameTimer;
        private readonly DispatcherTimer _companionTimer;
        private readonly DispatcherTimer _mouseLookTimer;
        private readonly DispatcherTimer _careTimer;
        private readonly DispatcherTimer _singleClickTimer;
        private readonly Dictionary<PetAction, MenuItem> _actionMenuItems;
        private readonly Dictionary<BehaviorMode, MenuItem> _behaviorMenuItems;
        private readonly Dictionary<double, MenuItem> _sizeMenuItems;
        private readonly Dictionary<double, MenuItem> _opacityMenuItems;
        private readonly bool _testUi;
        private readonly bool _testFeatures;
        private readonly bool _testNatural;

        private Image _activeImage;
        private BitmapSource _currentBitmap;
        private PetPose _stablePose;
        private PetAction _currentAction;
        private PetCommand _activeCommand;
        private Nullable<PetAction> _queuedManualAction;
        private string _playingClip;
        private int _playingFrame;
        private int _playingDirection;
        private int _playbackToken;
        private bool _framePending;
        private bool _clipPlaying;
        private bool _isCrossfading;
        private bool _isDragging;
        private bool _isClosing;
        private bool _clickThrough;
        private bool _manualMirror;
        private bool _mouseLookEnabled;
        private double _sizeMultiplier;
        private double _petOpacity;
        private Action _playbackCompleted;
        private int _lookRequestToken;
        private Drawing.Point _lastCursorPixels;
        private DateTime _lastCursorMovedUtc;
        private readonly CareRuntimeClock _careClock = new CareRuntimeClock();

        private readonly MenuItem _mouseLookMenuItem;
        private readonly MenuItem _clickThroughMenuItem;
        private readonly MenuItem _mirrorMenuItem;
        private readonly MenuItem _lockPositionMenuItem;
        private readonly MenuItem _edgeSnapMenuItem;
        private readonly MenuItem _alwaysOnTopMenuItem;
        private readonly MenuItem _focusMenuItem;
        private readonly TextBlock _menuStatusText;
        private SpeechBubbleWindow _speechBubble;
        private StatusCardWindow _statusCard;
        private SettingsWindow _settingsWindow;
        private Forms.NotifyIcon _trayIcon;
        private Forms.ToolStripMenuItem _trayShowItem;
        private Forms.ToolStripMenuItem _trayClickThroughItem;
        private Forms.ToolStripMenuItem _trayMouseLookItem;
        private readonly Dictionary<BehaviorMode, Forms.ToolStripMenuItem> _trayBehaviorItems;
        private HwndSource _windowSource;

        public MainWindow(bool testUi, bool testCycle, bool testFeatures, bool testIdle, bool testNatural = false, bool testGaze = false)
        {
            _testUi = testUi;
            _testFeatures = testFeatures;
            _testNatural = testNatural || testGaze;
            _testGaze = testGaze;
            _random = new Random();
            _settings = testUi ? new PetSettings() : PetSettings.Load();
            _manualMirror = _settings.ManualMirror;
            _mouseLookEnabled = _settings.MouseLookEnabled;
            _sizeMultiplier = _settings.Scale;
            _petOpacity = _settings.Opacity;
            if (testCycle)
            {
                _settings.Behavior = BehaviorMode.Companion;
                _mouseLookEnabled = true;
            }
            else if (testIdle)
            {
                _settings.Behavior = BehaviorMode.Quiet;
            }

            _controller = new PetStateController(_random, testCycle ? 0.08 : 1.0);
            _controller.SetFrequency(_settings.InteractionFrequency, DateTime.UtcNow);
            _controller.Start(DateTime.UtcNow, _settings.Behavior);
            _careController = new CareController(testUi ? new PetProfile() : PetProfile.Load(), !testUi);
            _reminderController = new ReminderController(_settings);
            _reminderController.ReminderDue += ReminderDue;
            _desktopIntegration = new DesktopIntegrationService();
            _stablePose = PetPose.Standing;
            _currentAction = PetAction.Stand;
            _activeCommand = PetCommand.None;
            _lastCursorPixels = Forms.Control.MousePosition;
            _lastCursorMovedUtc = DateTime.UtcNow;

            Title = "亚比桌宠 4.3.4 · 养成优化版";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = testUi;
            Topmost = _settings.AlwaysOnTop;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            Opacity = _petOpacity;

            _catalog = new AnimationCatalog(Dispatcher);
            _visualRoot = new Grid();
            _visualRoot.Background = Brushes.Transparent;
            _visualRoot.RenderTransformOrigin = new Point(0.5, 0.5);
            _visualRoot.ToolTip = "亚比：左键拖动，双击切换坐姿，右键打开菜单";
            _imageA = CreatePetImage();
            _imageB = CreatePetImage();
            _activeImage = _imageA;
            _imageA.Opacity = 1.0;
            _imageB.Opacity = 0.0;
            Panel.SetZIndex(_imageA, 1);
            Panel.SetZIndex(_imageB, 0);
            _visualRoot.Children.Add(_imageA);
            _visualRoot.Children.Add(_imageB);
            Content = _visualRoot;

            BitmapSource firstFrame = _catalog.LoadResidentFrame("stand_idle", 0);
            _currentBitmap = firstFrame;
            _activeImage.Source = firstFrame;
            Width = firstFrame.PixelWidth * BaseDisplayScale * _sizeMultiplier;
            Height = firstFrame.PixelHeight * BaseDisplayScale * _sizeMultiplier;
            ApplyMirror();

            ContextMenu menu = new ContextMenu();
            ApplyContextMenuTheme(menu);
            _menuStatusText = new TextBlock
            {
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(137, 111, 96)),
                Margin = new Thickness(0, 2, 0, 0)
            };
            menu.Items.Add(CreateBrandMenuItem(_menuStatusText));
            menu.Items.Add(new Separator());

            MenuItem actionRoot = CreateStyledMenuItem("动作", "✦");
            _actionMenuItems = new Dictionary<PetAction, MenuItem>();
            AddActionMenuItem(actionRoot, PetAction.Stand, "站立");
            AddActionMenuItem(actionRoot, PetAction.Blink, "眨眼");
            AddActionMenuItem(actionRoot, PetAction.Sit, "坐下");
            AddActionMenuItem(actionRoot, PetAction.LookAround, "环顾");
            AddActionMenuItem(actionRoot, PetAction.Sleep, "睡觉");
            AddActionMenuItem(actionRoot, PetAction.Wake, "唤醒");
            MenuItem libraryItem = CreateStyledMenuItem("更多动作…", "▦");
            libraryItem.Click += delegate { ShowActionLibrary(); };
            actionRoot.Items.Add(new Separator());
            actionRoot.Items.Add(libraryItem);
            menu.Items.Add(actionRoot);

            MenuItem careRoot = CreateStyledMenuItem("照顾亚比", "♥");
            MenuItem feedItem = CreateStyledMenuItem("喂食", "●");
            feedItem.Click += delegate { FeedYabi(); };
            careRoot.Items.Add(feedItem);
            MenuItem restItem = CreateStyledMenuItem("休息", "☾");
            restItem.Click += delegate { RestYabi(); };
            careRoot.Items.Add(restItem);
            MenuItem petItem = CreateStyledMenuItem("摸摸", "♡");
            petItem.Click += delegate { PetYabi(); };
            careRoot.Items.Add(petItem);
            MenuItem speakItem = CreateStyledMenuItem("和亚比说话", "…");
            speakItem.Click += delegate { SayRandomPhrase(); };
            careRoot.Items.Add(speakItem);
            menu.Items.Add(careRoot);

            MenuItem reminderRoot = CreateStyledMenuItem("提醒与专注", "◷");
            _focusMenuItem = CreateStyledMenuItem(_reminderController.GetFocusLabel(), "◎");
            _focusMenuItem.Click += delegate { ToggleFocus(); };
            reminderRoot.Items.Add(_focusMenuItem);
            MenuItem snoozeRoot = CreateStyledMenuItem("延后提醒", "＋");
            foreach (int minutes in new[] { 5, 10, 30 })
            {
                int capturedMinutes = minutes;
                MenuItem snooze = CreateStyledMenuItem(minutes + "分钟", "◷");
                snooze.Click += delegate { SnoozeFocus(capturedMinutes); };
                snoozeRoot.Items.Add(snooze);
            }
            reminderRoot.Items.Add(snoozeRoot);
            MenuItem resetFocusItem = CreateStyledMenuItem("重置专注", "↺");
            resetFocusItem.Click += delegate { ResetFocus(); };
            reminderRoot.Items.Add(resetFocusItem);
            MenuItem reminderSettingsItem = CreateStyledMenuItem("提醒设置", "⚙");
            reminderSettingsItem.Click += delegate { ShowSettings(2); };
            reminderRoot.Items.Add(reminderSettingsItem);
            menu.Items.Add(reminderRoot);

            MenuItem desktopRoot = CreateStyledMenuItem("桌面与外观", "▣");
            MenuItem behaviorRoot = CreateStyledMenuItem("陪伴模式", "✦");
            _behaviorMenuItems = new Dictionary<BehaviorMode, MenuItem>();
            AddBehaviorMenuItem(behaviorRoot, BehaviorMode.Companion, "自动陪伴");
            AddBehaviorMenuItem(behaviorRoot, BehaviorMode.Quiet, "安静睡觉");
            desktopRoot.Items.Add(behaviorRoot);

            _mouseLookMenuItem = new MenuItem
            {
                Header = "实时视线跟随（站立 / 坐姿）",
                Icon = CreateMenuIcon("◎"),
                IsCheckable = true,
                IsChecked = _mouseLookEnabled
            };
            _mouseLookMenuItem.Click += delegate { SetMouseLookEnabled(_mouseLookMenuItem.IsChecked, true); };
            MenuItem gazeRoot = CreateStyledMenuItem("鼠标注视", "◎");
            gazeRoot.Items.Add(_mouseLookMenuItem);
            MenuItem gazeStart = CreateStyledMenuItem("实时注视状态…", "▶");
            gazeStart.Click += delegate { StartAttentionTest(); };
            gazeRoot.Items.Add(gazeStart);
            _gazeMenuStatus = new TextBlock { Text = _gazeStatus, TextWrapping = TextWrapping.Wrap, MaxWidth = 232, FontSize = 11 };
            gazeRoot.Items.Add(new MenuItem { Header = _gazeMenuStatus, IsEnabled = false });
            menu.Items.Insert(3, gazeRoot);

            MenuItem appearanceRoot = CreateStyledMenuItem("外观设置", "◐");
            MenuItem sizeRoot = CreateStyledMenuItem("大小", "↕");
            _sizeMenuItems = new Dictionary<double, MenuItem>();
            AddSizeMenuItem(sizeRoot, 0.75, "小巧", "−");
            AddSizeMenuItem(sizeRoot, 1.0, "标准", "●");
            AddSizeMenuItem(sizeRoot, 1.25, "大号", "+");
            appearanceRoot.Items.Add(sizeRoot);

            MenuItem opacityRoot = CreateStyledMenuItem("透明度", "◒");
            _opacityMenuItems = new Dictionary<double, MenuItem>();
            AddOpacityMenuItem(opacityRoot, 0.60, "60%", "◔");
            AddOpacityMenuItem(opacityRoot, 0.80, "80%", "◑");
            AddOpacityMenuItem(opacityRoot, 1.0, "100%", "●");
            appearanceRoot.Items.Add(opacityRoot);

            _mirrorMenuItem = new MenuItem
            {
                Header = "镜像朝向",
                Icon = CreateMenuIcon("⇄"),
                IsCheckable = true,
                IsChecked = _manualMirror
            };
            _mirrorMenuItem.Click += delegate
            {
                _manualMirror = _mirrorMenuItem.IsChecked;
                ApplyMirror();
                SaveSettings();
            };
            appearanceRoot.Items.Add(_mirrorMenuItem);
            desktopRoot.Items.Add(appearanceRoot);

            _lockPositionMenuItem = CreateStyledMenuItem("锁定位置", "⌖");
            _lockPositionMenuItem.IsCheckable = true;
            _lockPositionMenuItem.Click += delegate
            {
                _settings.LockPosition = _lockPositionMenuItem.IsChecked;
                SaveSettings();
                ShowSpeech(_settings.LockPosition ? "亚比的位置已经锁定。" : "现在可以继续拖动亚比了。" );
            };
            desktopRoot.Items.Add(_lockPositionMenuItem);

            _edgeSnapMenuItem = CreateStyledMenuItem("吸附屏幕边缘", "↔");
            _edgeSnapMenuItem.IsCheckable = true;
            _edgeSnapMenuItem.Click += delegate { _settings.EdgeSnap = _edgeSnapMenuItem.IsChecked; SaveSettings(); };
            desktopRoot.Items.Add(_edgeSnapMenuItem);

            _alwaysOnTopMenuItem = CreateStyledMenuItem("始终置顶", "↑");
            _alwaysOnTopMenuItem.IsCheckable = true;
            _alwaysOnTopMenuItem.Click += delegate
            {
                _settings.AlwaysOnTop = _alwaysOnTopMenuItem.IsChecked;
                Topmost = _settings.AlwaysOnTop;
                SaveSettings();
            };
            desktopRoot.Items.Add(_alwaysOnTopMenuItem);

            _clickThroughMenuItem = new MenuItem
            {
                Header = "鼠标点击穿透",
                Icon = CreateMenuIcon("◇"),
                IsCheckable = true,
                InputGestureText = "Ctrl+Alt+P",
                ToolTip = "开启后用 Ctrl+Alt+P 或托盘菜单恢复"
            };
            _clickThroughMenuItem.Click += delegate { SetClickThrough(_clickThroughMenuItem.IsChecked, true); };
            desktopRoot.Items.Add(_clickThroughMenuItem);
            menu.Items.Add(desktopRoot);

            menu.Items.Add(new Separator());

            MenuItem settingsItem = CreateStyledMenuItem("设置", "⚙");
            settingsItem.Click += delegate { ShowSettings(0); };
            menu.Items.Add(settingsItem);
            MenuItem hideItem = CreateStyledMenuItem("隐藏亚比", "−");
            hideItem.InputGestureText = "Ctrl+Alt+H";
            hideItem.Click += delegate { ToggleVisibility(); };
            menu.Items.Add(hideItem);
            MenuItem exitItem = CreateStyledMenuItem("退出亚比", "×");
            exitItem.InputGestureText = "Ctrl+Alt+Q";
            exitItem.Click += delegate { Close(); };
            menu.Items.Add(exitItem);
            _visualRoot.ContextMenu = menu;

            _trayBehaviorItems = new Dictionary<BehaviorMode, Forms.ToolStripMenuItem>();
            _frameTimer = new DispatcherTimer(DispatcherPriority.Render);
            _frameTimer.Tick += FrameTimerTick;
            _companionTimer = new DispatcherTimer(DispatcherPriority.Background);
            _companionTimer.Interval = TimeSpan.FromMilliseconds(250);
            _companionTimer.Tick += CompanionTimerTick;
            _mouseLookTimer = new DispatcherTimer(DispatcherPriority.Background);
            _mouseLookTimer.Interval = TimeSpan.FromMilliseconds(100);
            _mouseLookTimer.Tick += MouseLookTimerTick;
            _careTimer = new DispatcherTimer(DispatcherPriority.Background);
            _careTimer.Interval = TimeSpan.FromSeconds(1);
            _careTimer.Tick += CareTimerTick;
            _singleClickTimer = new DispatcherTimer(DispatcherPriority.Input);
            _singleClickTimer.Interval = TimeSpan.FromMilliseconds(Forms.SystemInformation.DoubleClickTime);
            _singleClickTimer.Tick += delegate { _singleClickTimer.Stop(); ShowStatusCard(); };

            MouseLeftButtonDown += WindowMouseLeftButtonDown;
            LocationChanged += delegate
            {
                if (_speechBubble != null)
                {
                    _speechBubble.PositionNear(this);
                }
            };
            Loaded += WindowLoaded;
            if (_testUi) ContentRendered += CaptureStartupEvidence;
            SourceInitialized += WindowSourceInitialized;
            Closing += WindowClosing;
            SyncMenus();
        }

        private static Image CreatePetImage()
        {
            Image image = new Image();
            image.Stretch = Stretch.Uniform;
            image.HorizontalAlignment = HorizontalAlignment.Stretch;
            image.VerticalAlignment = VerticalAlignment.Stretch;
            image.SnapsToDevicePixels = true;
            image.IsHitTestVisible = false;
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            return image;
        }

        private static readonly string ContextMenuThemeXaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style x:Key='YabiContextMenuStyle' TargetType='{x:Type ContextMenu}'>
    <Setter Property='Background' Value='#FFFCF8'/>
    <Setter Property='Foreground' Value='#3D302A'/>
    <Setter Property='BorderBrush' Value='#E6D7C8'/>
    <Setter Property='BorderThickness' Value='1'/>
    <Setter Property='Padding' Value='7'/>
    <Setter Property='FontFamily' Value='Microsoft YaHei UI'/>
    <Setter Property='FontSize' Value='13.5'/>
    <Setter Property='HasDropShadow' Value='False'/>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='{x:Type ContextMenu}'>
          <Border x:Name='Surface'
                  MinWidth='252'
                  Padding='{TemplateBinding Padding}'
                  Background='{TemplateBinding Background}'
                  BorderBrush='{TemplateBinding BorderBrush}'
                  BorderThickness='{TemplateBinding BorderThickness}'
                  CornerRadius='14'
                  SnapsToDevicePixels='True'>
            <Border.Effect>
              <DropShadowEffect BlurRadius='22' ShadowDepth='5' Direction='270' Opacity='0.24' Color='#5A3526'/>
            </Border.Effect>
            <ScrollViewer CanContentScroll='True'
                          HorizontalScrollBarVisibility='Disabled'
                          VerticalScrollBarVisibility='Auto'>
              <ItemsPresenter KeyboardNavigation.DirectionalNavigation='Cycle'/>
            </ScrollViewer>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='{x:Type MenuItem}'>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Foreground' Value='#3D302A'/>
    <Setter Property='FontFamily' Value='Microsoft YaHei UI'/>
    <Setter Property='FontSize' Value='13.5'/>
    <Setter Property='FontWeight' Value='Normal'/>
    <Setter Property='Padding' Value='10,7'/>
    <Setter Property='Margin' Value='1'/>
    <Setter Property='MinHeight' Value='38'/>
    <Setter Property='MinWidth' Value='232'/>
    <Setter Property='HorizontalContentAlignment' Value='Stretch'/>
    <Setter Property='VerticalContentAlignment' Value='Center'/>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='{x:Type MenuItem}'>
          <Grid SnapsToDevicePixels='True'>
            <Border x:Name='ItemBorder'
                    Padding='{TemplateBinding Padding}'
                    Margin='{TemplateBinding Margin}'
                    Background='{TemplateBinding Background}'
                    CornerRadius='9'>
              <Grid>
                <Grid.ColumnDefinitions>
                  <ColumnDefinition Width='28'/>
                  <ColumnDefinition Width='*'/>
                  <ColumnDefinition Width='Auto'/>
                  <ColumnDefinition Width='13'/>
                </Grid.ColumnDefinitions>
                <ContentPresenter x:Name='IconPresenter'
                                  Grid.Column='0'
                                  ContentSource='Icon'
                                  HorizontalAlignment='Center'
                                  VerticalAlignment='Center'/>
                <Border x:Name='CheckPlate'
                        Grid.Column='0'
                        Width='22'
                        Height='22'
                        HorizontalAlignment='Center'
                        VerticalAlignment='Center'
                        Background='#EBC1A7'
                        CornerRadius='7'
                        Visibility='Collapsed'>
                  <Path x:Name='CheckGlyph'
                        Width='11'
                        Height='9'
                        Data='M 1,4 L 4,7 L 10,1'
                        Stroke='#8D482C'
                        StrokeThickness='2'
                        StrokeStartLineCap='Round'
                        StrokeEndLineCap='Round'/>
                </Border>
                <ContentPresenter Grid.Column='1'
                                  Margin='8,0,10,0'
                                  ContentSource='Header'
                                  RecognizesAccessKey='True'
                                  VerticalAlignment='Center'/>
                <TextBlock Grid.Column='2'
                           Margin='14,0,8,0'
                           VerticalAlignment='Center'
                           Foreground='#9B887C'
                           FontFamily='Segoe UI'
                           FontSize='10.5'
                           Text='{TemplateBinding InputGestureText}'/>
                <Path x:Name='SubmenuArrow'
                      Grid.Column='3'
                      Width='5'
                      Height='9'
                      Margin='2,0,0,0'
                      VerticalAlignment='Center'
                      Data='M 0,0 L 4,4.5 L 0,9'
                      Stroke='#9A7865'
                      StrokeThickness='1.5'
                      StrokeStartLineCap='Round'
                      StrokeEndLineCap='Round'
                      Visibility='Collapsed'/>
              </Grid>
            </Border>
            <Popup x:Name='PART_Popup'
                   Placement='Right'
                   HorizontalOffset='5'
                   VerticalOffset='-7'
                   AllowsTransparency='True'
                   Focusable='False'
                   IsOpen='{TemplateBinding IsSubmenuOpen}'
                   PopupAnimation='Fade'>
              <Border MinWidth='232'
                      Padding='7'
                      Background='#FFFCF8'
                      BorderBrush='#E6D7C8'
                      BorderThickness='1'
                      CornerRadius='12'>
                <Border.Effect>
                  <DropShadowEffect BlurRadius='18' ShadowDepth='4' Direction='270' Opacity='0.22' Color='#5A3526'/>
                </Border.Effect>
                <ScrollViewer CanContentScroll='True'
                              HorizontalScrollBarVisibility='Disabled'
                              VerticalScrollBarVisibility='Auto'>
                  <ItemsPresenter KeyboardNavigation.DirectionalNavigation='Cycle'/>
                </ScrollViewer>
              </Border>
            </Popup>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='HasItems' Value='True'>
              <Setter TargetName='SubmenuArrow' Property='Visibility' Value='Visible'/>
            </Trigger>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='IconPresenter' Property='Visibility' Value='Collapsed'/>
              <Setter TargetName='CheckPlate' Property='Visibility' Value='Visible'/>
              <Setter TargetName='ItemBorder' Property='Background' Value='#FFF0E6'/>
              <Setter Property='FontWeight' Value='SemiBold'/>
            </Trigger>
            <Trigger Property='IsHighlighted' Value='True'>
              <Setter TargetName='ItemBorder' Property='Background' Value='#FFE5D5'/>
            </Trigger>
            <Trigger Property='IsSubmenuOpen' Value='True'>
              <Setter TargetName='ItemBorder' Property='Background' Value='#FFE5D5'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter Property='Opacity' Value='0.45'/>
            </Trigger>
            <Trigger Property='Tag' Value='YabiHeader'>
              <Setter Property='Opacity' Value='1'/>
              <Setter Property='Padding' Value='10,9'/>
              <Setter Property='Margin' Value='1,1,1,5'/>
              <Setter Property='MinHeight' Value='58'/>
              <Setter TargetName='ItemBorder' Property='Background' Value='#FFF2E9'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='{x:Type Separator}'>
    <Setter Property='Height' Value='1'/>
    <Setter Property='Margin' Value='14,6'/>
    <Setter Property='Background' Value='#EADDD2'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='{x:Type Separator}'>
          <Border Height='1' Background='{TemplateBinding Background}' SnapsToDevicePixels='True'/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>";

        private static ResourceDictionary BuildContextMenuThemeResources()
        {
            return (ResourceDictionary)XamlReader.Parse(ContextMenuThemeXaml);
        }

        private static void ApplyContextMenuTheme(ContextMenu menu)
        {
            UiTheme.Apply(menu);
            ResourceDictionary resources = BuildContextMenuThemeResources();
            menu.Resources = resources;
            menu.Style = (Style)resources["YabiContextMenuStyle"];
            menu.Opened += delegate
            {
                menu.BeginAnimation(
                    OpacityProperty,
                    new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(110))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    });
            };
            menu.Closed += delegate
            {
                menu.BeginAnimation(OpacityProperty, null);
                menu.Opacity = 1.0;
            };
        }

        private static MenuItem CreateBrandMenuItem(TextBlock statusText)
        {
            StackPanel text = new StackPanel();
            text.Children.Add(new TextBlock
            {
                Text = "亚比",
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(78, 52, 40))
            });
            text.Children.Add(statusText);

            Border icon = new Border
            {
                Width = 28,
                Height = 28,
                Background = new SolidColorBrush(Color.FromRgb(207, 128, 83)),
                CornerRadius = new CornerRadius(10),
                Child = new TextBlock
                {
                    Text = "亚",
                    FontFamily = new FontFamily("Microsoft YaHei UI"),
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            return new MenuItem
            {
                Header = text,
                Icon = icon,
                Tag = "YabiHeader",
                IsEnabled = false,
                Focusable = false
            };
        }

        private static MenuItem CreateStyledMenuItem(string title, string glyph)
        {
            return new MenuItem { Header = title, Icon = CreateMenuIcon(glyph) };
        }

        private static TextBlock CreateMenuIcon(string glyph)
        {
            return new TextBlock
            {
                Text = glyph,
                Width = 22,
                TextAlignment = TextAlignment.Center,
                FontFamily = new FontFamily("Segoe UI Symbol"),
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(181, 96, 55)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private static string GetActionGlyph(PetAction action)
        {
            switch (action)
            {
                case PetAction.Stand: return "↑";
                case PetAction.Blink: return "◉";
                case PetAction.Sit: return "⌄";
                case PetAction.LookAround: return "↻";
                case PetAction.Sleep: return "☾";
                case PetAction.Wake: return "☀";
                default: return "•";
            }
        }

        internal static IList<string> RunMenuThemeSelfTests()
        {
            List<string> failures = new List<string>();
            try
            {
                ResourceDictionary resources = BuildContextMenuThemeResources();
                if (!(resources["YabiContextMenuStyle"] is Style))
                {
                    failures.Add("right-click menu theme style is missing");
                }
                if (!resources.Contains(typeof(MenuItem)))
                {
                    failures.Add("right-click menu item style is missing");
                }
                if (!resources.Contains(typeof(Separator)))
                {
                    failures.Add("right-click menu separator style is missing");
                }
            }
            catch (Exception exception)
            {
                failures.Add("right-click menu theme failed to parse: " + exception.Message);
            }
            return failures;
        }

        private void AddActionMenuItem(ItemsControl parent, PetAction action, string title)
        {
            MenuItem item = CreateStyledMenuItem(title, GetActionGlyph(action));
            item.Tag = action;
            item.IsCheckable = true;
            item.Click += ActionMenuItemClick;
            _actionMenuItems[action] = item;
            parent.Items.Add(item);
        }

        private void AddBehaviorMenuItem(MenuItem parent, BehaviorMode behavior, string title)
        {
            MenuItem item = CreateStyledMenuItem(title, behavior == BehaviorMode.Companion ? "✦" : "☾");
            item.Tag = behavior;
            item.IsCheckable = true;
            item.Click += delegate { SetBehavior((BehaviorMode)item.Tag, true); };
            _behaviorMenuItems[behavior] = item;
            parent.Items.Add(item);
        }

        private void AddSizeMenuItem(MenuItem parent, double multiplier, string title, string glyph)
        {
            MenuItem item = CreateStyledMenuItem(title, glyph);
            item.Tag = multiplier;
            item.IsCheckable = true;
            item.Click += delegate
            {
                _sizeMultiplier = (double)item.Tag;
                ResizeForCurrentBitmap(true);
                SyncMenus();
                SaveSettings();
            };
            _sizeMenuItems[multiplier] = item;
            parent.Items.Add(item);
        }

        private void AddOpacityMenuItem(MenuItem parent, double opacity, string title, string glyph)
        {
            MenuItem item = CreateStyledMenuItem(title, glyph);
            item.Tag = opacity;
            item.IsCheckable = true;
            item.Click += delegate
            {
                _petOpacity = (double)item.Tag;
                Opacity = _petOpacity;
                SyncMenus();
                SaveSettings();
            };
            _opacityMenuItems[opacity] = item;
            parent.Items.Add(item);
        }

        private void WindowLoaded(object sender, RoutedEventArgs e)
        {
            if (double.IsNaN(_settings.Left) || double.IsNaN(_settings.Top))
            {
                Left = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width - 42;
                Top = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height - 58;
            }
            _desktopIntegration.RestorePosition(this, _settings);
            _speechBubble = new SpeechBubbleWindow();
            _statusCard = new StatusCardWindow();
            _statusCard.FeedRequested = FeedYabi;
            _statusCard.RestRequested = RestYabi;
            _statusCard.PetRequested = PetYabi;
            _statusCard.FocusRequested = ToggleFocus;
            _statusCard.SettingsRequested = delegate { ShowSettings(0); };
            _statusCard.GazeRequested = StartAttentionTest;
            InitializeTrayIcon();
            _catalog.WarmPoseFrames();
            ResetNaturalIdleTimer();
            InitializeNaturalFeatures();
            _companionTimer.Start();
            _mouseLookTimer.Start();
            _careClock.Sample();
            Microsoft.Win32.SystemEvents.PowerModeChanged += CarePowerModeChanged;
            _careTimer.Start();
            ApplyPowerMode();
            if (_settings.StartWithWindows && !_testUi)
            {
                _desktopIntegration.SetAutoStart(true);
            }
            if (_controller.Mode == BehaviorMode.Quiet && !_testNatural)
            {
                Dispatcher.BeginInvoke(new Action(delegate { StartSleep(false); }));
            }
            else if (!_testUi)
            {
                ShowSpeech("亚比来了～");
            }
            if (_testFeatures)
            {
                DispatcherTimer featureTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
                featureTimer.Tick += delegate
                {
                    featureTimer.Stop();
                    CaptureFeaturePanels();
                };
                featureTimer.Start();
            }
            if (_testGaze) StartGazeRegressionTests();
            else if (_testNatural) StartNaturalRegressionTests();
        }

        private void WindowSourceInitialized(object sender, EventArgs e)
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            _windowSource = HwndSource.FromHwnd(handle);
            if (_windowSource != null)
            {
                _windowSource.AddHook(WindowMessageHook);
            }
            RegisterHotKey(handle, HotkeyToggleVisibility, ModifierControl | ModifierAlt, (uint)KeyInterop.VirtualKeyFromKey(Key.H));
            RegisterHotKey(handle, HotkeyToggleClickThrough, ModifierControl | ModifierAlt, (uint)KeyInterop.VirtualKeyFromKey(Key.P));
            RegisterHotKey(handle, HotkeyExit, ModifierControl | ModifierAlt, (uint)KeyInterop.VirtualKeyFromKey(Key.Q));
        }

        private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == WmHotkey)
            {
                int id = wParam.ToInt32();
                if (id == HotkeyToggleVisibility)
                {
                    ToggleVisibility();
                }
                else if (id == HotkeyToggleClickThrough)
                {
                    SetClickThrough(!_clickThrough, true);
                }
                else if (id == HotkeyExit)
                {
                    Close();
                }
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void InitializeTrayIcon()
        {
            _trayIcon = new Forms.NotifyIcon();
            _trayIcon.Text = "亚比桌宠 4.3.4 · 养成优化版";
            Drawing.Icon icon = null;
            try
            {
                icon = Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
            }
            catch
            {
                icon = null;
            }
            _trayIcon.Icon = icon ?? Drawing.SystemIcons.Application;
            Forms.ContextMenuStrip menu = new Forms.ContextMenuStrip();
            ApplyTrayMenuTheme(menu);
            _trayShowItem = new Forms.ToolStripMenuItem();
            _trayShowItem.Click += delegate { Dispatcher.BeginInvoke(new Action(ToggleVisibility)); };
            menu.Items.Add(_trayShowItem);
            menu.Items.Add(new Forms.ToolStripSeparator());

            AddTrayBehaviorItem(menu, BehaviorMode.Companion, "自动陪伴");
            AddTrayBehaviorItem(menu, BehaviorMode.Quiet, "安静睡觉");
            _trayMouseLookItem = new Forms.ToolStripMenuItem("实时视线跟随");
            _trayMouseLookItem.Click += delegate
            {
                Dispatcher.BeginInvoke(new Action(delegate { SetMouseLookEnabled(!_mouseLookEnabled, true); }));
            };
            menu.Items.Add(_trayMouseLookItem);
            Forms.ToolStripMenuItem gazeStart = new Forms.ToolStripMenuItem("实时注视状态…");
            gazeStart.Click += delegate { Dispatcher.BeginInvoke(new Action(StartAttentionTest)); };
            menu.Items.Add(gazeStart);

            _trayClickThroughItem = new Forms.ToolStripMenuItem("鼠标点击穿透");
            _trayClickThroughItem.Click += delegate
            {
                Dispatcher.BeginInvoke(new Action(delegate { SetClickThrough(!_clickThrough, true); }));
            };
            menu.Items.Add(_trayClickThroughItem);
            menu.Items.Add(new Forms.ToolStripSeparator());
            Forms.ToolStripMenuItem settings = new Forms.ToolStripMenuItem("打开设置");
            settings.Click += delegate { Dispatcher.BeginInvoke(new Action(delegate { ShowSettings(0); })); };
            menu.Items.Add(settings);
            Forms.ToolStripMenuItem feed = new Forms.ToolStripMenuItem("喂食亚比");
            feed.Click += delegate { Dispatcher.BeginInvoke(new Action(FeedYabi)); };
            menu.Items.Add(feed);
            menu.Items.Add(new Forms.ToolStripSeparator());
            Forms.ToolStripMenuItem exit = new Forms.ToolStripMenuItem("退出亚比");
            exit.Click += delegate { Dispatcher.BeginInvoke(new Action(Close)); };
            menu.Items.Add(exit);

            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.DoubleClick += delegate { Dispatcher.BeginInvoke(new Action(ToggleVisibility)); };
            _trayIcon.Visible = true;
            SyncMenus();
        }

        private static void ApplyTrayMenuTheme(Forms.ContextMenuStrip menu)
        {
            menu.BackColor = Drawing.Color.FromArgb(255, 252, 248);
            menu.ForeColor = Drawing.Color.FromArgb(61, 48, 42);
            menu.Font = new Drawing.Font("Microsoft YaHei UI", 9.5f, Drawing.FontStyle.Regular);
            menu.Padding = new Forms.Padding(7, 6, 7, 6);
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = true;
            menu.DropShadowEnabled = true;
            menu.Renderer = new Forms.ToolStripProfessionalRenderer(new YabiTrayColorTable());
            menu.ItemAdded += delegate(object sender, Forms.ToolStripItemEventArgs args)
            {
                args.Item.Margin = new Forms.Padding(1);
                if (!(args.Item is Forms.ToolStripSeparator))
                {
                    args.Item.Padding = new Forms.Padding(7, 5, 7, 5);
                }
            };
        }

        private sealed class YabiTrayColorTable : Forms.ProfessionalColorTable
        {
            private static readonly Drawing.Color Surface = Drawing.Color.FromArgb(255, 252, 248);
            private static readonly Drawing.Color Hover = Drawing.Color.FromArgb(255, 229, 213);
            private static readonly Drawing.Color Accent = Drawing.Color.FromArgb(235, 193, 167);
            private static readonly Drawing.Color Border = Drawing.Color.FromArgb(230, 215, 200);

            public override Drawing.Color ToolStripDropDownBackground { get { return Surface; } }
            public override Drawing.Color ImageMarginGradientBegin { get { return Surface; } }
            public override Drawing.Color ImageMarginGradientMiddle { get { return Surface; } }
            public override Drawing.Color ImageMarginGradientEnd { get { return Surface; } }
            public override Drawing.Color MenuBorder { get { return Border; } }
            public override Drawing.Color MenuItemBorder { get { return Drawing.Color.FromArgb(225, 170, 139); } }
            public override Drawing.Color MenuItemSelected { get { return Hover; } }
            public override Drawing.Color MenuItemSelectedGradientBegin { get { return Hover; } }
            public override Drawing.Color MenuItemSelectedGradientEnd { get { return Hover; } }
            public override Drawing.Color MenuItemPressedGradientBegin { get { return Hover; } }
            public override Drawing.Color MenuItemPressedGradientMiddle { get { return Hover; } }
            public override Drawing.Color MenuItemPressedGradientEnd { get { return Hover; } }
            public override Drawing.Color CheckBackground { get { return Accent; } }
            public override Drawing.Color CheckSelectedBackground { get { return Accent; } }
            public override Drawing.Color CheckPressedBackground { get { return Accent; } }
            public override Drawing.Color SeparatorDark { get { return Border; } }
            public override Drawing.Color SeparatorLight { get { return Surface; } }
        }

        private void AddTrayBehaviorItem(Forms.ContextMenuStrip parent, BehaviorMode behavior, string title)
        {
            Forms.ToolStripMenuItem item = new Forms.ToolStripMenuItem(title);
            item.Click += delegate
            {
                Dispatcher.BeginInvoke(new Action(delegate { SetBehavior(behavior, true); }));
            };
            _trayBehaviorItems[behavior] = item;
            parent.Items.Add(item);
        }

        private void WindowMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
            {
                return;
            }
            _controller.RegisterInteraction(DateTime.UtcNow);
            if (e.ClickCount >= 2)
            {
                _singleClickTimer.Stop();
                if (_statusCard != null)
                {
                    _statusCard.Hide();
                }
                PetAction action = _stablePose == PetPose.Sleeping
                    ? PetAction.Wake
                    : (_stablePose == PetPose.Sitting ? PetAction.Stand : PetAction.Sit);
                RequestManualAction(action);
                e.Handled = true;
                return;
            }

            bool moved = false;
            if (!_settings.LockPosition)
            {
                double beforeLeft = Left;
                double beforeTop = Top;
                _isDragging = true;
                try
                {
                    DragMove();
                    moved = Math.Abs(Left - beforeLeft) >= SystemParameters.MinimumHorizontalDragDistance
                        || Math.Abs(Top - beforeTop) >= SystemParameters.MinimumVerticalDragDistance;
                    if (_settings.EdgeSnap)
                    {
                        _desktopIntegration.SnapToEdges(this, 18.0);
                    }
                    else
                    {
                        _desktopIntegration.KeepInsideCurrentScreen(this);
                    }
                    SaveSettings();
                }
                catch (InvalidOperationException)
                {
                    // A fast release can end the native drag loop immediately.
                }
                finally
                {
                    _isDragging = false;
                }
            }
            if (!moved)
            {
                _singleClickTimer.Stop();
                _singleClickTimer.Start();
            }
            e.Handled = true;
        }

        private void ActionMenuItemClick(object sender, RoutedEventArgs e)
        {
            MenuItem item = sender as MenuItem;
            if (item != null && item.Tag is PetAction)
            {
                RequestManualAction((PetAction)item.Tag);
            }
        }

        private void RequestManualAction(PetAction action)
        {
            DateTime now = DateTime.UtcNow;
            _controller.RegisterInteraction(now);
            _controller.HoldAutomaticActions(now, 7.0);
            if (IsVisualBusy)
            {
                QueueNaturalRequest(new NaturalActionRequest { ClipId = "@pose:" + action, Label = ActionLabel(action) });
                return;
            }
            _naturalQueue.Clear();
            _queuedManualAction = null;
            ResetNaturalIdleTimer();

            if (action == PetAction.Stand)
            {
                if (_stablePose == PetPose.Standing)
                {
                    ShowStableFrame("stand_idle", PetPose.Standing, PetAction.Stand, false, null);
                }
                else
                {
                    StartWake(true);
                }
            }
            else if (action == PetAction.Blink)
            {
                if (_stablePose != PetPose.Standing)
                {
                    _queuedManualAction = PetAction.Blink;
                    StartWake(true);
                }
                else
                {
                    StartBlink(true);
                }
            }
            else if (action == PetAction.Sit)
            {
                EnsureSitting(true, null);
            }
            else if (action == PetAction.LookAround)
            {
                if (_stablePose != PetPose.Sitting)
                {
                    _queuedManualAction = PetAction.LookAround;
                    EnsureSitting(true, null);
                }
                else
                {
                    StartLookAround(true);
                }
            }
            else if (action == PetAction.Sleep)
            {
                StartSleep(true);
            }
            else if (action == PetAction.Wake)
            {
                if (_stablePose == PetPose.Standing)
                {
                    ShowStableFrame("stand_idle", PetPose.Standing, PetAction.Stand, false, null);
                }
                else
                {
                    StartWake(true);
                }
            }
        }

        private void StopVisualWorkForManual()
        {
            _playbackToken++;
            _catalog.CancelPendingRequests();
            _lookRequestToken++;
            _frameTimer.Stop();
            _clipPlaying = false;
            _framePending = false;
            _playbackCompleted = null;
            _activeCommand = PetCommand.None;
            CancelLayerAnimations();
            _controller.CancelTo(_stablePose, DateTime.UtcNow);
            ReleaseFinishedFrames();
        }

        private void CareTimerTick(object sender, EventArgs e)
        {
            if (_isClosing)
            {
                return;
            }
            DateTime nowUtc = DateTime.UtcNow;
            TimeSpan elapsed = _careClock.Sample();
            _careController.Tick(elapsed, _stablePose == PetPose.Sleeping, nowUtc, DateTime.Now);
            _reminderController.Tick(nowUtc);
            string lowNeed = _careController.GetLowNeedMessage(nowUtc);
            if (lowNeed != null && _controller.Mode == BehaviorMode.Companion && !FocusIsQuiet
                && IsLoaded && IsVisible && !_clickThrough && _speechBubble != null && !_speechBubble.ReminderActive)
            {
                ShowSpeech(lowNeed);
                if (_speechBubble.IsVisible) _careController.ConfirmLowNeedShown(nowUtc);
            }
            if (_statusCard != null && _statusCard.IsVisible)
            {
                RefreshStatusCard();
            }
            _focusMenuItem.Header = _reminderController.GetFocusLabel();
            if (_settingsWindow != null && _settingsWindow.IsVisible) _settingsWindow.RefreshData();
        }

        private void CarePowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
        {
            if (e.Mode == Microsoft.Win32.PowerModes.Suspend) _careClock.SetSuspended(true);
            else if (e.Mode == Microsoft.Win32.PowerModes.Resume) _careClock.SetSuspended(false);
        }

        private void ReminderDue(object sender, ReminderDueEventArgs e)
        {
            ReminderDefinition reminder = e.Reminder;
            PresentReminder(reminder);
            if (_trayIcon != null && (!IsVisible || _clickThrough))
            {
                try
                {
                    _trayIcon.BalloonTipTitle = reminder.Name;
                    _trayIcon.BalloonTipText = reminder.Message;
                    _trayIcon.BalloonTipIcon = Forms.ToolTipIcon.Info;
                    _trayIcon.ShowBalloonTip(5000);
                }
                catch
                {
                }
            }
        }

        private void FeedYabi()
        {
            RequestNaturalAction("feed", "feed", false);
        }

        private void PetYabi()
        {
            RequestNaturalAction("pet", "pet", false);
        }

        private void RestYabi()
        {
            _controller.RegisterInteraction(DateTime.UtcNow);
            if (_activeCommand == PetCommand.Sleep) { ShowSpeech("亚比正在准备休息。" ); return; }
            NaturalActionRequest request = new NaturalActionRequest { ClipId = "@rest", CareKind = "rest", CareEpoch = _careEpoch, Label = "休息" };
            if (IsVisualBusy) { QueueNaturalRequest(request); return; }
            _naturalQueue.Clear();
            BeginRestRequest(request);
        }

        private void BeginRestRequest(NaturalActionRequest request)
        {
            if (!request.TryBegin(_careEpoch)) return;
            CareActionResult result = _careController.Rest(DateTime.UtcNow, DateTime.Now, _stablePose == PetPose.Sleeping);
            ShowSpeech(result.Message);
            if (result.Applied)
            {
                RequestManualAction(PetAction.Sleep);
            }
            RefreshStatusCard();
        }

        private void ToggleFocus()
        {
            DateTime now = DateTime.UtcNow;
            if (_reminderController.FocusRunning)
            {
                _reminderController.PauseFocus(now);
                ShowSpeech("专注计时已暂停。" );
            }
            else if (_reminderController.FocusRemaining > TimeSpan.Zero)
            {
                _reminderController.ResumeFocus(now);
                ShowSpeech("继续专注，亚比陪着你。" );
            }
            else
            {
                _reminderController.StartFocus(now);
                ShowSpeech("专注计时开始，完成后亚比会提醒你。" );
            }
            _focusMenuItem.Header = _reminderController.GetFocusLabel();
            RefreshStatusCard();
        }

        private void ResetFocus()
        {
            _reminderController.ResetFocus();
            _focusMenuItem.Header = _reminderController.GetFocusLabel();
            ShowSpeech("专注计时已重置。" );
            RefreshStatusCard();
        }

        private void SnoozeFocus(int minutes)
        {
            _reminderController.SnoozeFocus(DateTime.UtcNow, minutes);
            _focusMenuItem.Header = _reminderController.GetFocusLabel();
            ShowSpeech("亚比会在" + minutes + "分钟后提醒你。" );
            RefreshStatusCard();
        }

        private void ShowStatusCard()
        {
            if (_isClosing || _clickThrough || _statusCard == null)
            {
                return;
            }
            RefreshStatusCard();
            _statusCard.ShowNear(this);
        }

        private void RefreshStatusCard()
        {
            if (_statusCard != null)
            {
                CareSnapshot snapshot = _careController.GetSnapshot(DateTime.UtcNow, DateTime.Now);
                _statusCard.Refresh(snapshot, _reminderController.GetFocusLabel());
                _statusCard.SetActivity(ActivityLabel);
                _statusCard.SetCareFeedback(snapshot.FeedCooldown, snapshot.PetCooldown,
                    _naturalQueue.Peek != null && _naturalQueue.Peek.CareKind == "feed",
                    _naturalQueue.Peek != null && _naturalQueue.Peek.CareKind == "pet");
                _statusCard.SetGazeStatus(_gazeStatus);
            }
        }

        private void ShowSettings(int initialTab)
        {
            if (_settingsWindow != null)
            {
                _settingsWindow.Activate();
                return;
            }
            _settingsWindow = new SettingsWindow(_settings, _careController, initialTab);
            _settingsWindow.Owner = this;
            _settingsWindow.SettingsChanged = ApplySettings;
            _settingsWindow.ProfileResetRequested = delegate {
                _careEpoch++; _naturalQueue.Clear(); _queuedManualAction = null; _latestReminder = null;
                _careClock.Sample();
                if (_speechBubble != null) _speechBubble.Hide();
            };
            _settingsWindow.TestAttentionRequested += delegate { StartAttentionTest(); };
            _settingsWindow.Closed += delegate { _settingsWindow = null; };
            FitUtilityWindow(_settingsWindow);
            _settingsWindow.Show();
            _settingsWindow.Activate();
        }

        private void ApplySettings()
        {
            _manualMirror = _settings.ManualMirror;
            _mouseLookEnabled = _settings.MouseLookEnabled;
            _sizeMultiplier = _settings.Scale;
            _petOpacity = _settings.Opacity;
            Topmost = _settings.AlwaysOnTop;
            Opacity = _petOpacity;
            ApplyMirror();
            ResizeForCurrentBitmap(true);
            SetBehavior(_settings.Behavior, false);
            _controller.SetFrequency(_settings.InteractionFrequency, DateTime.UtcNow);
            _reminderController.Refresh(DateTime.UtcNow);
            string startError = _testUi ? null : _desktopIntegration.SetAutoStart(_settings.StartWithWindows);
            if (startError != null)
            {
                ShowSpeech(startError);
            }
            ApplyPowerMode();
            SaveSettings();
            SyncMenus();
        }

        private void ApplyPowerMode()
        {
            bool eco = _desktopIntegration.IsEcoMode(_settings.PowerMode);
            _mouseLookTimer.Interval = TimeSpan.FromMilliseconds(eco ? 50 : 33);
            _companionTimer.Interval = TimeSpan.FromMilliseconds(eco ? 500 : 250);
        }

        private void CompanionTimerTick(object sender, EventArgs e)
        {
            if (_isClosing || !IsVisible || IsVisualBusy || FocusIsQuiet || HasInteractionPanel || _testNatural || _continuousEngaged)
            {
                return;
            }
            TrackCursorMovement();
            if (TryNaturalIdleAction()) return;
            bool cursorStill = DateTime.UtcNow - _lastCursorMovedUtc >= TimeSpan.FromSeconds(3);
            PetCommand command = _controller.Tick(DateTime.UtcNow, cursorStill);
            if (command != PetCommand.None)
            {
                ExecuteCommand(command, false);
            }
        }

        private void ExecuteCommand(PetCommand command, bool manual)
        {
            if (command == PetCommand.Blink && _stablePose == PetPose.Standing)
            {
                StartBlink(manual);
            }
            else if (command == PetCommand.Sit && _stablePose == PetPose.Standing)
            {
                StartSit(manual);
            }
            else if (command == PetCommand.LookAround && _stablePose == PetPose.Sitting)
            {
                StartLookAround(manual);
            }
            else if (command == PetCommand.Sleep)
            {
                StartSleep(manual);
            }
            else if (command == PetCommand.Wake && _stablePose != PetPose.Standing)
            {
                StartWake(manual);
            }
        }

        private void StartBlink(bool manual)
        {
            _controller.Begin(PetCommand.Blink, manual, DateTime.UtcNow);
            _activeCommand = PetCommand.Blink;
            _currentAction = PetAction.Blink;
            SyncMenus();
            PlayClip("stand_blink", false, delegate
            {
                _stablePose = PetPose.Standing;
                _controller.Complete(PetCommand.Blink, DateTime.UtcNow);
                _activeCommand = PetCommand.None;
                _currentAction = PetAction.Stand;
                ShowStableFrame("stand_idle", PetPose.Standing, PetAction.Stand, false, ProcessQueuedManualAction);
            });
        }

        private void StartSit(bool manual, Action completed = null)
        {
            _controller.Begin(PetCommand.Sit, manual, DateTime.UtcNow);
            _activeCommand = PetCommand.Sit;
            _currentAction = PetAction.Sit;
            SyncMenus();
            PlayClip("stand_to_sit", false, delegate
            {
                _stablePose = PetPose.Sitting;
                _controller.Complete(PetCommand.Sit, DateTime.UtcNow);
                _activeCommand = PetCommand.None;
                ShowStableFrame("sit_idle", PetPose.Sitting, PetAction.Sit, false, completed ?? ProcessQueuedManualAction);
            });
        }

        private void EnsureSitting(bool manual, Action completed)
        {
            if (_stablePose == PetPose.Sitting)
            {
                ShowStableFrame("sit_idle", PetPose.Sitting, PetAction.Sit, false, completed ?? ProcessQueuedManualAction);
                return;
            }
            if (_stablePose == PetPose.Standing)
            {
                StartSit(manual, completed);
                return;
            }

            _controller.Begin(PetCommand.Sit, manual, DateTime.UtcNow);
            _activeCommand = PetCommand.Sit;
            _currentAction = PetAction.Sit;
            PlayClip("sleep_to_sit", false, delegate
            {
                _stablePose = PetPose.Sitting;
                _controller.Complete(PetCommand.Sit, DateTime.UtcNow);
                _activeCommand = PetCommand.None;
                if (completed != null)
                {
                    ShowStableFrame("sit_idle", PetPose.Sitting, PetAction.Sit, false, completed);
                }
                else
                {
                    ShowStableFrame("sit_idle", PetPose.Sitting, PetAction.Sit, false, ProcessQueuedManualAction);
                }
            });
        }

        private void StartLookAround(bool manual)
        {
            _controller.Begin(PetCommand.LookAround, manual, DateTime.UtcNow);
            _activeCommand = PetCommand.LookAround;
            _currentAction = PetAction.LookAround;
            SyncMenus();
            PlayClip(_catalog.HasClip("notice_all") ? "notice_all" : "sit_lookaround", false, delegate
            {
                _stablePose = PetPose.Sitting;
                _controller.Complete(PetCommand.LookAround, DateTime.UtcNow);
                _activeCommand = PetCommand.None;
                ShowStableFrame("sit_idle", PetPose.Sitting, PetAction.Sit, false, ProcessQueuedManualAction);
            });
        }

        private void StartSleep(bool manual)
        {
            if (_stablePose == PetPose.Sleeping && !_controller.IsBusy)
            {
                ShowStableFrame("sleep_idle", PetPose.Sleeping, PetAction.Sleep, false, null);
                return;
            }
            _controller.Begin(PetCommand.Sleep, manual, DateTime.UtcNow);
            _activeCommand = PetCommand.Sleep;
            _currentAction = PetAction.Sleep;
            SyncMenus();
            Action finishSleep = delegate
            {
                _stablePose = PetPose.Sleeping;
                _controller.Complete(PetCommand.Sleep, DateTime.UtcNow);
                _activeCommand = PetCommand.None;
                ShowStableFrame("sleep_idle", PetPose.Sleeping, PetAction.Sleep, false, ProcessQueuedManualAction);
            };

            Action continueFromSitting = delegate
            {
                _stablePose = PetPose.Sitting;
                if (_catalog.HasClip("sit_to_sleep")
                    && _catalog.GetClip("sit_to_sleep").StartPose == PetPose.Sitting)
                {
                    PlayClip("sit_to_sleep", false, finishSleep);
                    return;
                }
                ShowStableFrame("sleep_idle", PetPose.Sleeping, PetAction.Sleep, true, delegate
                {
                    _stablePose = PetPose.Sleeping;
                    _controller.Complete(PetCommand.Sleep, DateTime.UtcNow);
                    _activeCommand = PetCommand.None;
                    ProcessQueuedManualAction();
                });
            };

            if (_stablePose == PetPose.Standing && _catalog.HasClip("stand_to_sit"))
            {
                PlayClip("stand_to_sit", false, continueFromSitting);
            }
            else
            {
                continueFromSitting();
            }
        }

        private void StartWake(bool manual)
        {
            if (_stablePose == PetPose.Standing)
            {
                ShowStableFrame("stand_idle", PetPose.Standing, PetAction.Stand, false, ProcessQueuedManualAction);
                return;
            }
            _controller.Begin(PetCommand.Wake, manual, DateTime.UtcNow);
            _activeCommand = PetCommand.Wake;
            _currentAction = PetAction.Wake;
            SyncMenus();

            Action reverseToStand = delegate
            {
                _stablePose = PetPose.Sitting;
                PlayClip("stand_to_sit", true, delegate
                {
                    _stablePose = PetPose.Standing;
                    _controller.Complete(PetCommand.Wake, DateTime.UtcNow);
                    _activeCommand = PetCommand.None;
                    ShowStableFrame("stand_idle", PetPose.Standing, PetAction.Stand, false, ProcessQueuedManualAction);
                });
            };

            if (_stablePose == PetPose.Sleeping)
            {
                if (_catalog.HasClip("sleep_to_sit")
                    && _catalog.GetClip("sleep_to_sit").StartPose == PetPose.Sleeping)
                {
                    PlayClip("sleep_to_sit", false, delegate
                    {
                        _stablePose = PetPose.Sitting;
                        reverseToStand();
                    });
                }
                else
                {
                    ShowStableFrame("sit_idle", PetPose.Sitting, PetAction.Wake, true, reverseToStand);
                }
            }
            else
            {
                reverseToStand();
            }
        }

        private void ProcessQueuedManualAction()
        {
            _activityText = null;
            ResetNaturalIdleTimer();
            if (ProcessNaturalQueue()) return;
            if (!_queuedManualAction.HasValue || _isClosing)
            {
                SyncMenus();
                return;
            }
            PetAction next = _queuedManualAction.Value;
            _queuedManualAction = null;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate { RequestManualAction(next); }));
        }

        private void PlayClip(string clipId, bool reverse, Action completed)
        {
            _frameTimer.Stop();
            int token = ++_playbackToken;
            _catalog.CancelPendingRequests();
            ClipDefinition clip = _catalog.GetClip(clipId);
            _playingClip = clipId;
            bool ecoPlayback = _desktopIntegration.IsEcoMode(_settings.PowerMode) && clip.Fps > 12;
            _playingDirection = reverse ? -1 : 1;
            _playingFrame = reverse ? clip.FrameCount - 1 : 0;
            _framePending = true;
            _clipPlaying = true;
            _playbackCompleted = completed;
            _playbackClock.Reset();
            _frameRequestClock.Restart();
            TracePlayback("start", clipId);
            int displayFps = ecoPlayback
                ? Math.Min(12, clip.Fps)
                : clip.Fps;
            _frameTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / Math.Max(1, displayFps));
            _catalog.PrefetchFrames(clipId, _playingFrame, _playingDirection, 3);
            _frameTimer.Start();
            _catalog.RequestFrameResult(clipId, _playingFrame, delegate(FrameLoadResult result)
            {
                if (token != _playbackToken || !_clipPlaying)
                {
                    return;
                }
                _framePending = false;
                if (!result.Succeeded) { HandlePlaybackFailure(result.Error); return; }
                DisplayDecodedFrame(result, false, null);
                _playbackClock.Restart();
            });
        }

        private void FrameTimerTick(object sender, EventArgs e)
        {
            if (!_clipPlaying)
            {
                return;
            }
            if (_framePending)
            {
                if (_frameRequestClock.Elapsed.TotalSeconds > 5) HandlePlaybackFailure("等待视频帧超时");
                return;
            }
            ClipDefinition clip = _catalog.GetClip(_playingClip);
            int last = _playingDirection < 0 ? 0 : clip.FrameCount - 1;
            double elapsed = _playbackClock.Elapsed.TotalMilliseconds;
            int next = clip.FrameAt(elapsed, _playingDirection < 0);
            if (elapsed >= clip.DurationMs && _playingFrame == last)
            {
                _frameTimer.Stop();
                _clipPlaying = false;
                TracePlayback("complete", _playingClip);
                Action completion = _playbackCompleted;
                _playbackCompleted = null;
                if (completion != null)
                {
                    completion();
                }
                ReleaseFinishedFrames();
                return;
            }
            if (next == _playingFrame) return;

            int token = _playbackToken;
            _framePending = true;
            _frameRequestClock.Restart();
            _catalog.PrefetchFrames(_playingClip, next, _playingDirection, 3);
            _catalog.RequestFrameResult(_playingClip, next, delegate(FrameLoadResult result)
            {
                if (token != _playbackToken || !_clipPlaying)
                {
                    return;
                }
                _playingFrame = next;
                _framePending = false;
                if (!result.Succeeded) { HandlePlaybackFailure(result.Error); return; }
                DisplayDecodedFrame(result, false, null);
            });
        }

        private void ShowStableFrame(string clipId, PetPose pose, PetAction action, bool crossfade, Action completed)
        {
            int token = _playbackToken;
            _framePending = true;
            _catalog.RequestFrameResult(clipId, 0, delegate(FrameLoadResult result)
            {
                if (token != _playbackToken || _isClosing)
                {
                    return;
                }
                _framePending = false;
                if (!result.Succeeded)
                {
                    _controller.CancelTo(_stablePose, DateTime.UtcNow);
                    _activeCommand = PetCommand.None;
                    _activityText = "素材暂不可用";
                    LogPlaybackError(result.Error);
                    SyncMenus();
                    return;
                }
                _stablePose = pose;
                _currentAction = action;
                DisplayDecodedFrame(result, crossfade, delegate
                {
                    SyncMenus();
                    if (completed != null)
                    {
                        completed();
                    }
                });
            });
        }

        private void DisplayBitmap(BitmapSource bitmap, bool crossfade, Action completed)
        {
            if (bitmap == null || _isClosing)
            {
                return;
            }
            SuspendContinuousGaze();
            _currentBitmap = bitmap;
            ResizeForCurrentBitmap(true);
            if (!crossfade || !IsLoaded)
            {
                CancelLayerAnimations();
                _activeImage.Source = bitmap;
                _activeImage.Opacity = 1.0;
                Image inactive = _activeImage == _imageA ? _imageB : _imageA;
                inactive.Opacity = 0.0;
                inactive.Source = null;
                if (completed != null)
                {
                    completed();
                }
                return;
            }

            CancelLayerAnimations();
            _isCrossfading = true;
            Image outgoing = _activeImage;
            Image incoming = outgoing == _imageA ? _imageB : _imageA;
            incoming.Source = bitmap;
            incoming.Opacity = 0.0;
            outgoing.Opacity = 1.0;
            Panel.SetZIndex(outgoing, 1);
            Panel.SetZIndex(incoming, 2);

            Duration duration = new Duration(TimeSpan.FromMilliseconds(190));
            DoubleAnimation fadeIn = new DoubleAnimation(0.0, 1.0, duration);
            DoubleAnimation fadeOut = new DoubleAnimation(1.0, 0.0, duration);
            fadeIn.FillBehavior = FillBehavior.Stop;
            fadeOut.FillBehavior = FillBehavior.Stop;
            fadeIn.Completed += delegate
            {
                incoming.BeginAnimation(UIElement.OpacityProperty, null);
                outgoing.BeginAnimation(UIElement.OpacityProperty, null);
                incoming.Opacity = 1.0;
                outgoing.Opacity = 0.0;
                outgoing.Source = null;
                Panel.SetZIndex(incoming, 1);
                Panel.SetZIndex(outgoing, 0);
                _activeImage = incoming;
                _isCrossfading = false;
                if (completed != null)
                {
                    completed();
                }
            };
            outgoing.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            incoming.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }

        private void ReleaseFinishedFrames()
        {
            _catalog.TrimDynamicCache(4);
        }

        private void CancelLayerAnimations()
        {
            _imageA.BeginAnimation(UIElement.OpacityProperty, null);
            _imageB.BeginAnimation(UIElement.OpacityProperty, null);
            if (_imageA.Opacity >= _imageB.Opacity)
            {
                _activeImage = _imageA;
            }
            else
            {
                _activeImage = _imageB;
            }
            _activeImage.Opacity = 1.0;
            Image inactive = _activeImage == _imageA ? _imageB : _imageA;
            inactive.Opacity = 0.0;
            Panel.SetZIndex(_activeImage, 1);
            Panel.SetZIndex(inactive, 0);
            _isCrossfading = false;
        }

        private void ResizeForCurrentBitmap(bool preserveAnchor)
        {
            if (_currentBitmap == null)
            {
                return;
            }
            double targetWidth = _currentBitmap.PixelWidth * BaseDisplayScale * _sizeMultiplier;
            double targetHeight = _currentBitmap.PixelHeight * BaseDisplayScale * _sizeMultiplier;
            if (Math.Abs(Width - targetWidth) < 0.2 && Math.Abs(Height - targetHeight) < 0.2)
            {
                return;
            }
            double center = Left + Width / 2.0;
            double bottom = Top + Height;
            bool anchored = preserveAnchor && IsLoaded;
            Width = targetWidth;
            Height = targetHeight;
            if (anchored)
            {
                Left = center - Width / 2.0;
                Top = bottom - Height;
                KeepInsideVirtualScreen();
            }
        }

        private void MouseLookTimerTick(object sender, EventArgs e)
        {
            NaturalMouseTick();
        }

        private void TrackCursorMovement()
        {
            Drawing.Point current = Forms.Control.MousePosition;
            int dx = current.X - _lastCursorPixels.X;
            int dy = current.Y - _lastCursorPixels.Y;
            if (dx * dx + dy * dy >= 9)
            {
                _lastCursorPixels = current;
                _lastCursorMovedUtc = DateTime.UtcNow;
            }
        }

        private Point GetCursorPositionInDips()
        {
            Drawing.Point cursor = Forms.Control.MousePosition;
            Point point = new Point(cursor.X, cursor.Y);
            PresentationSource source = PresentationSource.FromVisual(this);
            if (source != null && source.CompositionTarget != null)
            {
                point = source.CompositionTarget.TransformFromDevice.Transform(point);
            }
            return point;
        }

        private void SetBehavior(BehaviorMode behavior, bool announce)
        {
            _settings.Behavior = behavior;
            _controller.SetMode(behavior, DateTime.UtcNow);
            if (behavior == BehaviorMode.Quiet && _stablePose != PetPose.Sleeping)
            {
                RequestManualAction(PetAction.Sleep);
            }
            if (announce)
            {
                ShowSpeech(behavior == BehaviorMode.Quiet ? "亚比安静睡一会儿。" : "亚比会在原地陪着你。" );
            }
            SyncMenus();
            SaveSettings();
        }

        private void SetMouseLookEnabled(bool enabled, bool announce)
        {
            if (enabled) _continuousUnavailable = false;
            _mouseLookEnabled = enabled;
            _settings.MouseLookEnabled = enabled;
            _lookRequestToken++;
            _attention.Reset();
            if (announce)
            {
                ShowSpeech(enabled ? "实时注视已开启，移动鼠标即可；站立和坐姿都能回应。" : "已关闭实时注视，视线会平滑回正。" );
            }
            SyncMenus();
            SaveSettings();
        }

        private void ApplyMirror()
        {
            if (!IsVisualBusy) _attention.Reset();
            _visualRoot.RenderTransform = new ScaleTransform(_manualMirror ? -1.0 : 1.0, 1.0);
        }

        private void SetClickThrough(bool enabled, bool announce)
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }
            int style = GetWindowLong(handle, GwlExStyle);
            style = enabled ? (style | WsExTransparent) : (style & ~WsExTransparent);
            SetWindowLong(handle, GwlExStyle, style);
            _clickThrough = enabled;
            SyncMenus();
            if (announce)
            {
                ShowSpeech(enabled ? "已开启点击穿透，按 Ctrl+Alt+P 可恢复。" : "已关闭点击穿透，可以继续拖动亚比。" );
            }
        }

        private void ToggleVisibility()
        {
            if (IsVisible)
            {
                if (_attentionTest != null) _attentionTest.Close();
                if (_speechBubble != null)
                {
                    _speechBubble.StopAndHide();
                }
                Hide();
                if (_statusCard != null) _statusCard.Hide();
                _naturalQueue.Clear();
                _queuedManualAction = null;
                _attention.Reset();
            }
            else
            {
                Show();
                Activate();
                Topmost = _settings.AlwaysOnTop;
            }
            SyncMenus();
        }

        private void SayRandomPhrase()
        {
            if (FocusIsQuiet) return;
            string[] phrases = _settings.CustomPhrases.Count == 0
                ? PetSettings.DefaultPhrases
                : _settings.CustomPhrases.ToArray();
            ShowSpeech(phrases[_random.Next(phrases.Length)]);
        }

        private void ShowSpeech(string message)
        {
            if (IsLoaded && IsVisible && _speechBubble != null)
            {
                _speechBubble.Say(message, this);
            }
        }

        private void SyncMenus()
        {
            _visualRoot.ToolTip = "亚比 · " + GetStateLabel() + "（左键拖动，右键菜单）";
            _menuStatusText.Text = "当前：" + GetStateLabel() + " · " +
                (_controller.Mode == BehaviorMode.Companion ? "自动陪伴" : "安静睡觉");
            foreach (KeyValuePair<PetAction, MenuItem> pair in _actionMenuItems)
            {
                pair.Value.IsChecked = pair.Key == _currentAction;
            }
            foreach (KeyValuePair<BehaviorMode, MenuItem> pair in _behaviorMenuItems)
            {
                pair.Value.IsChecked = pair.Key == _controller.Mode;
            }
            foreach (KeyValuePair<double, MenuItem> pair in _sizeMenuItems)
            {
                pair.Value.IsChecked = Math.Abs(pair.Key - _sizeMultiplier) < 0.01;
            }
            foreach (KeyValuePair<double, MenuItem> pair in _opacityMenuItems)
            {
                pair.Value.IsChecked = Math.Abs(pair.Key - _petOpacity) < 0.01;
            }
            _mouseLookMenuItem.IsChecked = _mouseLookEnabled;
            _clickThroughMenuItem.IsChecked = _clickThrough;
            _mirrorMenuItem.IsChecked = _manualMirror;
            _lockPositionMenuItem.IsChecked = _settings.LockPosition;
            _edgeSnapMenuItem.IsChecked = _settings.EdgeSnap;
            _alwaysOnTopMenuItem.IsChecked = _settings.AlwaysOnTop;
            _focusMenuItem.Header = _reminderController.GetFocusLabel();
            if (_trayShowItem != null)
            {
                _trayShowItem.Text = IsVisible ? "隐藏亚比" : "显示亚比";
            }
            if (_trayClickThroughItem != null)
            {
                _trayClickThroughItem.Checked = _clickThrough;
            }
            if (_trayMouseLookItem != null)
            {
                _trayMouseLookItem.Checked = _mouseLookEnabled;
            }
            foreach (KeyValuePair<BehaviorMode, Forms.ToolStripMenuItem> pair in _trayBehaviorItems)
            {
                pair.Value.Checked = pair.Key == _controller.Mode;
            }
        }

        private string GetStateLabel()
        {
            if (!string.IsNullOrEmpty(_activityText)) return _activityText;
            if (_activeCommand == PetCommand.Blink) return "眨眼";
            if (_activeCommand == PetCommand.Sit) return "坐下";
            if (_activeCommand == PetCommand.LookAround) return "环顾";
            if (_activeCommand == PetCommand.Sleep) return "入睡";
            if (_activeCommand == PetCommand.Wake) return "唤醒";
            if (_stablePose == PetPose.Sitting) return "坐姿";
            if (_stablePose == PetPose.Sleeping) return "睡觉";
            return "站立";
        }

        private void KeepInsideVirtualScreen()
        {
            _desktopIntegration.KeepInsideCurrentScreen(this);
        }

        private void SaveSettings()
        {
            if (!IsLoaded || _testUi)
            {
                return;
            }
            _settings.Left = Left;
            _settings.Top = Top;
            _settings.Scale = _sizeMultiplier;
            _settings.Opacity = _petOpacity;
            _settings.ManualMirror = _manualMirror;
            _settings.MouseLookEnabled = _mouseLookEnabled;
            _settings.Behavior = _controller.Mode;
            _settings.AlwaysOnTop = Topmost;
            _desktopIntegration.CapturePosition(this, _settings);
            _settings.Save();
        }

        private void ShowAbout(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                this,
                "亚比桌宠 4.3.4 · 养成优化版\n\n" +
                "单击查看状态；左键拖动；双击切换站立/坐姿，睡觉时双击唤醒；右键打开菜单。\n\n" +
                "快捷键：\n" +
                "Ctrl+Alt+H  显示/隐藏亚比\n" +
                "Ctrl+Alt+P  开关鼠标穿透\n" +
                "Ctrl+Alt+Q  退出\n\n" +
                "实时小幅视线跟随，完整原速视频动作；照片网格近似，非独立眼球或真实侧脸。完全静音、离线运行。",
                "关于亚比",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void WindowClosing(object sender, CancelEventArgs e)
        {
            _careController.Tick(_careClock.Sample(), _stablePose == PetPose.Sleeping, DateTime.UtcNow, DateTime.Now);
            _isClosing = true;
            CloseNaturalFeatures();
            _playbackToken++;
            _lookRequestToken++;
            _frameTimer.Stop();
            _companionTimer.Stop();
            _mouseLookTimer.Stop();
            _careTimer.Stop();
            Microsoft.Win32.SystemEvents.PowerModeChanged -= CarePowerModeChanged;
            _singleClickTimer.Stop();
            SaveSettings();
            _careController.Save();

            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
            {
                UnregisterHotKey(handle, HotkeyToggleVisibility);
                UnregisterHotKey(handle, HotkeyToggleClickThrough);
                UnregisterHotKey(handle, HotkeyExit);
            }
            if (_windowSource != null)
            {
                _windowSource.RemoveHook(WindowMessageHook);
            }
            if (_speechBubble != null)
            {
                _speechBubble.Close();
            }
            if (_statusCard != null)
            {
                _statusCard.Close();
            }
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }
            _catalog.Dispose();
        }

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr windowHandle, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr windowHandle, int index, int newStyle);

        [DllImport("psapi.dll")]
        private static extern bool EmptyWorkingSet(IntPtr processHandle);
    }
}
