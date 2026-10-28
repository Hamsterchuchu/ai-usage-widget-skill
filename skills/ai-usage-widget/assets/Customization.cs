using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AIUsageWidget {
  public static class Options {
    public static string Provider="codex", DataDirectory;
    public static bool Preview, Render, Demo, SettingsPreview, CheckConnection;
    public static string DisplayName { get { return Provider=="claude" ? "Claude" : "Codex"; } }
    public static void Parse(string[] args) {
      string folder=null;
      for(int i=0;i<args.Length;i++) {
        string arg=args[i];
        if(arg=="--provider" || arg=="--data-dir") {
          if(i+1>=args.Length) throw new ArgumentException("Missing value for "+arg);
          if(arg=="--provider") Provider=args[++i].ToLowerInvariant(); else folder=args[++i];
        } else if(arg=="--preview") Preview=true;
        else if(arg=="--render-preview") { Preview=true; Render=true; }
        else if(arg=="--settings-preview") { Preview=true; SettingsPreview=true; }
        else if(arg=="--demo") Demo=true;
        else if(arg=="--check-connection") CheckConnection=true;
        else if(arg!="--self-test") throw new ArgumentException("Unknown option: "+arg);
      }
      if(Provider!="claude" && Provider!="codex") throw new ArgumentException("Provider must be codex or claude");
      if(Demo && !Preview) throw new ArgumentException("Demo is available only in preview mode");
      if(CheckConnection && Provider!="codex") throw new ArgumentException("Connection check currently supports codex only");
      DataDirectory=Path.GetFullPath(folder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AIUsageWidget",Provider));
    }
    public static Dictionary<string,object> DemoSnapshot() {
      return UsageData.Parse("{\"schema\":1,\"sampledAt\":"+UsageData.UnixMilliseconds().ToString("0",CultureInfo.InvariantCulture)+",\"windows\":[{\"kind\":\"primary\",\"label\":\"5시간\",\"windowDurationMins\":300,\"percentUsed\":17},{\"kind\":\"secondary\",\"label\":\"주간\",\"windowDurationMins\":10080,\"percentUsed\":64}],\"error\":null}");
    }
  }

  public sealed class WidgetSettings {
    public string Theme="warm", Accent="#7AA597", Display="used", Windows="all";
    public double TextScale=1, CardWidth=280, Opacity=1;
    public bool ShowFooter=true;
    static double Clamp(double value,double min,double max,double fallback) { return Double.IsNaN(value)||Double.IsInfinity(value) ? fallback : Math.Max(min,Math.Min(max,value)); }
    public static bool ValidColor(string value) { return value!=null && Regex.IsMatch(value,@"\A#[0-9a-fA-F]{6}\z"); }
    public void Validate() {
      if(Theme!="warm" && Theme!="mint" && Theme!="night") Theme="warm";
      if(Display!="used" && Display!="remaining") Display="used";
      if(Windows!="all" && Windows!="weekly" && Windows!="session") Windows="all";
      if(!ValidColor(Accent)) Accent="#7AA597";
      TextScale=Clamp(TextScale,.9,1.5,1); CardWidth=Clamp(CardWidth,260,420,280); Opacity=Clamp(Opacity,.65,1,1);
    }
    public WidgetSettings Copy() { return (WidgetSettings)MemberwiseClone(); }
    public static double? DisplayPercent(double? used,bool remaining) { return used.HasValue ? remaining ? 100-used.Value : used.Value : (double?)null; }
    public void Save(string folder) {
      Validate(); string path=Path.Combine(folder,"settings.json"), temp=path+".tmp";
      File.WriteAllText(temp,UsageData.Stringify(this),Encoding.UTF8);
      if(File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path);
    }
    public static WidgetSettings Load(string folder) {
      try {
        WidgetSettings result=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<WidgetSettings>(File.ReadAllText(Path.Combine(folder,"settings.json"),Encoding.UTF8));
        if(result==null) return new WidgetSettings(); result.Validate(); return result;
      } catch { return new WidgetSettings(); }
    }
    public static int SelfTest(string folder) {
      List<string> checks=new List<string>();
      Action<bool,string> check=delegate(bool ok,string name) { if(!ok) throw new Exception(name); checks.Add(name); };
      try {
        WidgetSettings invalid=new WidgetSettings { Theme="invalid", Display="invalid", Windows="invalid", Accent="red; markup", TextScale=99, CardWidth=-1, Opacity=Double.NaN };
        invalid.Validate();
        check(invalid.Theme=="warm" && invalid.Display=="used" && invalid.Windows=="all" && invalid.Accent=="#7AA597","invalid choices use safe defaults");
        check(invalid.TextScale==1.5 && invalid.CardWidth==260 && invalid.Opacity==1,"dimensions and opacity bounded");
        check(ValidColor("#Aa77FF") && !ValidColor("#FFFFFF\n") && !ValidColor("<xml>"),"only hex colors accepted");
        WidgetSettings saved=new WidgetSettings { Theme="night",Display="remaining",Accent="#BBAADD",TextScale=1.25,ShowFooter=false };
        string temp=Path.Combine(folder,"settings-test"); Directory.CreateDirectory(temp); saved.Save(temp);
        WidgetSettings loaded=Load(temp);
        check(loaded.Theme=="night" && loaded.Display=="remaining" && loaded.Accent==saved.Accent && loaded.TextScale==1.25 && !loaded.ShowFooter,"settings persist across reload");
        WidgetSettings copy=saved.Copy(); copy.Theme="mint"; check(saved.Theme=="night","cancel snapshot independent of live edits");
        Dictionary<string,object> snapshot=Options.DemoSnapshot();
        check(UsageWindow.DisplayRows(snapshot,"weekly").Count==1 && UsageWindow.DisplayRows(snapshot,"session").Count==1 && UsageWindow.DisplayRows(snapshot,"all").Count==2,"window visibility filters");
        check(UsageWindow.DisplayRows(UsageData.Parse("{\"windows\":[{\"kind\":\"primary\",\"label\":\"주간\",\"windowDurationMins\":10080,\"percentUsed\":6}]}"),"session").Count==0,"missing session window not invented");
        check(DisplayPercent(17,true)==83 && DisplayPercent(0,true)==100 && DisplayPercent(null,true)==null && DisplayPercent(17,false)==17,"used and remaining preserve zero and unknown");
        File.WriteAllText(Path.Combine(folder,"customization-verification.json"),UsageData.Stringify(new { success=true,count=checks.Count,checks=checks })); return 0;
      } catch(Exception error) {
        File.WriteAllText(Path.Combine(folder,"customization-verification.json"),UsageData.Stringify(new { success=false,count=checks.Count,error=error.Message })); return 1;
      }
    }
  }

  public sealed class SettingsWindow : Window {
    public WidgetSettings Value;
    readonly Action<WidgetSettings> changed;
    ComboBox theme, display, windows;
    Slider scale, width, opacity;
    TextBox accent;
    CheckBox footer;
    TextBlock error;
    bool ready;
    public SettingsWindow(WidgetSettings initial,Action<WidgetSettings> onChange) {
      Value=initial; changed=onChange;
      Title=Options.DisplayName+" 위젯 꾸미기"; Width=440; Height=660;
      WindowStartupLocation=WindowStartupLocation.CenterScreen; ResizeMode=ResizeMode.NoResize;
      Background=(Brush)new BrushConverter().ConvertFromString("#FAF9F6"); Foreground=(Brush)new BrushConverter().ConvertFromString("#50463D");
      FontFamily=new FontFamily("Segoe UI"); FontSize=13;
      StackPanel body=new StackPanel { Margin=new Thickness(26,20,26,18), Background=Background };
      body.Children.Add(new TextBlock { Text="내 취향대로, 사용량 위젯",FontSize=22,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,5) });
      body.Children.Add(new TextBlock { Text="바꾼 모습은 즉시 반영돼요. 저장하면 다음에도 유지돼요.",FontSize=11,Foreground=Brushes.Gray,Margin=new Thickness(0,0,0,14) });
      theme=Choices(body,"테마",new string[]{"따뜻한 크림","은은한 민트","차분한 다크"},new string[]{"warm","mint","night"},initial.Theme);
      scale=Range(body,"글씨·배율",.9,1.5,initial.TextScale,.05,"0.00");
      width=Range(body,"카드 너비",260,420,initial.CardWidth,10,"0");
      opacity=Range(body,"투명도",65,100,initial.Opacity*100,5,"0");
      Label(body,"막대 포인트 색상");
      accent=new TextBox { Text=initial.Accent,Height=28,Padding=new Thickness(6,3,6,3),MaxLength=7 }; body.Children.Add(accent);
      display=Choices(body,"비율 표시",new string[]{"사용한 양","남은 양"},new string[]{"used","remaining"},initial.Display);
      windows=Choices(body,"표시할 한도",new string[]{"제공되는 한도 모두","짧은 시간 한도만","주간 한도만"},new string[]{"all","session","weekly"},initial.Windows);
      footer=new CheckBox { Content="아래쪽 갱신 상태 표시", IsChecked=initial.ShowFooter, Margin=new Thickness(0,12,0,2) }; body.Children.Add(footer);
      error=new TextBlock { Foreground=Brushes.IndianRed,FontSize=11,Height=24,TextWrapping=TextWrapping.Wrap }; body.Children.Add(error);
      StackPanel buttons=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right };
      Button reset=ActionButton("기본값"), cancel=ActionButton("취소"), save=ActionButton("저장");
      buttons.Children.Add(reset); buttons.Children.Add(cancel); buttons.Children.Add(save); body.Children.Add(buttons);
      Content=body;
      theme.SelectionChanged+=delegate { Update(); }; display.SelectionChanged+=delegate { Update(); }; windows.SelectionChanged+=delegate { Update(); };
      scale.ValueChanged+=delegate { Update(); }; width.ValueChanged+=delegate { Update(); }; opacity.ValueChanged+=delegate { Update(); };
      accent.TextChanged+=delegate { Update(); }; footer.Checked+=delegate { Update(); }; footer.Unchecked+=delegate { Update(); };
      reset.Click+=delegate { ready=false; theme.SelectedIndex=0; scale.Value=1; width.Value=280; opacity.Value=100; accent.Text="#7AA597"; display.SelectedIndex=0; windows.SelectedIndex=0; footer.IsChecked=true; ready=true; Update(); };
      cancel.Click+=delegate { DialogResult=false; };
      save.Click+=delegate { if(Update()) DialogResult=true; };
      ready=true;
    }
    static Button ActionButton(string text) { return new Button { Content=text,MinWidth=64,Height=30,Margin=new Thickness(6,2,0,0),Padding=new Thickness(10,0,10,0) }; }
    static void Label(StackPanel body,string text) { body.Children.Add(new TextBlock { Text=text,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,9,0,4) }); }
    static ComboBox Choices(StackPanel body,string label,string[] labels,string[] values,string current) {
      Label(body,label); ComboBox combo=new ComboBox { Height=28,SelectedValuePath="Tag" };
      for(int i=0;i<labels.Length;i++) combo.Items.Add(new ComboBoxItem { Content=labels[i],Tag=values[i] });
      combo.SelectedValue=current; body.Children.Add(combo); return combo;
    }
    static Slider Range(StackPanel body,string name,double min,double max,double value,double step,string format) {
      Grid row=new Grid { Margin=new Thickness(0,9,0,1) };
      row.Children.Add(new TextBlock { Text=name,FontWeight=FontWeights.SemiBold });
      TextBlock readout=new TextBlock { Text=value.ToString(format,CultureInfo.InvariantCulture),HorizontalAlignment=HorizontalAlignment.Right,Foreground=Brushes.Gray }; row.Children.Add(readout); body.Children.Add(row);
      Slider slider=new Slider { Minimum=min,Maximum=max,Value=value,TickFrequency=step,IsSnapToTickEnabled=true,Height=22,Margin=new Thickness(0,2,0,0) };
      slider.ValueChanged+=delegate { readout.Text=slider.Value.ToString(format,CultureInfo.InvariantCulture); };
      body.Children.Add(slider); return slider;
    }
    bool Update() {
      if(!ready) return false;
      if(!WidgetSettings.ValidColor(accent.Text)) { error.Text="색상은 #7AA597처럼 6자리 코드로 입력해 주세요."; return false; }
      Value=new WidgetSettings { Theme=Convert.ToString(theme.SelectedValue),Accent=accent.Text,TextScale=scale.Value,CardWidth=width.Value,Opacity=opacity.Value/100,Display=Convert.ToString(display.SelectedValue),Windows=Convert.ToString(windows.SelectedValue),ShowFooter=footer.IsChecked==true };
      Value.Validate(); error.Text=""; changed(Value.Copy()); return true;
    }
    public void RenderAndClose(string path) {
      WindowStartupLocation=WindowStartupLocation.Manual; Left=-10000; Top=-10000;
      Loaded+=delegate {
        DispatcherTimer timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(400) };
        timer.Tick+=delegate {
          timer.Stop(); FrameworkElement content=(FrameworkElement)Content;
          RenderTargetBitmap bitmap=new RenderTargetBitmap((int)(content.ActualWidth*2),(int)(content.ActualHeight*2),192,192,PixelFormats.Pbgra32);
          DrawingVisual visual=new DrawingVisual(); using(DrawingContext drawing=visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(content),null,new Rect(0,0,content.ActualWidth,content.ActualHeight)); bitmap.Render(visual);
          PngBitmapEncoder encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using(FileStream stream=File.Create(path)) encoder.Save(stream);
          DialogResult=false;
        }; timer.Start();
      };
    }
  }
}
