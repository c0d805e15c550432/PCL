using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using PCL;
using MeloongCore.Extensions;
using System.Security.Cryptography;
using System.Text;

internal static class Program
{
    private static Assembly launcher;
    private static bool featuresEnabled;
    private const BindingFlags AllStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly IntPtr Hkcu = new IntPtr(unchecked((int)0x80000001));
    [DllImport("advapi32.dll")]
    private static extern int RegOverridePredefKey(IntPtr key, IntPtr replacement);

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        Console.WriteLine("PASS " + label);
    }

    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        // No user's PCL registry/account settings are read or written by this process.
        string sandbox = @"Software\PclSchematicChecks_" + Guid.NewGuid().ToString("N");
        var key = Registry.CurrentUser.CreateSubKey(sandbox);
        bool redirected = false;
        int result = 1;
        try
        {
            redirected = RegOverridePredefKey(Hkcu, key.Handle.DangerousGetHandle()) == 0;
            if (!redirected) throw new Exception("Unable to isolate registry");
            Environment.SetEnvironmentVariable("PCL_MS_CLIENT_ID", null);
            launcher = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            var resources = new ResourceManager("PCL.Resources", launcher);
            var cache = new Dictionary<string, Assembly>();
            AppDomain.CurrentDomain.AssemblyResolve += (_, request) =>
            {
                string name = new AssemblyName(request.Name).Name;
                if (name == launcher.GetName().Name) return launcher;
                string[] embedded = { "NAudio", "Newtonsoft.Json", "Ookii.Dialogs.Wpf", "Imazen.WebP", "CacheCow.Common",
                    "CacheCow.Client.FileStore", "CacheCow.Client", "ThrottleDebounce", "System.Net.Http.Formatting",
                    "Microsoft.Win32.Registry", "PCLCS", "MeloongCore.Wpf", "MeloongCore" };
                if (!embedded.Contains(name)) return null;
                if (cache.TryGetValue(name, out var value)) return value;
                byte[] data = resources.GetObject(name) as byte[];
                if (data == null) return null;
                return cache[name] = Assembly.Load(data);
            };
            // A manually hosted WPF test must install the context normally supplied by Application.Run.
            System.Threading.SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            featuresEnabled = args.Length > 2 && args[2] == "enabled";
            RunChecks(Path.GetFullPath(args[1]));
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
        Environment.Exit(result); // PCL owns persistent manager threads.
        return result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunChecks(string output)
    {
        Directory.CreateDirectory(output);
        var app = (PCL.Application)Activator.CreateInstance(typeof(PCL.Application), true);
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        // Exercise BAML and code directly without normal startup, accounts or update services.
        var startup = typeof(PCL.Application).GetMethod("Application_Startup", BindingFlags.Instance | BindingFlags.NonPublic);
        app.Startup -= (StartupEventHandler)Delegate.CreateDelegate(typeof(StartupEventHandler), app, startup);
        var handler = typeof(PCL.Application).GetMethod("Application_DispatcherUnhandledException", BindingFlags.Instance | BindingFlags.NonPublic);
        app.DispatcherUnhandledException -= (DispatcherUnhandledExceptionEventHandler)Delegate.CreateDelegate(typeof(DispatcherUnhandledExceptionEventHandler), app, handler);
        MeloongCore.Main.Init("PCL");
        MeloongCore.Wpf.Main.Init();
        app.InitializeComponent();
        ModAnimation.AniControlEnabled = 1; // Render final control colors without the normal startup animation loop.
        typeof(System.Windows.Application).GetField("_startupUri", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(app, null);
        ModMain.FrmMain = new FormMain();
        var secret = launcher.GetType("PCL.ModSecret");
        Check(Guid.TryParse((string)secret.GetField("OAuthClientId").GetValue(null), out var clientId) && clientId != Guid.Empty, "embedded local OAuth Client ID is valid");
        Check((bool)secret.GetField("RestrictMcDownload").GetRawConstantValue() == !featuresEnabled, "compiled download switch matches variant");
        Check((bool)secret.GetField("EnableThemeCustomization").GetRawConstantValue() == featuresEnabled, "compiled theme switch matches variant");
        CheckLoginMigration();
        Check(secret.GetMethod("ServerSub", AllStatic) == null && secret.GetMethod("UpdateReplace", AllStatic) == null,
            "no cloud announcement handler or update replacement method");
        Check(typeof(ModLaunch).GetMethod("McLaunchCustom", AllStatic) == null, "no custom pre-launch command method");
        Check(typeof(ModNet).GetMethod("ShouldValidateSslCertificateOnLogin", AllStatic) == null &&
              System.Net.ServicePointManager.ServerCertificateValidationCallback == null, "system HTTPS certificate validation without login bypass");

        var setup = new PageSetupUI();
        ModMain.FrmSetupUI = setup;
        secret.GetMethod("ThemeCheckAll", AllStatic).Invoke(null, new object[] { true });
        var checkTheme = secret.GetMethod("ThemeCheckOne", AllStatic);
        if (featuresEnabled)
        {
        for (int id = 0; id <= 14; id++)
        {
            var radio = (UIElement)setup.FindName("RadioLauncherTheme" + id);
            Check(radio.IsEnabled && radio.Opacity == 1 && (bool)checkTheme.Invoke(null, new object[] { id }), "theme control enabled " + id);
        }
        Check(((UIElement)setup.FindName("PanLauncherHide")).Visibility == Visibility.Collapsed &&
              ((UIElement)setup.FindName("LabLauncherTheme8Copy")).Visibility == Visibility.Collapsed,
            "theme overlays removed");
        Check(!(bool)checkTheme.Invoke(null, new object[] { 15 }) && !(bool)checkTheme.Invoke(null, new object[] { -1 }), "invalid theme IDs rejected");
        Check(!(bool)secret.GetMethod("ThemeUnlock", AllStatic).Invoke(null, new object[] { 10, false, null }),
            "already available themes do not trigger repeated login unlock dialogs");
        var colors = new HashSet<string>();
        foreach (int id in Enumerable.Range(0, 14).Concat(new[] { 42 }))
        {
            secret.GetMethod("ThemeRefresh").Invoke(null, new object[] { id });
            colors.Add(app.Resources["ColorBrush3"].ToString());
            Check((int)secret.GetField("ThemeNow").GetValue(null) == id, "theme selected " + id);
        }
        Check(colors.Count == 15, "all built-in theme primary colors are distinct");
        Settings.Set("UiLauncherHue", 110);
        Settings.Set("UiLauncherSat", 63);
        Settings.Set("UiLauncherLight", 27);
        Settings.Set("UiLauncherDelta", 120);
        Settings.Set("UiLauncherTheme", 14);
        Check((int)secret.GetField("ColorHue").GetValue(null) == 110 && (int)secret.GetField("ColorSat").GetValue(null) == 63 &&
              (int)secret.GetField("ColorLightAdjust").GetValue(null) == 7, "custom HSL settings reach renderer");
        Settings.Set("UiLauncherTheme", 999);
        secret.GetMethod("ThemeCheckAll", AllStatic).Invoke(null, new object[] { true });
        Check(Settings.Get<int>("UiLauncherTheme") == 0, "invalid persisted theme repaired");

        }
        else
        {
            Check(!((UIElement)setup.FindName("PanLauncherTheme")).IsEnabled, "standard build disables all theme controls");
            foreach (int id in new[] { 1, 5, 12, 14, 42 })
            {
                Check(!(bool)checkTheme.Invoke(null, new object[] { id }), "standard rejects theme " + id);
                secret.GetMethod("ThemeRefresh").Invoke(null, new object[] { id });
                Check((int)secret.GetField("ThemeNow").GetValue(null) == 0, "standard renderer remains blue");
            }
            Settings.Set("UiLauncherHue", 110);
            Settings.Set("UiLauncherTheme", 14);
            secret.GetMethod("ThemeCheckAll", AllStatic).Invoke(null, new object[] { true });
            Check(Settings.Get<int>("UiLauncherTheme") == 0 && (int)secret.GetField("ColorHue").GetValue(null) == 210,
                "persisted theme and custom hue cannot bypass disabled switch");
        }

        // Make only the window lifecycle live; remove normal startup and its child pages.
        var main = ModMain.FrmMain;
        var loaded = typeof(FormMain).GetMethod("FormMain_Loaded", BindingFlags.Instance | BindingFlags.NonPublic);
        main.Loaded -= (RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler), main, loaded);
        var closing = typeof(FormMain).GetMethod("FormMain_Closing", BindingFlags.Instance | BindingFlags.NonPublic);
        main.Closing -= (System.ComponentModel.CancelEventHandler)Delegate.CreateDelegate(typeof(System.ComponentModel.CancelEventHandler), main, closing);
        main.Content = new Grid();
        main.ShowInTaskbar = false;
        main.ShowActivated = false;
        main.Opacity = 0;
        main.Show();
        PumpUntil(() => main.IsLoaded);
        var titlePanel = (Panel)main.FindName("PanTitle");
        foreach (int id in featuresEnabled ? new[] { 5, 12, 13, 14, 42 } : new[] { 0 })
        {
            secret.GetMethod("ThemeRefresh").Invoke(null, new object[] { id });
            var gradient = titlePanel.Background as LinearGradientBrush;
            Check(gradient != null && gradient.GradientStops.Count == (id == 12 ? 4 : 3), "title gradient rendered " + id);
        }
        var image = main.FindName("ImgTitle");
        Check((((System.Windows.Controls.Image)image).Source != null) == featuresEnabled, "title texture matches feature switch");
        main.Close();

        string game = Path.Combine(output, ".minecraft") + "\\";
        string version = Path.Combine(game, "versions", "test") + "\\";
        Directory.CreateDirectory(version);
        ModMinecraft.McFolderSelected = game;
        var instance = new ModMinecraft.McInstance(version);
        Settings.Set("VersionArgumentIndieV2", true, instance);
        PageInstanceLeft.Instance = instance;
        var left = new PageInstanceLeft();
        var entry = (MyListItem)left.FindName("ItemSchematic");
        left.RefreshSchematicAvailability();
        PumpUntil(() => !left.SchematicCheckPending);
        Check(entry.Visibility == Visibility.Collapsed && !left.SchematicAvailable, "schematic entry hidden without installed mod");
        Check(!(left.PageGet(FormMain.PageSubType.InstanceSchematic) is PageInstanceSchematic), "hidden schematic page cannot be routed directly");
        string mods = Path.Combine(version, "mods");
        Directory.CreateDirectory(mods);
        string modFile = Path.Combine(mods, "arbitrary-name.jar");
        using (var file = File.Create(modFile))
        using (var zip = new System.IO.Compression.ZipArchive(file, System.IO.Compression.ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("fabric.mod.json").Open())) writer.Write("{\"id\":\"litematica\"}");
        left.RefreshSchematicAvailability();
        PumpUntil(() => !left.SchematicCheckPending);
        Check(entry.Visibility == Visibility.Visible && left.SchematicAvailable, "active Litematica declaration reveals entry regardless of filename");
        var page = (PageInstanceSchematic)left.PageGet(FormMain.PageSubType.InstanceSchematic);
        Check(((MyListItem)left.FindName("ItemSchematic")).Title == "投影管理" && page != null, "version navigation resolves schematic page");
        var status = (TextBlock)page.FindName("LabStatus");
        var directory = (MyButton)page.FindName("BtnFolder");
        var grid = (StackPanel)page.FindName("PanList");
        var selection = (UIElement)page.FindName("CardSelect");
        var search = (MySearchBox)page.FindName("SearchBox");
        // Empty-library and isolation changes use the real async page binding.
        page.Reload();
        PumpUntil(() => status.Text.StartsWith("尚未"));
        Check((string)directory.ToolTip == version.TrimEnd('\\') + "\\schematics", "page uses isolated PathIndie");
        string library = Path.Combine(version, "schematics");
        Directory.CreateDirectory(library);
        // Minimal valid raw NBT: root compound with dimensions 1,2,3.
        File.WriteAllBytes(Path.Combine(library, "示例.schematic"), new byte[] { 10,0,0,2,0,5,87,105,100,116,104,0,1,2,0,6,72,101,105,103,104,116,0,2,2,0,6,76,101,110,103,116,104,0,3,0 });
        page.Reload();
        PumpUntil(() => grid.Children.Count == 1);
        var row = (MySchematicItem)grid.Children[0];
        row.Checked = true;
        var detailTask = page.ReadDetailsAsync(row.Entry);
        PumpUntil(() => detailTask.IsCompleted);
        Check(detailTask.Result.Contains("1 × 2 × 3") && selection.Visibility == Visibility.Visible, "selection details loaded asynchronously and action card visible");
        search.Text = "no-match";
        Check(grid.Children.Count == 0 && selection.Visibility == Visibility.Collapsed, "search filters list and clears hidden selection");
        search.Text = "示例";
        Check(grid.Children.Count == 1, "Unicode search restores match");
        search.Text = "";
        foreach (string name in new[] { "仓库.litematic", "树场.litematic", "村庄.schem", "结构.nbt" })
            File.Copy(Path.Combine(library, "示例.schematic"), Path.Combine(library, name));
        page.Reload();
        PumpUntil(() => grid.Children.Count == 5);
        ((MyRadioButton)page.FindName("BtnFilterLitematic")).Checked = true;
        Check(grid.Children.Count == 2 && grid.Children.Cast<MySchematicItem>().All(item => item.Entry.Format == "LITEMATIC"), "format chips filter rendered rows");
        ((MyRadioButton)page.FindName("BtnFilterAll")).Checked = true;
        typeof(PageInstanceSchematic).GetMethod("SelectAll", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(page, null);
        Check(grid.Children.Cast<MySchematicItem>().All(item => item.Checked) && !((UIElement)page.FindName("BtnRename")).IsEnabled, "select all enables batch selection and disables single-file rename");
        typeof(PageInstanceSchematic).GetMethod("CancelSelection", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(page, null);
        Check(selection.Visibility == Visibility.Collapsed, "cancel selection hides action card");
        Render(page, Path.Combine(output, "schematic-page.png"), 850, 600);
        Render(setup, Path.Combine(output, "personalization-page.png"), 950, 740);
        File.Move(modFile, modFile + ".disabled");
        left.RefreshSchematicAvailability();
        PumpUntil(() => !left.SchematicCheckPending);
        Check(!left.SchematicAvailable && entry.Visibility == Visibility.Collapsed, "disabling last schematic mod hides entry");
        File.Move(modFile + ".disabled", modFile);
        left.RefreshSchematicAvailability();
        PumpUntil(() => !left.SchematicCheckPending);
        Check(left.SchematicAvailable, "re-enabling mod restores entry");
        // Host just the sidebar to exercise filesystem notifications; normal version UI startup stays detached.
        left.Loaded -= (RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler), left,
            typeof(PageInstanceLeft).GetMethod("PageInstanceLeft_Loaded", BindingFlags.Instance | BindingFlags.NonPublic));
        var sidebarHost = new Window { Content = left, Width = 180, Height = 500, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        sidebarHost.Show();
        PumpUntil(() => left.IsLoaded);
        File.Move(modFile, modFile + ".disabled");
        PumpUntil(() => !left.SchematicAvailable);
        Check(entry.Visibility == Visibility.Collapsed, "filesystem watcher detects external disable without refresh");
        File.Move(modFile + ".disabled", modFile);
        PumpUntil(() => left.SchematicAvailable);
        Check(true, "filesystem watcher detects external re-enable");
        Directory.Move(mods, mods + "-moved");
        PumpUntil(() => !left.SchematicAvailable);
        Directory.Move(mods + "-moved", mods);
        PumpUntil(() => left.SchematicAvailable);
        Check(true, "filesystem watcher recovers after mods directory replacement");
        sidebarHost.Close();
        Settings.Set("VersionArgumentIndieV2", false, instance);
        left.RefreshSchematicAvailability();
        PumpUntil(() => !left.SchematicCheckPending);
        Check(!left.SchematicAvailable, "shared directory does not inherit isolated mod availability");
        page.Reload();
        PumpUntil(() => (string)directory.ToolTip == game.TrimEnd('\\') + "\\schematics" && status.Text.StartsWith("尚未"));
        Check(grid.Children.Count == 0, "switching to shared game directory clears isolated entries");
        // Allow pending UI callbacks to complete before exit.
        Check(new PageDownloadInstall() != null, "download page BAML and LoaderInit construct");
        var download = new PageDownloadLeft();
        Check((((UIElement)download.FindName("ItemInstall")).Visibility == Visibility.Visible) == featuresEnabled,
            "upstream Minecraft download navigation follows switch");
        Check(download.PageID == (featuresEnabled ? FormMain.PageSubType.DownloadInstall : FormMain.PageSubType.DownloadMod),
            "disabled download defaults to mod resources");
        Console.WriteLine("PASS Launcher UI checks completed; normal startup/login/game launch not exercised.");
    }

    private static void CheckLoginMigration()
    {
        var import = typeof(Settings).GetMethod("ImportOfficialLoginFromKeys", AllStatic);
        var encrypt = typeof(Settings).GetMethod("DesEncrypt", AllStatic);
        var decrypt = typeof(Settings).GetMethod("DesDecrypt", AllStatic);
        const string seed = "fixture-seed-2026";
        const string hardware = "FIXTURE-HARDWARE";
        string hash = (hardware + seed).GetStableHashCode().ToString("X").EnsureLength('7', 16);
        string officialId = hash.Substring(4, 4) + "-" + hash.Substring(12, 4) + "-" + hash.Substring(0, 4) + "-" + hash.Substring(8, 4);
        string Legacy(string value, string identity)
        {
            string key = ("PCL" + identity).GetStableHashCode().ToString().EnsureLength('X', 8).Substring(0, 8);
            using var des = new DESCryptoServiceProvider();
            using var stream = new MemoryStream();
            using (var crypto = new CryptoStream(stream, des.CreateEncryptor(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes("95168702")), CryptoStreamMode.Write))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(value);
                crypto.Write(bytes, 0, bytes.Length);
                crypto.FlushFinalBlock();
                return Convert.ToBase64String(stream.ToArray());
            }
        }
        string json = "{\"Fixture Player\":\"fixture-refresh-token\"}";
        string original = Legacy(json, officialId);
        using var sourceWrite = Registry.CurrentUser.CreateSubKey("MigrationFixtureOfficial");
        sourceWrite.SetValue("Identify", seed);
        sourceWrite.SetValue("LoginMsJson", original);
        sourceWrite.SetValue("CacheMsV2Name", Legacy("Fixture Player", officialId));
        sourceWrite.SetValue("CacheMsV2OAuthRefresh", Legacy("fixture-refresh-token", officialId));
        sourceWrite.SetValue("LaunchAdvanceRun", "fixture-never-import");
        using var source = Registry.CurrentUser.OpenSubKey("MigrationFixtureOfficial", false);
        using var target = Registry.CurrentUser.CreateSubKey("MigrationFixtureLocal");
        Check((bool)import.Invoke(null, new object[] { source, target, hardware }), "official DES account migrates from read-only registry key");
        string migrated = (string)target.GetValue("LoginMsJson");
        Check(migrated.StartsWith("dpapi:v1:") && (string)decrypt.Invoke(null, new object[] { migrated, "PCL" + ModBase.Identify }) == json,
            "imported credential protected by current-user DPAPI and readable locally");
        Check((string)source.GetValue("LoginMsJson") == original && (string)source.GetValue("Identify") == seed && source.GetValueNames().Length == 5,
            "official values remain byte-for-byte unchanged");
        Check(target.GetValue("LaunchAdvanceRun") == null, "migration whitelist excludes startup commands");
        Check(!(bool)import.Invoke(null, new object[] { source, target, hardware }), "successful migration is one-time");
        target.DeleteValue("LoginMsJson");
        Check(!(bool)import.Invoke(null, new object[] { source, target, hardware }), "logout does not reimport official account");
        using var existing = Registry.CurrentUser.CreateSubKey("MigrationExistingLocal");
        string own = (string)encrypt.Invoke(null, new object[] { "{\"Own\":\"own-token\"}", "PCL" + ModBase.Identify });
        existing.SetValue("LoginMsJson", own);
        Check(!(bool)import.Invoke(null, new object[] { source, existing, hardware }) && (string)existing.GetValue("LoginMsJson") == own,
            "existing fork accounts are never overwritten");
        using var badTarget = Registry.CurrentUser.CreateSubKey("MigrationBadTarget");
        sourceWrite.SetValue("LoginMsJson", "damaged");
        bool rejected = false;
        try { import.Invoke(null, new object[] { source, badTarget, hardware }); } catch (TargetInvocationException) { rejected = true; }
        Check(rejected && badTarget.GetValueNames().Length == 0, "damaged official ciphertext leaves target untouched");
        sourceWrite.SetValue("LoginMsJson", original);
        rejected = false;
        try { import.Invoke(null, new object[] { source, badTarget, "DIFFERENT-HARDWARE" }); } catch (TargetInvocationException) { rejected = true; }
        Check(rejected && badTarget.GetValueNames().Length == 0, "incompatible device identity does not overwrite target");
        sourceWrite.DeleteValue("LoginMsJson");
        sourceWrite.DeleteValue("CacheMsV2Name");
        sourceWrite.DeleteValue("CacheMsV2OAuthRefresh");
        sourceWrite.SetValue("CacheMsName", Legacy("Legacy Player", officialId));
        sourceWrite.SetValue("CacheMsOAuthRefresh", Legacy("legacy-fixture-refresh", officialId));
        Check((bool)import.Invoke(null, new object[] { source, badTarget, hardware }), "older single-account official cache can migrate");
        Check((string)decrypt.Invoke(null, new object[] { Legacy("legacy-local", ModBase.Identify), "PCL" + ModBase.Identify }) == "legacy-local",
            "previous fork DES data remains readable");
        string protectedText = (string)encrypt.Invoke(null, new object[] { "fixture-new", "unused" });
        Check((string)decrypt.Invoke(null, new object[] { protectedText, "unused" }) == "fixture-new", "new encrypted settings round-trip with DPAPI");
        Settings.Set("LoginMsJson", "{\"New A\":\"token-a\",\"New B\":\"token-b\"}");
        Settings.Set("CacheMsV2Name", "New B");
        Settings.Set("CacheMsV2OAuthRefresh", "token-b-refreshed");
        Check(Settings.Get<string>("LoginMsJson").Contains("New B") && Settings.Get<string>("CacheMsV2Name") == "New B" &&
            Settings.Get<string>("CacheMsV2OAuthRefresh") == "token-b-refreshed", "normal account addition, switching and refresh writes work after migration");
        using (var ownRegistry = Registry.CurrentUser.OpenSubKey("Software\\PCLDebug"))
        {
            Check(((string)ownRegistry.GetValue("LoginMsJson")).StartsWith("dpapi:v1:") &&
                (string)decrypt.Invoke(null, new object[] { ownRegistry.GetValue("CacheMsV2OAuthRefresh"), "unused" }) == "token-b-refreshed",
                "normal account writes persist encrypted and can be read back");
        }
        Settings.Set("LoginMsJson", "{}");
        Settings.Set("CacheMsV2OAuthRefresh", "");
        Check(Settings.Get<string>("LoginMsJson") == "{}" && Settings.Get<string>("CacheMsV2OAuthRefresh") == "", "normal logout clears local account data");
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("UI callback did not complete");
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            System.Threading.Thread.Sleep(10);
        }
    }

    private static void Render(FrameworkElement page, string path, int width, int height)
    {
        var surface = new Border { Background = Brushes.White, Child = page, Width = width, Height = height };
        var host = new Window { Content = surface, Width = width, Height = height, WindowStyle = WindowStyle.None,
            ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
        host.Show();
        if (page is PageInstanceSchematic)
        {
            var grid = (StackPanel)page.FindName("PanList");
            PumpUntil(() => grid.Children.Count == 5);
            ((MySchematicItem)grid.Children[0]).Checked = true;
        }
        surface.UpdateLayout();
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        image.Render(surface);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using (var file = File.Create(path)) encoder.Save(file);
        host.Content = null;
        surface.Child = null;
        host.Close();
    }
}
