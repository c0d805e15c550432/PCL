Imports Microsoft.Web.WebView2.Core
Imports Microsoft.Web.WebView2.Wpf
Imports PCLCS.THUnion

Public Class PageTHUnionWeb
    Implements IRefreshable

    Private ReadOnly TargetUrl As String
    Private Browser As WebView2CompositionControl
    Private Generation As Integer
    Private Initializing As Boolean
    Private Shared LoaderConfigured As Boolean

    Public Sub New(id As Integer)
        InitializeComponent()
        TargetUrl = UnionService.PageUrl(id)
    End Sub

    Private Async Sub StartBrowser() Handles Me.Loaded
        If Not EnableTHUnion OrElse Initializing OrElse Browser IsNot Nothing Then Return
        Initializing = True
        Generation += 1
        Dim version = Generation
        StatusPanel.Visibility = Visibility.Visible
        Loading.State.LoadingState = MyLoading.MyLoadingState.Run
        LabStatus.Text = ""
        Dim candidate As WebView2CompositionControl = Nothing
        Try
            '在控件加入视觉树之前检查 WinRT 的动态依赖，使缺包错误落入本页而不是 Loaded/Layout 的全局异常。
            Reflection.Assembly.Load("System.Numerics.Vectors, Version=4.1.4.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a")
            If Not LoaderConfigured Then
                CoreWebView2Environment.SetLoaderDllFolderPath(EmbeddedDependencies.PrepareWebViewLoader())
                LoaderConfigured = True
            End If
            '使用单独的浏览器配置目录，不读取默认浏览器或启动器登录令牌。
            Dim profile = IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCL-Union", "THUnion", "WebView2")
            Dim webEnvironment = Await CoreWebView2Environment.CreateAsync(Nothing, profile)
            If version <> Generation OrElse Not IsLoaded Then Return
            candidate = New WebView2CompositionControl()
            Browser = candidate
            BrowserHost.Children.Add(candidate)
            Await candidate.EnsureCoreWebView2Async(webEnvironment)
            If version <> Generation OrElse Browser IsNot candidate Then Return
            candidate.CoreWebView2.Settings.AreHostObjectsAllowed = False
            candidate.CoreWebView2.Settings.IsWebMessageEnabled = False
            AddHandler candidate.CoreWebView2.NavigationStarting, AddressOf NavigationStarting
            AddHandler candidate.CoreWebView2.NavigationCompleted, AddressOf NavigationCompleted
            AddHandler candidate.CoreWebView2.NewWindowRequested,
                Sub(sender, args)
                    args.Handled = True
                    If UnionService.IsWebUrl(args.Uri) Then candidate.CoreWebView2.Navigate(args.Uri)
                End Sub
            AddHandler candidate.CoreWebView2.PermissionRequested, Sub(sender, args) args.State = CoreWebView2PermissionState.Deny
            AddHandler candidate.CoreWebView2.ProcessFailed,
                Sub(sender, args)
                    ShowError("网页进程已停止。点击重试，或在浏览器中打开。")
                    ReleaseBrowser()
                End Sub
            candidate.CoreWebView2.Navigate(TargetUrl)
        Catch ex As WebView2RuntimeNotFoundException
            If version <> Generation Then Return
            ReleaseBrowser()
            ShowError("未找到 Microsoft Edge WebView2 Runtime，请安装后重试，或在浏览器中打开。" & vbCrLf & ex.Message)
        Catch ex As IO.FileNotFoundException
            If version <> Generation Then Return
            ReleaseBrowser()
            ShowError("内置网页组件加载失败，请重新获取完整的启动器 EXE，无需重装 .NET Framework。" & vbCrLf & ex.Message)
        Catch ex As Exception
            If version <> Generation Then Return
            ReleaseBrowser()
            ShowError("内嵌网页启动失败。可点击重试或在浏览器中打开。" & vbCrLf & ex.Message)
        Finally
            If version = Generation Then Initializing = False
        End Try
    End Sub

    Private Sub NavigationStarting(sender As Object, args As CoreWebView2NavigationStartingEventArgs)
        If Not UnionService.IsWebUrl(args.Uri) Then
            args.Cancel = True
            Return
        End If
        StatusPanel.Visibility = Visibility.Visible
        Loading.State.LoadingState = MyLoading.MyLoadingState.Run
        LabStatus.Text = ""
    End Sub

    Private Sub NavigationCompleted(sender As Object, args As CoreWebView2NavigationCompletedEventArgs)
        If args.IsSuccess AndAlso args.HttpStatusCode < 400 Then
            Loading.State.LoadingState = MyLoading.MyLoadingState.Stop
            StatusPanel.Visibility = Visibility.Collapsed
        Else
            ShowError("网页加载失败：" & args.WebErrorStatus.ToString() & "（HTTP " & args.HttpStatusCode & "）。点击重试，或在浏览器中打开。")
        End If
    End Sub

    Private Sub ShowError(message As String)
        StatusPanel.Visibility = Visibility.Visible
        Loading.State.LoadingState = MyLoading.MyLoadingState.Error
        LabStatus.Text = message
    End Sub

    Private Sub ReleaseBrowser() Handles Me.Unloaded
        Generation += 1
        Initializing = False
        If Browser IsNot Nothing Then
            Dim previous = Browser
            Browser = Nothing
            BrowserHost.Children.Clear()
            previous.Dispose()
        End If
    End Sub

    Public Sub Refresh() Implements IRefreshable.Refresh
        If Browser?.CoreWebView2 IsNot Nothing Then
            Browser.CoreWebView2.Reload()
        Else
            StartBrowser()
        End If
    End Sub

    Private Sub Retry(sender As Object, e As MouseButtonEventArgs)
        Refresh()
    End Sub

    Private Sub OpenBrowser(sender As Object, e As MouseButtonEventArgs)
        Dim url = If(Browser?.Source?.AbsoluteUri, TargetUrl)
        If UnionService.IsWebUrl(url) Then OpenWebsite(url)
    End Sub
End Class
