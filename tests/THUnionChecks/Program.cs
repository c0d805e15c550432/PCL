using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Wpf;
using PCL;
using PCLCS.THUnion;

internal static class Program
{
    private static Assembly launcher;
    private static object launcherApp;
    private static string folder;
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly IntPtr Hkcu = new IntPtr(unchecked((int)0x80000001));
    [DllImport("advapi32.dll")] private static extern int RegOverridePredefKey(IntPtr key, IntPtr replacement);

    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        string sandbox = @"Software\PclTHUnionChecks_" + Guid.NewGuid().ToString("N");
        var key = Registry.CurrentUser.CreateSubKey(sandbox);
        bool redirected = false;
        int result = 1;
        try
        {
            Check(!File.Exists(AppDomain.CurrentDomain.SetupInformation.ConfigurationFile) &&
                !File.Exists(Path.GetFullPath(args[0]) + ".config"), "portable run has no sidecar or test-host config");
            Check(Directory.GetFiles(Path.GetDirectoryName(Path.GetFullPath(args[0]))).Length == 1,
                "portable directory initially contains only the launcher EXE");
            redirected = RegOverridePredefKey(Hkcu, key.Handle.DangerousGetHandle()) == 0;
            if (!redirected) throw new Exception("Unable to isolate registry");
            folder = Path.GetDirectoryName(Path.GetFullPath(args[0]));
            launcher = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            // Only locate the renamed EXE. All dependency resolution uses production Application code.
            AppDomain.CurrentDomain.AssemblyResolve += (_, request) =>
                new AssemblyName(request.Name).Name == launcher.GetName().Name ? launcher : null;
            launcherApp = Activator.CreateInstance(launcher.GetType("PCL.Application"), true);
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            Run(args);
            result = 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); }
        finally
        {
            if (redirected) RegOverridePredefKey(Hkcu, IntPtr.Zero);
            key.Dispose();
            Registry.CurrentUser.DeleteSubKeyTree(sandbox, false);
        }
        Console.Out.Flush();
        Console.Error.Flush();
        Environment.Exit(result);
        return result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(string[] args)
    {
        bool unionEnabled = args[1] != "all-disabled", themeEnabled = args[1] == "all-enabled";
        string output = Path.GetFullPath(args[2]);
        Directory.CreateDirectory(output);
        var app = (PCL.Application)launcherApp;
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var startup = typeof(PCL.Application).GetMethod("Application_Startup", PrivateInstance);
        app.Startup -= (StartupEventHandler)Delegate.CreateDelegate(typeof(StartupEventHandler), app, startup);
        var handler = typeof(PCL.Application).GetMethod("Application_DispatcherUnhandledException", PrivateInstance);
        app.DispatcherUnhandledException -= (DispatcherUnhandledExceptionEventHandler)Delegate.CreateDelegate(typeof(DispatcherUnhandledExceptionEventHandler), app, handler);
        MeloongCore.Main.Init("PCL");
        MeloongCore.Wpf.Main.Init();
        app.InitializeComponent();
        typeof(System.Windows.Application).GetField("_startupUri", PrivateInstance).SetValue(app, null);
        ModAnimation.AniControlEnabled = 1;
        var main = ModMain.FrmMain = new FormMain();
        var secret = launcher.GetType("PCL.ModSecret");
        Check(launcher.GetName().Version == new Version(2, 99, 0, 2) &&
            launcher.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion == "2.99.T.1" &&
            (string)launcher.GetType("PCL.ModBase").GetField("VersionBaseName").GetRawConstantValue() == "2.99.T.1",
            "assembly and displayed versions match 2.99.T.1");
        Check((bool)secret.GetField("EnableTHUnion").GetRawConstantValue() == unionEnabled, "THUnion build constant");
        Check((bool)secret.GetField("RestrictMcDownload").GetRawConstantValue() == !unionEnabled, "download build constant");
        Check((bool)secret.GetField("EnableThemeCustomization").GetRawConstantValue() == themeEnabled, "theme build constant");
        secret.GetMethod("ThemeRefresh").Invoke(null, new object[] { 0 });
        var title = (Panel)main.FindName("PanTitleSelect");
        string[] labels = title.Children.OfType<MyRadioButton>().Select(b => b.Text).ToArray();
        Check(Array.IndexOf(labels, "下载") < Array.IndexOf(labels, "THUnion") && Array.IndexOf(labels, "THUnion") < Array.IndexOf(labels, "设置"), "top-level tab order");
        RemoveLifecycle(main);
        main.Topmost = false;
        main.ShowActivated = false;
        main.ShowInTaskbar = false;
        main.Width = 1050;
        main.Height = 720;
        main.Opacity = 0;
        ((FrameworkElement)main.FindName("PanBack")).RenderTransform = Transform.Identity;
        ((Border)main.FindName("PanMainLeft")).Child = main.PageLeft = new MyPageLeft();
        ((Border)main.FindName("PanMainRight")).Child = main.PageRight = new MyPageRight { Child = new Grid() };
        main.Show();
        PumpUntil(() => main.IsLoaded);
        PageSetupUI.HiddenRefresh();
        Check(((MyRadioButton)main.FindName("BtnTitleTHUnion")).Visibility == (unionEnabled ? Visibility.Visible : Visibility.Collapsed), "tab visibility");
        ModAnimation.AniControlEnabled = 0;
        ModAnimation.AniStart();
        if (!unionEnabled)
        {
            main.PageChange(FormMain.PageType.THUnion);
            Check(main.PageCurrent.Page == FormMain.PageType.Launch && ModMain.FrmTHUnionLeft == null, "disabled direct navigation blocked");
            PageSetupUI.HiddenForceShow = true;
            PageSetupUI.HiddenRefresh();
            Check(((MyRadioButton)main.FindName("BtnTitleTHUnion")).Visibility == Visibility.Collapsed, "F12 cannot reveal compiled-out feature");
            main.Close();
            return;
        }

        RunEnabled(main, args, output);
    }

    private static void CheckNativeCache(string output)
    {
        var prepare = launcher.GetType("PCL.EmbeddedDependencies").GetMethod("PrepareWebViewLoader", BindingFlags.Static | BindingFlags.Public);
        string testRoot = Path.Combine(output, "cache-fixture");
        string cache = (string)prepare.Invoke(null, new object[] { testRoot });
        string native = Path.Combine(cache, "WebView2Loader.dll");
        byte[] expected = File.ReadAllBytes(native);
        Check(expected.Length > 100000, "native loader extracted from EXE to separate cache");
        File.WriteAllBytes(native, new byte[] { 0, 1, 2 }); // Only corrupt this test-owned fixture.
        Check((string)prepare.Invoke(null, new object[] { testRoot }) == cache && File.ReadAllBytes(native).SequenceEqual(expected),
            "native cache integrity check repairs a damaged test-owned loader");
        prepare.Invoke(null, new object[] { testRoot });
        Check(File.ReadAllBytes(native).SequenceEqual(expected), "native cache reuse preserves exact embedded bytes");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunEnabled(FormMain main, string[] args, string output)
    {
        CheckNativeCache(output);
        CheckParsers(args[3]);
        main.PageChange(FormMain.PageType.THUnion);
        PumpUntil(() => ((Border)main.FindName("PanMainRight")).Child is PageTHUnionData && main.PageRight.IsLoaded);
        var left = ModMain.FrmTHUnionLeft;
        var names = ((StackPanel)left.FindName("PanItem")).Children.OfType<MyListItem>().Select(item => item.Title);
        Check(string.Join("|", names) == "在线列表|实时地图|工业设施导引|Wiki首页|服务器列表|二周目|一周目|部门规章制度", "eight sidebar entries in exact order");
        var online = (PageTHUnionData)main.PageRight;
        PumpUntil(() => Read<bool>(online, "HasData"), 25);
        Check(((MyCard)online.FindName("CardData")).Title.StartsWith("在线玩家数："), "live online data rendered");
        DateTime first = Read<DateTime>(online, "LastSuccess");
        PumpUntil(() => Read<DateTime>(online, "LastSuccess") > first, 5);
        Check(true, "one-second live polling updates");
        Pump(1200);
        int sidebarLoads = 0, sidebarUnloads = 0;
        left.Loaded += (_, __) => sidebarLoads++;
        left.Unloaded += (_, __) => sidebarUnloads++;
        Save(main, Path.Combine(output, "online.png"));
        // Native operation button follows exactly the main navigation route.
        typeof(PageTHUnionData).GetMethod("OpenMap", PrivateInstance).Invoke(online, new object[] { null, null });
        PumpUntil(() => ((Border)main.FindName("PanMainRight")).Child is PageTHUnionWeb && main.PageRight.IsLoaded);
        Check(!Read<DispatcherTimer>(online, "PollTimer").IsEnabled && Read<CancellationTokenSource>(online, "Lifetime") == null, "leaving online page cancels polling and requests");
        CheckWeb((PageTHUnionWeb)main.PageRight, 1, output);

        foreach (int id in new[] { 2, 4 })
        {
            if (id == 2) ((MyListItem)left.FindName("ItemIndustry")).Checked = true;
            else main.PageChange(FormMain.PageType.THUnion, (FormMain.PageSubType)id);
            Check(!ModAnimation.AniIsRun("FrmMain PageChangeLeft"), "subpage navigation never schedules sidebar animation " + id);
            PumpUntil(() => ((Border)main.FindName("PanMainRight")).Child is PageTHUnionData && (int)main.PageCurrentSub == id && main.PageRight.IsLoaded);
            var data = (PageTHUnionData)main.PageRight;
            PumpUntil(() => Read<bool>(data, "HasData"), 25);
            Check(((StackPanel)data.FindName("Rows")).Children.Count > 0, "live Wiki table rendered " + id);
            Pump(700);
            var scroller = (MyScrollViewer)data.FindName("TableScroll");
            if (id == 2)
            {
                Check(scroller.ScrollableWidth > 0 && scroller.ComputedHorizontalScrollBarVisibility == Visibility.Visible,
                    "industrial table exposes a horizontal scrollbar");
                scroller.ScrollToRightEnd();
                Pump(200);
                Check(scroller.HorizontalOffset > 0, "rightmost industrial columns reachable");
                Save(main, Path.Combine(output, "table-2-rightmost.png"));
                scroller.ScrollToLeftEnd();
            }
            Save(main, Path.Combine(output, "table-" + id + ".png"));
        }
        foreach (int id in new[] { 3, 5, 6, 7 })
        {
            main.PageChange(FormMain.PageType.THUnion, (FormMain.PageSubType)id);
            PumpUntil(() => ((Border)main.FindName("PanMainRight")).Child is PageTHUnionWeb && (int)main.PageCurrentSub == id && main.PageRight.IsLoaded);
            CheckWeb((PageTHUnionWeb)main.PageRight, id, output);
        }
        // A rapid switch during browser initialization must not attach a disposed WebView.
        main.PageChange(FormMain.PageType.THUnion, FormMain.PageSubType.THUnionWiki);
        Pump(170);
        main.PageChange(FormMain.PageType.THUnion, FormMain.PageSubType.THUnionOnline);
        PumpUntil(() => ((Border)main.FindName("PanMainRight")).Child == online && online.IsLoaded);
        PumpUntil(() => Read<DateTime>(online, "LastSuccess") > first);
        Check(Read<DispatcherTimer>(online, "PollTimer").IsEnabled, "online refresh resumes after rapid navigation");
        main.Width = 850;
        main.Height = 500;
        Pump(800);
        Check(((MyScrollViewer)online.FindName("TableScroll")).ScrollableWidth == 0, "all five online columns fit default window width");
        Save(main, Path.Combine(output, "online-default-size.png"));
        Check(sidebarLoads == 0 && sidebarUnloads == 0 && ReferenceEquals(((Border)main.FindName("PanMainLeft")).Child, left),
            "sidebar remains mounted across operation buttons, sidebar clicks and programmatic navigation");
        main.PageChange(FormMain.PageType.THUnion, FormMain.PageSubType.THUnionMap);
        Pump(20);
        main.PageChange(FormMain.PageType.THUnion, FormMain.PageSubType.THUnionIndustry);
        Pump(20);
        main.PageChange(FormMain.PageType.THUnion, FormMain.PageSubType.THUnionOnline);
        Pump(800);
        Check(ReferenceEquals(((Border)main.FindName("PanMainRight")).Child, online) && Read<DispatcherTimer>(online, "PollTimer").IsEnabled && sidebarLoads == 0 && sidebarUnloads == 0,
            "interrupted subpage animations preserve sidebar and restart online polling");
        CheckFailureRecovery(online);
        // Tag-based selection preserves existing numeric navigation after inserting THUnion.
        main.PageChange(FormMain.PageType.Setup, FormMain.PageSubType.SetupLaunch);
        PumpUntil(() => main.PageCurrent.Page == FormMain.PageType.Setup && ((Border)main.FindName("PanMainRight")).Child == main.PageRight && main.PageRight.IsLoaded);
        Check(((MyRadioButton)main.FindName("BtnTitleSelect3")).Checked, "existing setup navigation uses correct title button");
        main.Close();
    }

    private static void CheckFailureRecovery(PageTHUnionData online)
    {
        typeof(PageTHUnionData).GetMethod("EndLoading", PrivateInstance).Invoke(online, null);
        var clientField = typeof(UnionService).GetField("Client", BindingFlags.Static | BindingFlags.NonPublic);
        var original = (HttpClient)clientField.GetValue(null);
        var handler = new FixtureHandler();
        using var client = new HttpClient(handler);
        try
        {
            clientField.SetValue(null, client);
            var rows = (StackPanel)online.FindName("Rows");
            int originalRows = rows.Children.Count;
            var previous = Read<DateTime>(online, "LastSuccess");
            typeof(PageTHUnionData).GetMethod("BeginLoading", PrivateInstance).Invoke(online, null);
            PumpUntil(() => ((TextBlock)online.FindName("LabStatus")).Text.Contains("更新失败"));
            Check(rows.Children.Count == originalRows && Read<DateTime>(online, "LastSuccess") == previous,
                "failed refresh preserves last successful rows and timestamp");
            handler.Fail = false;
            online.Refresh();
            PumpUntil(() => ((MyCard)online.FindName("CardData")).Title == "在线玩家数：0");
            Check(rows.Children.Count == 1 && Read<DateTime>(online, "LastSuccess") > previous, "recovery to valid empty server removes departed rows");
            typeof(PageTHUnionData).GetMethod("EndLoading", PrivateInstance).Invoke(online, null);
            var firstLoad = new PageTHUnionData(0);
            var host = new Window { Content = firstLoad, Width = 700, Height = 500, ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
            handler.Fail = true;
            host.Show();
            PumpUntil(() => ((MyLoading)firstLoad.FindName("Loading")).State.LoadingState == MyLoading.MyLoadingState.Error);
            Check(!Read<bool>(firstLoad, "HasData") && ((MyButton)firstLoad.FindName("BtnMap")).IsVisible,
                "initial failure keeps operation buttons visible and offers retry");
            handler.Fail = false;
            firstLoad.Refresh();
            PumpUntil(() => Read<bool>(firstLoad, "HasData"));
            Check(((MyLoading)firstLoad.FindName("Loading")).Visibility == Visibility.Collapsed, "initial-error retry recovers loading state");
            host.Close();
        }
        finally { clientField.SetValue(null, original); }
    }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        public bool Fail = true;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(Fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            { Content = new StringContent("{\"currentcount\":0,\"players\":[]}") });
        }
    }

    private static void CheckParsers(string wikiRoot)
    {
        string player = "{\"name\":\"test\",\"world\":\"DIM1\",\"x\":-2.125,\"y\":64,\"z\":0,\"health\":18.5,\"armor\":12}";
        var snapshot = UnionService.ParseOnline("{\"currentcount\":8,\"players\":[" + player + "]}");
        Check(snapshot.CurrentCount == 8 && snapshot.Players.Count == 1 && snapshot.Players[0].Dimension == "末地", "currentcount independent from player count, End dimension mapping");
        Check(UnionService.ParseOnline("{\"currentcount\":0,\"players\":[]}").Players.Count == 0, "valid empty server");
        foreach (string invalid in new[] { "<html>Error</html>", "{}", "{\"currentcount\":-1,\"players\":[]}", "{\"currentcount\":1,\"players\":[{}]}" })
        {
            bool rejected = false;
            try { UnionService.ParseOnline(invalid); } catch { rejected = true; }
            Check(rejected, "malformed online payload rejected");
        }
        foreach (var file in Directory.GetFiles(wikiRoot, "*.html", SearchOption.AllDirectories).Where(f => f.Contains("Industry") || f.Contains("ServerList")))
        {
            var tables = UnionService.ParseTables(File.ReadAllText(file), UnionService.WikiUrl("首页"));
            Check(tables.Count > 0 && tables[0].ColumnCount >= 5 && tables[0].RowCount > 2, "saved HTML parsed: " + Path.GetFileName(file));
            Console.WriteLine("TABLE columns=" + tables[0].ColumnCount + " rows=" + tables[0].RowCount);
        }
        string html = "<table class='wikitable'><tbody><tr><th rowspan='2'>A<br>B</th><th colspan='2'>C</th></tr><tr><td><a href='javascript:alert(1)'>bad</a></td><td><a href='/index.php?title=X'>good</a></td></tr></tbody></table>";
        var table = UnionService.ParseTables(html, UnionService.WikiUrl("首页"))[0];
        Check(table.ColumnCount == 3 && table.RowCount == 2 && table.Cells[2].Column == 1 && table.Cells[0].Text.Contains("\n"), "HTML rowspan colspan and line break preservation");
        Check(table.Cells[2].Links.Count == 0 && table.Cells[3].Links[0].Url.StartsWith("http://www.lvss.xyz:14014/"), "safe links and relative URL resolution");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        bool canceled = false;
        try { UnionService.GetOnlineAsync(cancel.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { canceled = true; }
        Check(canceled, "HTTP cancellation");
    }

    private static void CheckWeb(PageTHUnionWeb page, int id, string output)
    {
        PumpUntil(() => Read<WebView2CompositionControl>(page, "Browser")?.CoreWebView2 != null || ((TextBlock)page.FindName("LabStatus")).Text.Length > 0, 25);
        var web = Read<WebView2CompositionControl>(page, "Browser");
        Check(web?.CoreWebView2 != null, "WebView2 initialized " + id + ": " + ((TextBlock)page.FindName("LabStatus")).Text);
        var vectors = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "System.Numerics.Vectors" && a.GetName().Version == new Version(4, 1, 4, 0));
        Check(string.IsNullOrEmpty(vectors.Location), "Vectors is loaded from EXE memory without external config");
        foreach (string name in new[] { "Microsoft.Web.WebView2.Core", "Microsoft.Web.WebView2.Wpf", "HtmlAgilityPack" })
            Check(AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == name).Location == "", "embedded managed dependency: " + name);
        var loader = Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Single(m => m.ModuleName.Equals("WebView2Loader.dll", StringComparison.OrdinalIgnoreCase));
        Check(loader.FileName.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), StringComparison.OrdinalIgnoreCase) &&
            !loader.FileName.StartsWith(folder, StringComparison.OrdinalIgnoreCase), "native WebView loader uses user cache, not EXE directory");
        PumpUntil(() => ((Border)page.FindName("StatusPanel")).Visibility == Visibility.Collapsed || ((TextBlock)page.FindName("LabStatus")).Text.Length > 0, 30);
        Check(((Border)page.FindName("StatusPanel")).Visibility == Visibility.Collapsed, "web navigation completed " + id + ": " + ((TextBlock)page.FindName("LabStatus")).Text);
        Pump(1200);
        string url = Await(web.ExecuteScriptAsync("location.href"));
        File.WriteAllText(Path.Combine(output, "web-" + id + "-location.json"), url);
        if (id == 5 || id == 6) Check(url.Contains("#"), "Wiki anchor retained " + id);
        string dom = Await(web.ExecuteScriptAsync("JSON.stringify({title:document.title,body:document.body.innerText.length,frames:frames.length,hash:location.hash})"));
        File.WriteAllText(Path.Combine(output, "web-" + id + "-dom.json"), dom);
        using (var stream = File.Create(Path.Combine(output, "web-" + id + ".png")))
            Await(web.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, stream));
        Save(ModMain.FrmMain, Path.Combine(output, "web-" + id + "-launcher.png"));
    }

    private static T Read<T>(object target, string field) => (T)target.GetType().GetField(field, PrivateInstance).GetValue(target);
    private static T Await<T>(Task<T> task) { PumpUntil(() => task.IsCompleted, 30); return task.GetAwaiter().GetResult(); }
    private static void Await(Task task) { PumpUntil(() => task.IsCompleted, 30); task.GetAwaiter().GetResult(); }
    private static void Check(bool value, string label) { if (!value) throw new Exception(label); Console.WriteLine("PASS " + label); }
    private static void RemoveLifecycle(FormMain main)
    {
        main.Loaded -= (RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler), main, typeof(FormMain).GetMethod("FormMain_Loaded", PrivateInstance));
        main.Closing -= (System.ComponentModel.CancelEventHandler)Delegate.CreateDelegate(typeof(System.ComponentModel.CancelEventHandler), main, typeof(FormMain).GetMethod("FormMain_Closing", PrivateInstance));
    }
    private static void Pump(int milliseconds) { var timer = Stopwatch.StartNew(); PumpUntil(() => timer.ElapsedMilliseconds >= milliseconds); }
    private static void PumpUntil(Func<bool> condition, int seconds = 15)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.Elapsed.TotalSeconds > seconds) throw new TimeoutException("UI callback timed out");
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(10);
        }
    }
    private static void Save(FormMain main, string path)
    {
        var surface = (FrameworkElement)main.Content;
        surface.UpdateLayout();
        var image = new RenderTargetBitmap((int)surface.ActualWidth, (int)surface.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(surface);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using (var file = File.Create(path)) encoder.Save(file);
    }
}
