using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Windows.Media.Imaging;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace AIUsageWidget {
  public class UsageWindow : Window {
    static string Folder { get { return Options.DataDirectory; } }
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    readonly DispatcherTimer follow = new DispatcherTimer();
    readonly DispatcherTimer refresh = new DispatcherTimer();
    Dictionary<string, object> snapshot = new Dictionary<string, object>();
    FrameworkElement content;
    TextBlock sessionValue, weeklyValue, footer, compactValue;
    Border sessionFill, weeklyFill;
    Grid details;
    Button foldButton;
    IntPtr target = IntPtr.Zero;
    double? offsetX;
    double offsetY = 78;
    bool collapsed, dragging;
    bool showProblem;
    readonly bool preview;
    readonly UsageClient client;
    bool? reportedVisible;
    WidgetSettings settings;
    bool settingsOpen;

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr p, uint flags, StringBuilder path, ref uint length);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr p);

    public UsageWindow(bool renderPreview) {
      preview = renderPreview;
      settings = WidgetSettings.Load(Folder);
      client = preview || Options.Provider == "claude" ? null : new UsageClient(Folder);
      Title = Options.DisplayName + " Usage Widget";
      Width = 280; Height = 124;
      WindowStyle = WindowStyle.None;
      AllowsTransparency = true;
      Background = Brushes.Transparent;
      ShowInTaskbar = false;
      ShowActivated = false;
      Topmost = true;
      ResizeMode = ResizeMode.NoResize;
      FontFamily = new FontFamily("맑은 고딕");
      const string xaml = @"<Grid xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
        <Grid.Resources>
          <Style TargetType='Button'>
            <Setter Property='Width' Value='22'/><Setter Property='Height' Value='22'/>
            <Setter Property='Foreground' Value='#8F8377'/><Setter Property='FontSize' Value='14'/>
            <Setter Property='Cursor' Value='Hand'/><Setter Property='Background' Value='Transparent'/>
            <Setter Property='BorderThickness' Value='0'/><Setter Property='Focusable' Value='False'/>
            <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
              <Border x:Name='buttonBg' CornerRadius='8' Background='{TemplateBinding Background}'>
                <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
              </Border><ControlTemplate.Triggers>
                <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='buttonBg' Property='Background' Value='#EDE7DF'/><Setter Property='Foreground' Value='#594B3F'/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
        </Grid.Resources>
        <Border x:Name='Card' Margin='8' Padding='12,8,9,8' CornerRadius='17' Background='#FFFAF9F6' BorderBrush='#E5DED4' BorderThickness='1'>
          <Border.Effect><DropShadowEffect BlurRadius='14' ShadowDepth='3' Opacity='0.14' Color='#6A5645'/></Border.Effect>
          <StackPanel>
            <Grid x:Name='Header' Height='22' Background='Transparent'>
              <Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition Width='Auto'/><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/><ColumnDefinition Width='Auto'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
              <TextBlock x:Name='AppIcon' Text='◈' FontSize='19' Foreground='#73968B' VerticalAlignment='Center' Margin='0,-2,6,0'/>
              <TextBlock x:Name='AppTitle' Grid.Column='1' Text='Codex' FontFamily='Segoe UI' FontSize='13' FontWeight='SemiBold' Foreground='#50463D' VerticalAlignment='Center'/>
              <TextBlock x:Name='CompactValue' Grid.Column='2' FontSize='11' Foreground='#8B7C6D' VerticalAlignment='Center' HorizontalAlignment='Stretch' TextTrimming='CharacterEllipsis' TextAlignment='Right' Margin='3,0,5,0'/>
              <Button x:Name='SettingsButton' Grid.Column='3' Content='⚙' ToolTip='꾸미기' AutomationProperties.Name='위젯 설정'/>
              <Button x:Name='FoldButton' Grid.Column='4' Content='−' ToolTip='작게 접기' AutomationProperties.Name='위젯 접기'/>
              <Button x:Name='CloseButton' Grid.Column='5' Content='×' ToolTip='위젯 종료' AutomationProperties.Name='위젯 종료'/>
            </Grid>
            <Grid x:Name='Details' Margin='0,7,3,0'>
              <Grid.RowDefinitions><RowDefinition Height='36'/><RowDefinition Height='16'/></Grid.RowDefinitions>
              <Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='18'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions>
              <Grid x:Name='SessionGroup'>
                <Grid.RowDefinitions><RowDefinition Height='22'/><RowDefinition Height='4'/></Grid.RowDefinitions>
                <TextBlock x:Name='SessionLabel' Text='사용량' FontSize='12' Foreground='#93877B' VerticalAlignment='Center'/>
                <TextBlock x:Name='SessionValue' Text='—' FontFamily='Segoe UI' FontSize='18' FontWeight='SemiBold' Foreground='#4E655F' HorizontalAlignment='Right' VerticalAlignment='Center'/>
                <Border Grid.Row='1' CornerRadius='2' Background='#E9E8E0'>
                  <Border x:Name='SessionFill' CornerRadius='2' Background='#7AA597' HorizontalAlignment='Left' Width='0'/>
                </Border>
              </Grid>
              <Border x:Name='Separator' Grid.Column='1' Width='1' Height='28' Background='#E8E1D7' VerticalAlignment='Top'/>
              <Grid Grid.Column='2' x:Name='WeeklyGroup'>
                <Grid.RowDefinitions><RowDefinition Height='22'/><RowDefinition Height='4'/></Grid.RowDefinitions>
                <TextBlock x:Name='WeeklyLabel' Text='주간' FontSize='12' Foreground='#93877B' VerticalAlignment='Center'/>
                <TextBlock x:Name='WeeklyValue' Text='—' FontFamily='Segoe UI' FontSize='18' FontWeight='SemiBold' Foreground='#7E6352' HorizontalAlignment='Right' VerticalAlignment='Center'/>
                <Border Grid.Row='1' CornerRadius='2' Background='#EEE5DC'>
                  <Border x:Name='WeeklyFill' CornerRadius='2' Background='#C69B7E' HorizontalAlignment='Left' Width='0'/>
                </Border>
              </Grid>
              <StackPanel x:Name='FooterRow' Grid.Row='1' Grid.ColumnSpan='3' Orientation='Horizontal' VerticalAlignment='Center'>
                <Ellipse x:Name='StatusDot' Width='4' Height='4' Fill='#8EA99A' Margin='0,0,5,0'/>
                <TextBlock x:Name='Footer' Text='사용량 연결 대기' FontSize='10' Foreground='#A09487' MaxWidth='340' TextTrimming='CharacterEllipsis'/>
              </StackPanel>
            </Grid>
          </StackPanel>
        </Border>
      </Grid>";
      content = (FrameworkElement)XamlReader.Parse(xaml);
      Content = content;
      sessionValue = Get<TextBlock>("SessionValue"); weeklyValue = Get<TextBlock>("WeeklyValue");
      footer = Get<TextBlock>("Footer"); compactValue = Get<TextBlock>("CompactValue");
      sessionFill = Get<Border>("SessionFill"); weeklyFill = Get<Border>("WeeklyFill");
      details = Get<Grid>("Details"); foldButton = Get<Button>("FoldButton");
      Get<Button>("CloseButton").Click += delegate { Close(); };
      Get<Button>("SettingsButton").Click += delegate { ShowSettings(); };
      Get<TextBlock>("AppTitle").Text=Options.DisplayName;
      Get<TextBlock>("AppIcon").Text=Options.Provider=="claude" ? "✳" : "◈";
      foldButton.Click += delegate { collapsed = !collapsed; SetCollapsed(); SavePosition(); };
      Get<Grid>("Header").MouseLeftButtonDown += delegate(object s, System.Windows.Input.MouseButtonEventArgs e) {
        DependencyObject source = e.OriginalSource as DependencyObject;
        while (source != null && source != s) {
          if (source is Button) return;
          source = VisualTreeHelper.GetParent(source);
        }
        dragging = true;
        try { DragMove(); } catch (InvalidOperationException) { }
        dragging = false;
        RECT rect;
        if (target != IntPtr.Zero && GetWindowRect(target, out rect)) {
          Point p = FromPixels(rect.Left, rect.Top);
          offsetX = Left - p.X; offsetY = Top - p.Y; SavePosition();
        }
      };
      try {
        Dictionary<string, object> positionSettings = ReadJson(Path.Combine(Folder, "position.json"));
        double value;
        if (Number(positionSettings, "offsetX", out value)) offsetX = value;
        if (Number(positionSettings, "offsetY", out value)) offsetY = value;
        object c; if (positionSettings.TryGetValue("collapsed", out c) && c is bool) collapsed = (bool)c;
      } catch { }
      if (preview) collapsed = false;
      ApplySettings();
      follow.Interval = TimeSpan.FromMilliseconds(400); follow.Tick += delegate { FollowCodex(); };
      refresh.Interval = TimeSpan.FromSeconds(2); refresh.Tick += delegate { ReadUsage(); };
      SourceInitialized += delegate {
        ReadUsage();
        if (!preview) { if (client!=null) client.Start(); follow.Start(); refresh.Start(); }
      };
      content.SizeChanged += delegate { ReadUsage(); };
      Loaded += delegate {
        if (!preview) { Hide(); ReportVisible(false); return; }
        if (!Options.Render) { ShowInTaskbar=true; ShowActivated=true; if(Options.SettingsPreview) ShowSettings(); return; }
        Left=-10000; Top=-10000;
        if(Options.SettingsPreview) { ShowSettings(); Close(); return; }
        DispatcherTimer render = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        render.Tick += delegate {
          render.Stop();
          RenderTargetBitmap bitmap = new RenderTargetBitmap((int)(Width*2), (int)(Height*2), 192, 192, PixelFormats.Pbgra32);
          bitmap.Render(content);
          PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
          using (FileStream stream = File.Create(Path.Combine(Folder, "preview.png"))) encoder.Save(stream);
          Close();
        };
        render.Start();
      };
      Closed += delegate { follow.Stop(); refresh.Stop(); if (client != null) client.Dispose(); if (!preview) ReportVisible(false); };
    }

    T Get<T>(string name) where T : class { return content.FindName(name) as T; }
    static Dictionary<string, object> ReadJson(string path) { return Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8)); }
    static bool Number(Dictionary<string, object> d, string key, out double result) {
      result = 0; object v;
      if (d == null || !d.TryGetValue(key, out v) || v == null || v is bool || v is string) return false;
      try { result = Convert.ToDouble(v, CultureInfo.InvariantCulture); return !Double.IsNaN(result) && !Double.IsInfinity(result); } catch { return false; }
    }
    public static double? Percent(Dictionary<string, object> data, string kind, DateTimeOffset now, out string resetText) {
      resetText = "사용량 정보 대기";
      object rows;
      if (data == null || !data.TryGetValue("windows", out rows) || !(rows is IEnumerable)) return null;
      foreach (object item in (IEnumerable)rows) {
        Dictionary<string, object> row = item as Dictionary<string, object>; object k;
        if (row == null || !row.TryGetValue("kind", out k) || !String.Equals(k as string, kind)) continue;
        double value; if (!Number(row, "percentUsed", out value)) return null;
        object reset; DateTimeOffset time;
        if (row.TryGetValue("resetsAt", out reset) && reset is string && DateTimeOffset.TryParse((string)reset, CultureInfo.InvariantCulture, DateTimeStyles.None, out time)) {
          TimeSpan left = time - now;
          if (left.TotalSeconds <= 0) { resetText = "초기화 후 새 정보 대기"; return null; }
          resetText = left.TotalDays >= 1 ? String.Format("{0}일 {1}시간 뒤 초기화", (int)left.TotalDays, left.Hours)
            : String.Format("{0}시간 {1}분 뒤 초기화", (int)left.TotalHours, left.Minutes);
        } else resetText = "초기화 시각 정보 없음";
        return Math.Min(100, Math.Max(0, value));
      }
      return null;
    }
    void ReadUsage() {
      try { Dictionary<string, object> next = Options.Demo ? Options.DemoSnapshot() : ReadJson(Path.Combine(Folder, "usage.json")); object schema;
        if (next == null || !next.TryGetValue("schema", out schema) || Convert.ToInt32(schema) != 1) throw new FormatException();
        snapshot = next; showProblem = false;
      } catch (FileNotFoundException) { showProblem = snapshot.Count > 0; } catch { showProblem = true; }
      List<Dictionary<string,object>> rows = DisplayRows(snapshot, settings.Windows);
      if(Options.Provider=="claude") foreach(Dictionary<string,object> row in rows) {
        if(!row.ContainsKey("label")) row["label"]=Convert.ToString(row["kind"])=="five_hour" ? "5시간" : "주간";
      }
      bool single = rows.Count <= 1;
      Grid.SetColumnSpan(Get<Grid>("SessionGroup"), single ? 3 : 1);
      Get<Grid>("WeeklyGroup").Visibility = single ? Visibility.Collapsed : Visibility.Visible;
      Get<Border>("Separator").Visibility = single ? Visibility.Collapsed : Visibility.Visible;
      string firstKind = rows.Count > 0 ? Convert.ToString(rows[0]["kind"]) : "primary";
      string secondKind = rows.Count > 1 ? Convert.ToString(rows[1]["kind"]) : "secondary";
      string firstLabel = rows.Count > 0 ? Convert.ToString(rows[0]["label"]) : "사용량";
      string secondLabel = rows.Count > 1 ? Convert.ToString(rows[1]["label"]) : "";
      Get<TextBlock>("SessionLabel").Text = firstLabel;
      Get<TextBlock>("WeeklyLabel").Text = secondLabel;
      string firstReset="사용량 정보 대기", secondReset="사용량 정보 대기";
      double? first = rows.Count>0 ? Percent(snapshot, firstKind, DateTimeOffset.Now, out firstReset) : (double?)null;
      double? second = rows.Count>1 ? Percent(snapshot, secondKind, DateTimeOffset.Now, out secondReset) : (double?)null;
      bool remaining=settings.Display=="remaining";
      first=WidgetSettings.DisplayPercent(first,remaining); second=WidgetSettings.DisplayPercent(second,remaining);
      SetGauge(sessionValue, sessionFill, first, settings.Accent);
      SetGauge(weeklyValue, weeklyFill, second, settings.Accent);
      Get<Grid>("SessionGroup").ToolTip = (remaining ? "남은 비율 · " : "사용한 비율 · ") + firstReset;
      Get<Grid>("WeeklyGroup").ToolTip = (remaining ? "남은 비율 · " : "사용한 비율 · ") + secondReset;
      compactValue.Text = collapsed ? Format(first) + (single ? "" : " · " + Format(second)) : "";
      string mode=remaining ? "남은 비율" : "사용한 비율";
      string status = Options.Provider=="claude" ? "Claude Code 연결 대기" : "Codex 사용량 연결 중";
      double sampled;
      bool stale = true;
      if (Number(snapshot, "sampledAt", out sampled)) {
        double age = Math.Max(0, UsageData.UnixMilliseconds() / 1000 - sampled/1000);
        status = age < 60 ? mode+" · 방금 확인" : String.Format(mode+" · {0}분 전 확인", (int)(age/60));
        stale = age >= 300;
      }
      object error;
      if (snapshot.TryGetValue("error", out error) && error is string && ((string)error).Length > 0) { status = (string)error; stale = true; }
      if (showProblem) { status = "연결 확인 중 · 마지막 값"; stale = true; }
      footer.Text = status;
      footer.ToolTip = status + "\n2분마다 확인 · 추가 AI 호출 없음\n설정에서 사용한 비율과 남은 비율을 바꿀 수 있습니다.";
      Get<System.Windows.Shapes.Ellipse>("StatusDot").Fill = ColorBrush(stale ? "#C9A273" : "#8EA99A");
    }
    static string Format(double? v) { return v.HasValue ? v.Value.ToString("0.#", CultureInfo.InvariantCulture) + "%" : "—"; }
    static Brush ColorBrush(string color) { return (Brush)new BrushConverter().ConvertFromString(color); }
    void SetGauge(TextBlock label, Border fill, double? value, string color) {
      label.Text = Format(value);
      fill.Background = ColorBrush(value.HasValue && (settings.Display=="remaining" ? value.Value<=5 : value.Value>=95) ? "#C8867C" : color);
      double track = ((FrameworkElement)fill.Parent).ActualWidth;
      double width = value.HasValue ? Math.Max(0, track * value.Value / 100) : 0;
      if (Math.Abs(fill.Width - width) > 0.2) {
        fill.BeginAnimation(WidthProperty, new DoubleAnimation(width, TimeSpan.FromMilliseconds(240)) { EasingFunction = new QuadraticEase() });
      }
    }
    void SetCollapsed() {
      Height = (collapsed ? 60 : settings.ShowFooter ? 124 : 108) * settings.TextScale;
      details.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
      foldButton.Content = collapsed ? "+" : "−";
      foldButton.ToolTip = collapsed ? "펼치기" : "작게 접기";
      System.Windows.Automation.AutomationProperties.SetName(foldButton, collapsed ? "위젯 펼치기" : "위젯 접기");
      ReadUsage();
    }
    Point FromPixels(double x, double y) {
      PresentationSource source = PresentationSource.FromVisual(this);
      return source == null ? new Point(x,y) : source.CompositionTarget.TransformFromDevice.Transform(new Point(x,y));
    }
    public static bool IsCodexPath(string path) {
      string normalized = path.Replace('/', '\\');
      string name = Path.GetFileName(normalized);
      return (String.Equals(name, "ChatGPT.exe", StringComparison.OrdinalIgnoreCase) || String.Equals(name, "Codex.exe", StringComparison.OrdinalIgnoreCase))
        && normalized.IndexOf("\\OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) >= 0;
    }
    bool IsCodex(IntPtr h) {
      uint pid; GetWindowThreadProcessId(h, out pid);
      IntPtr p = OpenProcess(0x1000, false, pid); if (p == IntPtr.Zero) return false;
      try { uint length = 32768; StringBuilder path = new StringBuilder((int)length);
        return QueryFullProcessImageName(p, 0, path, ref length) && MatchesProvider(path.ToString());
      } finally { CloseHandle(p); }
    }
    void FollowCodex() {
      if (dragging || settingsOpen || preview) return;
      IntPtr foreground = GetForegroundWindow(); uint pid; GetWindowThreadProcessId(foreground, out pid);
      if (pid == (uint)Process.GetCurrentProcess().Id) return;
      if (!IsCodex(foreground) || IsIconic(foreground)) { if (IsVisible) Hide(); ReportVisible(false); return; }
      target = foreground;
      RECT rect; if (!GetWindowRect(target, out rect)) return;
      Point top = FromPixels(rect.Left, rect.Top), bottom = FromPixels(rect.Right, rect.Bottom);
      double dx = offsetX ?? Math.Max(8, bottom.X-top.X-Width-16);
      Left = top.X + Math.Min(Math.Max(8, dx), Math.Max(8, bottom.X-top.X-Width-8));
      Top = top.Y + Math.Min(Math.Max(32, offsetY), Math.Max(32, bottom.Y-top.Y-Height-8));
      if (!IsVisible) Show();
      ReportVisible(true);
    }
    void ReportVisible(bool visible) {
      if (reportedVisible == visible) return;
      reportedVisible = visible;
      try { File.WriteAllText(Path.Combine(Folder, "widget-status.json"), Json.Serialize(new {
        visible=visible, target=Options.Provider+" desktop", updatedAt=UsageData.UnixMilliseconds(), processId=Process.GetCurrentProcess().Id
      }), Encoding.UTF8); } catch { }
    }
    void SavePosition() {
      try { File.WriteAllText(Path.Combine(Folder, "position.json"), Json.Serialize(new { offsetX=offsetX, offsetY=offsetY, collapsed=collapsed }), Encoding.UTF8); } catch { }
    }

    public static bool MatchesProvider(string path) {
      return Options.Provider=="claude" ? String.Equals(Path.GetFileName(path),"claude.exe",StringComparison.OrdinalIgnoreCase) : IsCodexPath(path);
    }
    public static List<Dictionary<string,object>> DisplayRows(Dictionary<string,object> snapshot,string mode) {
      List<Dictionary<string,object>> result=new List<Dictionary<string,object>>();
      foreach(Dictionary<string,object> row in UsageData.Rows(snapshot)) {
        double duration; string kind=Convert.ToString(row.ContainsKey("kind") ? row["kind"] : "");
        if(kind.Length==0) continue;
        bool weekly=kind=="seven_day" || (UsageData.Number(row,"windowDurationMins",out duration) && duration>=10080);
        if(mode=="weekly" && !weekly || mode=="session" && weekly) continue;
        result.Add(new Dictionary<string,object>(row));
      }
      return result;
    }
    void ApplySettings() {
      settings.Validate();
      content.Width=settings.CardWidth;
      content.LayoutTransform=new ScaleTransform(settings.TextScale,settings.TextScale);
      Width=settings.CardWidth*settings.TextScale; Opacity=settings.Opacity;
      bool night=settings.Theme=="night", mint=settings.Theme=="mint";
      Get<Border>("Card").Background=ColorBrush(night ? "#252C2C" : mint ? "#F2F8F5" : "#FAF9F6");
      Get<Border>("Card").BorderBrush=ColorBrush(night ? "#465451" : mint ? "#D3E3DC" : "#E5DED4");
      PaintText(content,ColorBrush(night ? "#D4DEDA" : "#776B60"));
      Get<TextBlock>("AppTitle").Foreground=ColorBrush(night ? "#F3F5F2" : "#50463D");
      sessionValue.Foreground=weeklyValue.Foreground=ColorBrush(night ? "#E0EDE6" : "#4E655F");
      Get<TextBlock>("AppIcon").Foreground=ColorBrush(settings.Accent);
      foreach(Border fill in new Border[]{sessionFill,weeklyFill}) ((Border)fill.Parent).Background=ColorBrush(night ? "#42504C" : "#E3E8DF");
      Get<StackPanel>("FooterRow").Visibility=settings.ShowFooter ? Visibility.Visible : Visibility.Collapsed;
      details.RowDefinitions[1].Height=new GridLength(settings.ShowFooter ? 16 : 0);
      footer.MaxWidth=settings.CardWidth-50;
      SetCollapsed();
    }
    static void PaintText(DependencyObject parent,Brush color) {
      TextBlock text=parent as TextBlock; if(text!=null) text.Foreground=color;
      for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++) PaintText(VisualTreeHelper.GetChild(parent,i),color);
    }
    void ShowSettings() {
      if(settingsOpen) return;
      settingsOpen=true;
      try {
        WidgetSettings previous=settings.Copy();
        SettingsWindow dialog=new SettingsWindow(settings.Copy(),delegate(WidgetSettings value){ settings=value; ApplySettings(); });
        dialog.Owner=this;
        if(Options.Render && Options.SettingsPreview) dialog.RenderAndClose(Path.Combine(Folder,"settings-preview.png"));
        bool? saved=dialog.ShowDialog();
        if(saved==true) { settings=dialog.Value.Copy(); settings.Save(Folder); }
        else settings=previous;
        ApplySettings();
      } finally { settingsOpen=false; }
    }
    [STAThread] public static int Main(string[] args) {
      try {
        Options.Parse(args); Directory.CreateDirectory(Folder);
        if (Array.IndexOf(args,"--self-test")>=0) { int data=UsageData.SelfTest(Folder); return data==0 ? WidgetSettings.SelfTest(Folder) : data; }
        if(Options.CheckConnection) {
          double baseline=UsageData.UnixMilliseconds(); bool success=false;
          using(UsageClient reader=new UsageClient(Folder)) {
            reader.Start(); Stopwatch timeout=Stopwatch.StartNew();
            while(timeout.Elapsed.TotalSeconds<30) {
              Thread.Sleep(200);
              try { Dictionary<string,object> data=ReadJson(Path.Combine(Folder,"usage.json")); double sampled;
                if(Number(data,"sampledAt",out sampled) && sampled>=baseline && UsageData.Rows(data).Count>0) { success=true; break; }
                if(data.ContainsKey("error") && data["error"] is string) break;
              } catch { }
            }
          }
          File.WriteAllText(Path.Combine(Folder,"connection-verification.json"),UsageData.Stringify(new { success=success,source="codex-app-server" })); return success ? 0 : 1;
        }
        bool created;
        string instance="Local\\AIUsageWidget-"+Options.Provider+(Options.Preview ? "-preview" : "");
        using(Mutex mutex=new Mutex(true,instance,out created)) {
          if(!created) return 0;
          Application app=new Application { ShutdownMode=ShutdownMode.OnMainWindowClose };
          app.Run(new UsageWindow(Options.Preview)); return 0;
        }
      } catch(Exception ex) {
        try { File.WriteAllText(Path.Combine(Folder,"native-error.txt"),ex.GetType().Name+": "+ex.Message); } catch { }
        return 1;
      }
    }
  }
}

