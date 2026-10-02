using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;

namespace YabiDesktopPet
{
    internal static class UiTheme
    {
        public static readonly SolidColorBrush Cream = Brush("#FFFCF6");
        public static readonly SolidColorBrush Apricot = Brush("#F5DFC9");
        public static readonly SolidColorBrush Ink = Brush("#493B32");
        public static readonly SolidColorBrush Muted = Brush("#796C60");
        public static readonly SolidColorBrush Line = Brush("#E6D8C8");
        public static readonly SolidColorBrush Green = Brush("#83997A");

        public static void Apply(FrameworkElement root)
        {
            root.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(Resources));
            root.SetValue(System.Windows.Controls.TextBlock.FontFamilyProperty, new FontFamily("Microsoft YaHei UI"));
            root.SetValue(System.Windows.Controls.TextBlock.ForegroundProperty, Ink);
            root.SetValue(System.Windows.Controls.TextBlock.FontSizeProperty, 13.0);
        }

        private static SolidColorBrush Brush(string value)
        {
            SolidColorBrush brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
            brush.Freeze();
            return brush;
        }

        private const string Resources = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
 <Style x:Key='FocusRing'><Setter Property='Control.Template'><Setter.Value><ControlTemplate><Border Margin='-3' CornerRadius='10' BorderBrush='#697F61' BorderThickness='2'/></ControlTemplate></Setter.Value></Setter></Style>
 <Style x:Key='Base' TargetType='Control'>
  <Setter Property='FontFamily' Value='Microsoft YaHei UI'/><Setter Property='FontSize' Value='13'/><Setter Property='Foreground' Value='#493B32'/>
  <Setter Property='Background' Value='#FFFCF6'/><Setter Property='BorderBrush' Value='#E6D8C8'/><Setter Property='BorderThickness' Value='1'/>
  <Setter Property='Padding' Value='12,8'/><Setter Property='FocusVisualStyle' Value='{StaticResource FocusRing}'/>
  <Setter Property='VerticalContentAlignment' Value='Center'/>
  <Style.Triggers><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.5'/></Trigger></Style.Triggers>
 </Style>
 <Style TargetType='Button' BasedOn='{StaticResource Base}'>
  <Setter Property='Cursor' Value='Hand'/><Setter Property='HorizontalContentAlignment' Value='Center'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
   <Border x:Name='Surface' CornerRadius='8' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' Padding='{TemplateBinding Padding}'>
    <ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='{TemplateBinding VerticalContentAlignment}' RecognizesAccessKey='True'/>
   </Border>
   <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='Background' Value='#F3E6D6'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='Surface' Property='Background' Value='#E9CFB5'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Surface' Property='BorderBrush' Value='#697F61'/></Trigger></ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='TextBox' BasedOn='{StaticResource Base}'>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='TextBox'>
   <Border x:Name='Surface' CornerRadius='8' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'><ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}'/></Border>
   <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='BorderBrush' Value='#BCA48C'/></Trigger><Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='Surface' Property='BorderBrush' Value='#697F61'/></Trigger></ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='ComboBox' BasedOn='{StaticResource Base}'>
  <Setter Property='ScrollViewer.HorizontalScrollBarVisibility' Value='Auto'/><Setter Property='ScrollViewer.VerticalScrollBarVisibility' Value='Auto'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBox'>
   <Grid>
    <Border x:Name='Surface' CornerRadius='8' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'/>
    <ToggleButton x:Name='DropDownToggle' Focusable='False' ClickMode='Press' IsChecked='{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'>
     <ToggleButton.Template><ControlTemplate TargetType='ToggleButton'><Border Background='Transparent'><TextBlock Text='⌄' Margin='0,0,12,0' HorizontalAlignment='Right' VerticalAlignment='Center' Foreground='#796C60'/></Border></ControlTemplate></ToggleButton.Template>
    </ToggleButton>
    <ContentPresenter x:Name='SelectedContent' Margin='12,8,34,8' IsHitTestVisible='False' VerticalAlignment='Center' Content='{TemplateBinding SelectionBoxItem}' ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}' ContentTemplateSelector='{TemplateBinding ItemTemplateSelector}'/>
    <TextBox x:Name='PART_EditableTextBox' Margin='8,3,30,3' Visibility='Hidden' IsReadOnly='{TemplateBinding IsReadOnly}' Background='Transparent' BorderThickness='0' Padding='4'/>
    <Popup x:Name='PART_Popup' Placement='Bottom' IsOpen='{TemplateBinding IsDropDownOpen}' AllowsTransparency='True' Focusable='False' PopupAnimation='Fade'>
     <Border MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}' MaxHeight='{TemplateBinding MaxDropDownHeight}' Background='#FFFCF6' BorderBrush='#E6D8C8' BorderThickness='1' CornerRadius='8' Padding='4'>
      <ScrollViewer CanContentScroll='True'><ItemsPresenter KeyboardNavigation.DirectionalNavigation='Contained'/></ScrollViewer>
     </Border>
    </Popup>
   </Grid>
   <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='BorderBrush' Value='#BCA48C'/></Trigger><Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='Surface' Property='BorderBrush' Value='#697F61'/></Trigger><Trigger Property='IsEditable' Value='True'><Setter TargetName='PART_EditableTextBox' Property='Visibility' Value='Visible'/><Setter TargetName='SelectedContent' Property='Visibility' Value='Hidden'/></Trigger></ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='ComboBoxItem' BasedOn='{StaticResource Base}'>
  <Setter Property='Padding' Value='10,7'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBoxItem'><Border x:Name='Surface' CornerRadius='6' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}'><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property='IsHighlighted' Value='True'><Setter TargetName='Surface' Property='Background' Value='#F3E6D6'/></Trigger><Trigger Property='IsSelected' Value='True'><Setter TargetName='Surface' Property='Background' Value='#E8EDDF'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='CheckBox' BasedOn='{StaticResource Base}'>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='CheckBox'>
   <Grid><Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions>
    <Border x:Name='Box' Width='19' Height='19' VerticalAlignment='Top' Margin='0,2,10,0' CornerRadius='5' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1'>
     <TextBlock x:Name='Mark' Text='✓' Foreground='#FFFCF6' HorizontalAlignment='Center' VerticalAlignment='Center' FontSize='13' Visibility='Hidden'/>
    </Border><ContentPresenter Grid.Column='1' RecognizesAccessKey='True' VerticalAlignment='{TemplateBinding VerticalContentAlignment}'/>
   </Grid>
   <ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'><Setter TargetName='Box' Property='Background' Value='#83997A'/><Setter TargetName='Mark' Property='Visibility' Value='Visible'/></Trigger><Trigger Property='IsChecked' Value='{x:Null}'><Setter TargetName='Box' Property='Background' Value='#83997A'/><Setter TargetName='Mark' Property='Text' Value='−'/><Setter TargetName='Mark' Property='Visibility' Value='Visible'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Box' Property='BorderBrush' Value='#697F61'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Box' Property='BorderBrush' Value='#493B32'/><Setter TargetName='Box' Property='BorderThickness' Value='2'/></Trigger></ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='ProgressBar' BasedOn='{StaticResource Base}'>
  <Setter Property='Foreground' Value='#83997A'/><Setter Property='Background' Value='#EFE6DA'/><Setter Property='BorderThickness' Value='0'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ProgressBar'>
   <Grid><Border x:Name='PART_Track' Background='{TemplateBinding Background}' CornerRadius='8'/><Border x:Name='PART_Indicator' HorizontalAlignment='Left' Background='{TemplateBinding Foreground}' CornerRadius='8'/></Grid>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='TabItem' BasedOn='{StaticResource Base}'>
  <Setter Property='Margin' Value='0,0,8,6'/><Setter Property='Padding' Value='16,12'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='TabItem'>
   <Border x:Name='Surface' Background='Transparent' CornerRadius='8' Padding='{TemplateBinding Padding}' BorderThickness='1' BorderBrush='Transparent'><ContentPresenter x:Name='Heading' ContentSource='Header' RecognizesAccessKey='True'/></Border>
   <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='Background' Value='#F3E6D6'/></Trigger><Trigger Property='IsSelected' Value='True'><Setter TargetName='Surface' Property='Background' Value='#F5DFC9'/><Setter TargetName='Heading' Property='TextElement.FontWeight' Value='SemiBold'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Surface' Property='BorderBrush' Value='#697F61'/></Trigger></ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='TabControl' BasedOn='{StaticResource Base}'>
  <Setter Property='HorizontalContentAlignment' Value='Stretch'/><Setter Property='VerticalContentAlignment' Value='Stretch'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='TabControl'><Grid KeyboardNavigation.TabNavigation='Local'><Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions><TabPanel IsItemsHost='True' KeyboardNavigation.TabIndex='1' Margin='0,0,8,0'/><ContentPresenter x:Name='PART_SelectedContentHost' Grid.Column='1' ContentSource='SelectedContent' KeyboardNavigation.TabIndex='2'/></Grid></ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='Slider' BasedOn='{StaticResource Base}'>
  <Setter Property='Height' Value='30'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Slider'>
   <Grid Margin='8,0'><Border Height='6' CornerRadius='3' Background='#EFE6DA' VerticalAlignment='Center'/>
    <Track x:Name='PART_Track' Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' Value='{TemplateBinding Value}' Orientation='{TemplateBinding Orientation}' IsDirectionReversed='{TemplateBinding IsDirectionReversed}'>
     <Track.DecreaseRepeatButton><RepeatButton Command='{x:Static Slider.DecreaseLarge}' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Height='6' CornerRadius='3' Background='#83997A' VerticalAlignment='Center'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
     <Track.Thumb><Thumb Width='18' Height='18'><Thumb.Template><ControlTemplate TargetType='Thumb'><Border CornerRadius='9' Background='#FFFCF6' BorderBrush='#697F61' BorderThickness='2'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
     <Track.IncreaseRepeatButton><RepeatButton Command='{x:Static Slider.IncreaseLarge}' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
    </Track>
   </Grid>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
</ResourceDictionary>";
    }
}