namespace AIUsageWidget {
  public static class UsageData {
    public static Dictionary<string,object> Parse(string json) { return new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(json); }
    public static string Stringify(object value) { return new JavaScriptSerializer().Serialize(value); }
    public static double UnixMilliseconds() { return (DateTimeOffset.UtcNow-new DateTimeOffset(1970,1,1,0,0,0,TimeSpan.Zero)).TotalMilliseconds; }
    public static bool Number(Dictionary<string,object> obj, string key, out double number) {
      number=0; object v;
      if (obj==null || !obj.TryGetValue(key,out v) || v==null || v is bool || v is string) return false;
      try { number=Convert.ToDouble(v,CultureInfo.InvariantCulture); return !Double.IsNaN(number) && !Double.IsInfinity(number); } catch { return false; }
    }
    static Dictionary<string,object> Child(Dictionary<string,object> obj, string key) {
      object v; return obj!=null && obj.TryGetValue(key,out v) ? v as Dictionary<string,object> : null;
    }
    public static List<Dictionary<string,object>> Rows(Dictionary<string,object> data) {
      List<Dictionary<string,object>> result=new List<Dictionary<string,object>>(); object v;
      if (data!=null && data.TryGetValue("windows",out v) && v is IEnumerable) {
        foreach (object row in (IEnumerable)v) if (row is Dictionary<string,object>) result.Add((Dictionary<string,object>)row);
      }
      return result;
    }
    public static Dictionary<string,object> Normalize(Dictionary<string,object> result) {
      Dictionary<string,object> buckets=Child(result,"rateLimitsByLimitId");
      Dictionary<string,object> bucket=Child(buckets,"codex") ?? Child(result,"rateLimits");
      List<Dictionary<string,object>> rows=new List<Dictionary<string,object>>();
      foreach(string kind in new string[]{"primary","secondary"}) {
        Dictionary<string,object> row=Child(bucket,kind); double percent,duration;
        if (!Number(row,"usedPercent",out percent) || !Number(row,"windowDurationMins",out duration) || duration<=0) continue;
        string label=duration==10080 ? "주간" : duration>=1440 && duration%1440==0 ? (duration/1440).ToString("0",CultureInfo.InvariantCulture)+"일"
          : duration>=60 && duration%60==0 ? (duration/60).ToString("0",CultureInfo.InvariantCulture)+"시간" : duration.ToString("0",CultureInfo.InvariantCulture)+"분";
        Dictionary<string,object> safe=new Dictionary<string,object> { {"kind",kind}, {"label",label}, {"percentUsed",Math.Max(0,Math.Min(100,percent))}, {"windowDurationMins",duration} };
        double reset;
        if (Number(row,"resetsAt",out reset)) {
          try { safe["resetsAt"]=new DateTimeOffset(1970,1,1,0,0,0,TimeSpan.Zero).AddSeconds(reset).ToString("o",CultureInfo.InvariantCulture); } catch (ArgumentOutOfRangeException) { }
        }
        rows.Add(safe);
      }
      rows.Sort(delegate(Dictionary<string,object> a,Dictionary<string,object> b) { return Convert.ToDouble(a["windowDurationMins"]).CompareTo(Convert.ToDouble(b["windowDurationMins"])); });
      return new Dictionary<string,object> { {"schema",1}, {"source","codex-app-server"}, {"sampledAt",UnixMilliseconds()}, {"windows",rows}, {"error",rows.Count==0 ? "계정 사용량 정보 없음" : null} };
    }
    public static void Save(string folder, Dictionary<string,object> data) {
      string path=Path.Combine(folder,"usage.json"), temp=path+".tmp";
      File.WriteAllText(temp,Stringify(data),Encoding.UTF8);
      if (File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path);
    }
    public static int SelfTest(string folder) {
      List<string> checks=new List<string>();
      Action<bool,string> check=delegate(bool success,string name) { if(!success) throw new Exception("Verification failed: "+name); checks.Add(name); };
      try {
        check(Rows(Normalize(Parse("{}"))).Count==0,"missing limits never become zero");
        Dictionary<string,object> one=Normalize(Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":0,\"windowDurationMins\":10080}}}"));
        check(Rows(one).Count==1 && (double)Rows(one)[0]["percentUsed"]==0 && (string)Rows(one)[0]["label"]=="주간","weekly-only and real zero");
        Dictionary<string,object> two=Normalize(Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":12,\"windowDurationMins\":10080},\"secondary\":{\"usedPercent\":34,\"windowDurationMins\":300}}}"));
        check(Rows(two).Count==2 && (string)Rows(two)[0]["label"]=="5시간","two windows ordered by duration");
        Dictionary<string,object> mapped=Normalize(Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":99,\"windowDurationMins\":300}},\"rateLimitsByLimitId\":{\"codex\":{\"primary\":{\"usedPercent\":6,\"windowDurationMins\":10080}},\"other\":{\"primary\":{\"usedPercent\":90,\"windowDurationMins\":300}}}}"));
        check(Rows(mapped).Count==1 && (double)Rows(mapped)[0]["percentUsed"]==6,"prefer Codex bucket");
        check(Rows(Normalize(Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":true,\"windowDurationMins\":300}}}"))).Count==0,"invalid percentage rejected");
        string reset;
        check(UsageWindow.Percent(Parse("{\"windows\":[{\"kind\":\"primary\",\"percentUsed\":86,\"resetsAt\":\"2026-10-26T00:00:00Z\"}]}"),"primary",new DateTimeOffset(2026,10,27,0,0,0,TimeSpan.Zero),out reset)==null,"expired reset waits for fresh usage");
        check(UsageWindow.IsCodexPath(@"C:\Program Files\WindowsApps\OpenAI.Codex_1_x64__abc\app\ChatGPT.exe") && !UsageWindow.IsCodexPath(@"C:\Tools\Codex\bin\version\codex.exe") && !UsageWindow.IsCodexPath(@"C:\Program Files\WindowsApps\OpenAI.ChatGPT-Desktop_1\app\ChatGPT.exe"),"desktop app distinguished from CLI and ChatGPT");
        check(!Stringify(mapped).Contains("credits") && !Stringify(mapped).Contains("accountId"),"snapshot contains only usage fields");
        File.WriteAllText(Path.Combine(folder,"verification.json"),Stringify(new { success=true, count=checks.Count, checks=checks })); return 0;
      } catch(Exception ex) { File.WriteAllText(Path.Combine(folder,"verification.json"),Stringify(new { success=false, count=checks.Count, error=ex.Message })); return 1; }
    }
  }

  // Uses only initialize, initialized, and account/rateLimits/read. No turn or thread is created.
  public sealed class UsageClient : IDisposable {
    readonly string folder;
    readonly ManualResetEvent stopped=new ManualResetEvent(false);
    readonly object sync=new object();
    Process child;
    BlockingCollection<Dictionary<string,object>> messages;
    Thread worker;
    bool disposed;
    int requestId;
    public UsageClient(string path) { folder=path; }
    public void Start() { worker=new Thread(Run); worker.IsBackground=true; worker.Name="Codex usage reader"; worker.Start(); }
    static string FindExecutable() {
      string bin=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenAI","Codex","bin");
      if (!Directory.Exists(bin)) throw new FileNotFoundException();
      DirectoryInfo[] versions=new DirectoryInfo(bin).GetDirectories();
      Array.Sort(versions,delegate(DirectoryInfo a,DirectoryInfo b) { return b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc); });
      foreach (DirectoryInfo version in versions) { string exe=Path.Combine(version.FullName,"codex.exe"); if(File.Exists(exe)) return exe; }
      throw new FileNotFoundException();
    }
    void Connect() {
      messages=new BlockingCollection<Dictionary<string,object>>();
      Process process=new Process();
      process.StartInfo=new ProcessStartInfo(FindExecutable(),"app-server --listen stdio://") {
        UseShellExecute=false, CreateNoWindow=true, RedirectStandardInput=true, RedirectStandardOutput=true, RedirectStandardError=true,
        StandardOutputEncoding=Encoding.UTF8, StandardErrorEncoding=Encoding.UTF8, WorkingDirectory=folder
      };
      BlockingCollection<Dictionary<string,object>> queue=messages;
      process.OutputDataReceived+=delegate(object s,DataReceivedEventArgs e) {
        if (e.Data==null) return;
        try { queue.Add(UsageData.Parse(e.Data)); } catch { }
      };
      process.ErrorDataReceived+=delegate { }; // Do not persist raw diagnostic or authentication content.
      lock(sync) { if(disposed) { process.Dispose(); throw new OperationCanceledException(); } process.Start(); child=process; }
      process.BeginOutputReadLine(); process.BeginErrorReadLine();
      process.StandardInput.AutoFlush=true;
      int id=++requestId;
      Send(new { id=id, method="initialize", @params=new { clientInfo=new { name="ai_usage_widget", title="AI Usage Widget", version="0.2.0" } } });
      WaitResponse(id);
      Send(new { method="initialized", @params=new { } });
    }
    void Send(object message) {
      lock(sync) { if(disposed || child==null || child.HasExited) throw new OperationCanceledException(); child.StandardInput.WriteLine(UsageData.Stringify(message)); }
    }
    Dictionary<string,object> WaitResponse(int id) {
      Stopwatch timeout=Stopwatch.StartNew();
      while(timeout.Elapsed.TotalSeconds<25) {
        if(stopped.WaitOne(0)) throw new OperationCanceledException();
        Dictionary<string,object> message;
        if(!messages.TryTake(out message,200)) { lock(sync) { if(child==null || child.HasExited) throw new IOException(); } continue; }
        object responseId,method;
        if(message.TryGetValue("id",out responseId) && message.TryGetValue("method",out method)) {
          Send(new { id=responseId, error=new { code=-32601, message="Usage companion supports read-only account limits only" } }); continue;
        }
        if(!message.TryGetValue("id",out responseId) || Convert.ToString(responseId,CultureInfo.InvariantCulture)!=id.ToString(CultureInfo.InvariantCulture)) continue;
        if(message.ContainsKey("error")) throw new InvalidOperationException("rate_limits_unavailable");
        object result;
        if(message.TryGetValue("result",out result) && result is Dictionary<string,object>) return (Dictionary<string,object>)result;
        throw new InvalidDataException();
      }
      throw new TimeoutException();
    }
    void Disconnect() {
      lock(sync) {
        if(child==null) return;
        try { if(!child.HasExited) child.Kill(); } catch { }
        child.Dispose(); child=null;
      }
    }
    void RecordError(string message) {
      try {
        Dictionary<string,object> data=File.Exists(Path.Combine(folder,"usage.json")) ? UsageData.Parse(File.ReadAllText(Path.Combine(folder,"usage.json"),Encoding.UTF8)) : new Dictionary<string,object> { {"schema",1},{"windows",new object[0]} };
        data["error"]=message; UsageData.Save(folder,data);
      } catch { }
    }
    void Run() {
      try {
        while(!stopped.WaitOne(0)) {
          int wait=120000;
          try {
            if(child==null || child.HasExited) { Disconnect(); Connect(); }
            int id=++requestId; Send(new { id=id, method="account/rateLimits/read" });
            Dictionary<string,object> snapshot=UsageData.Normalize(WaitResponse(id));
            UsageData.Save(folder,snapshot);
          } catch(OperationCanceledException) { if(stopped.WaitOne(0)) break; Disconnect(); RecordError("Codex 연결 확인 중 · 5분 뒤 재시도"); wait=300000; }
          catch(FileNotFoundException) { Disconnect(); RecordError("Codex 실행 파일을 찾을 수 없음"); wait=300000; }
          catch { Disconnect(); RecordError("사용량 연결 확인 중 · 5분 뒤 재시도"); wait=300000; }
          if(stopped.WaitOne(wait)) break;
        }
      } finally { Disconnect(); }
    }
    public void Dispose() { lock(sync) { disposed=true; stopped.Set(); } Disconnect(); if(worker!=null) worker.Join(1500); }
  }
}
