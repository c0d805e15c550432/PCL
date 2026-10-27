'由于包含加解密等安全信息，本文件中的部分代码已被删除

Friend Module ModSecret

    '标注 PCL 的不同分支，仅用于替换标记
    Public Const VersionBranchMain As String = "PCL-Union-ThirdParty"
    '在开源版的注册表与常规版的注册表隔离，以防数据冲突
    Public Const RegFolder As String = "PCLDebug"
    '用于微软登录的 ClientId
    Public ReadOnly OAuthClientId As String = ReadBuildClientId()

    Private Function ReadBuildClientId() As String
        Using Stream = Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("PCL.ClientId")
            If Stream Is Nothing Then Return ""
            Using Reader As New StreamReader(Stream)
                Dim Value = Reader.ReadToEnd().Trim()
                Dim Id As Guid
                Return If(Guid.TryParse(Value, Id), Id.ToString(), "")
            End Using
        End Using
    End Function
    'CurseForge API Key
    Public CurseForgeAPIKey As String = If(Environment.GetEnvironmentVariable("PCL_CURSEFORGE_API_KEY"), "")
    '用于匿名数据收集的腾讯云日志服务上报 URL，形如 https://{region}.cls.tencentcs.com/track?topic_id={topic_id}
    Public Const ClsBaseUrl As String = ""

#Region "网络鉴权"

    '发布源码默认关闭；本地构建可通过编译常量启用，两者互相独立。
#If PCL_ENABLE_DOWNLOAD Then
    Public Const RestrictMcDownload As Boolean = False
#Else
    Public Const RestrictMcDownload As Boolean = True
#End If
#If PCL_ENABLE_THEME Then
    Public Const EnableThemeCustomization As Boolean = True
#Else
    Public Const EnableThemeCustomization As Boolean = False
#End If
#If PCL_ENABLE_THUNION Then
    Public Const EnableTHUnion As Boolean = True
#Else
    Public Const EnableTHUnion As Boolean = False
#End If
    Friend Function SecretCdnSign(UrlWithMark As String) As String
        If Not UrlWithMark.EndsWithF("{CDN}") Then Return UrlWithMark
        Return UrlWithMark.Replace("{CDN}", "").Replace(" ", "%20")
    End Function
    ''' <summary>
    ''' 设置 Headers 的 UA、Referer。
    ''' </summary>
    Friend Sub SecretHeadersSign(Url As String, ByRef Req As HttpRequestMessage, Optional SimulateBrowserHeaders As Boolean = False)
        If Not Req.Headers.UserAgent.Any Then
            If Url.Contains("baidupcs.com") OrElse Url.Contains("baidu.com") Then
                Req.Headers.Add("User-Agent", "LogStatistic")  '#4951
            ElseIf SimulateBrowserHeaders Then
                Req.Headers.Add("User-Agent", $"PCL2/{VersionBaseName}.{CInt(BuildType)} Mozilla/5.0 AppleWebKit/537.36 Chrome/63.0.3239.132 Safari/537.36")
            Else
                Req.Headers.Add("User-Agent", $"PCL2/{VersionBaseName}.{CInt(BuildType)}")
            End If
        End If
        If Not SimulateBrowserHeaders Then Req.Headers.Add("Referer", $"http://{VersionCode}.open.pcl2.server/")
        If Url.Contains("api.curseforge.com") OrElse Url.Contains("forgecdn.net") Then Req.Headers.Add("x-api-key", CurseForgeAPIKey)
    End Sub

#End Region

#Region "主题"

    Public Color1 As New MyColor(52, 61, 74)
    Public Color2 As New MyColor(11, 91, 203)
    Public Color3 As New MyColor(19, 112, 243)
    Public Color4 As New MyColor(72, 144, 245)
    Public Color5 As New MyColor(150, 192, 249)
    Public Color6 As New MyColor(213, 230, 253)
    Public Color7 As New MyColor(222, 236, 253)
    Public Color8 As New MyColor(234, 242, 254)
    Public ColorBg0 As New MyColor(150, 192, 249)
    Public ColorBg1 As New MyColor(190, Color7)
    Public ColorGray1 As New MyColor(64, 64, 64)
    Public ColorGray2 As New MyColor(115, 115, 115)
    Public ColorGray3 As New MyColor(140, 140, 140)
    Public ColorGray4 As New MyColor(166, 166, 166)
    Public ColorGray5 As New MyColor(204, 204, 204)
    Public ColorGray6 As New MyColor(235, 235, 235)
    Public ColorGray7 As New MyColor(240, 240, 240)
    Public ColorGray8 As New MyColor(245, 245, 245)
    Public ColorSemiTransparent As New MyColor(1, Color8)

    Public ThemeNow As Integer = -1
    Public ColorHue As Integer = 210, ColorSat As Integer = 85, ColorLightAdjust As Integer = 0, ColorHueTopbarDelta As OneOf(Of Integer, Integer()) = 0
    Public ThemeDontClick As Integer = 0

    Public Sub ThemeRefresh(Optional NewTheme As Integer = -1)
        Try
            If Not EnableThemeCustomization Then NewTheme = 0
            If NewTheme >= 0 AndAlso Not ThemeCheckOne(NewTheme) Then NewTheme = 0
            If EnableThemeCustomization AndAlso ThemeNow = NewTheme AndAlso NewTheme >= 0 Then Return
            If NewTheme >= 0 Then ThemeNow = NewTheme
            If Not ThemeCheckOne(ThemeNow) Then ThemeNow = 0

            '根据本地快照分析补齐配色；不使用联网主题权限或赞助账户状态。
            Dim Palette As Integer(,) = {
                {210, 85, 0}, {180, 50, 0}, {130, 40, 0}, {75, 50, 0}, {35, 30, 0},
                {225, 10, -20}, {345, 70, 15}, {285, 50, 0}, {50, 65, 10}, {25, 75, 5},
                {5, 70, -5}, {230, 60, -5}, {210, 45, 0}, {290, 50, 0}, {210, 85, 0}}
            ColorHueTopbarDelta = 0
            If ThemeNow = 14 Then
                ColorHue = Math.Max(0, Math.Min(360, Settings.Get(Of Integer)("UiLauncherHue")))
                ColorSat = Math.Max(0, Math.Min(100, Settings.Get(Of Integer)("UiLauncherSat")))
                ColorLightAdjust = Math.Max(0, Math.Min(40, Settings.Get(Of Integer)("UiLauncherLight"))) - 20
                ColorHueTopbarDelta = Math.Max(0, Math.Min(180, Settings.Get(Of Integer)("UiLauncherDelta"))) - 90
            ElseIf ThemeNow = 12 Then
                ColorHue = (ColorHue + 4) Mod 360
                ColorSat = 45
                ColorLightAdjust = 0
            ElseIf ThemeNow = 42 Then
                ColorHue = 225
                ColorSat = 60
                ColorLightAdjust = -15
            Else
                ColorHue = Palette(ThemeNow, 0)
                ColorSat = Palette(ThemeNow, 1)
                ColorLightAdjust = Palette(ThemeNow, 2)
                If ThemeNow = 2 Then ColorHueTopbarDelta = New Integer() {5, -5, 5}
                If ThemeNow = 13 Then ColorHueTopbarDelta = New Integer() {-30, 10, 50}
            End If

            Color1 = New MyColor().FromHSL2(ColorHue, ColorSat * 0.2, 25 + ColorLightAdjust * 0.3)
            Color2 = New MyColor().FromHSL2(ColorHue, ColorSat, 45 + ColorLightAdjust)
            Color3 = New MyColor().FromHSL2(ColorHue, ColorSat, 55 + ColorLightAdjust)
            Color4 = New MyColor().FromHSL2(ColorHue, ColorSat, 65 + ColorLightAdjust)
            Color5 = New MyColor().FromHSL2(ColorHue, ColorSat, 80 + ColorLightAdjust * 0.4)
            Color6 = New MyColor().FromHSL2(ColorHue, ColorSat, 91 + ColorLightAdjust * 0.1)
            Color7 = New MyColor().FromHSL2(ColorHue, ColorSat, 95)
            Color8 = New MyColor().FromHSL2(ColorHue, ColorSat, 97)
            ColorBg0 = Color4 * 0.4 + Color5 * 0.4 + ColorGray4 * 0.2
            ColorBg1 = New MyColor(190, Color7)

            ColorSemiTransparent = New MyColor(1, Color8)
            Application.Current.Resources("ColorBrush1") = New SolidColorBrush(Color1)
            Application.Current.Resources("ColorBrush2") = New SolidColorBrush(Color2)
            Application.Current.Resources("ColorBrush3") = New SolidColorBrush(Color3)
            Application.Current.Resources("ColorBrush4") = New SolidColorBrush(Color4)
            Application.Current.Resources("ColorBrush5") = New SolidColorBrush(Color5)
            Application.Current.Resources("ColorBrush6") = New SolidColorBrush(Color6)
            Application.Current.Resources("ColorBrush7") = New SolidColorBrush(Color7)
            Application.Current.Resources("ColorBrush8") = New SolidColorBrush(Color8)
            Application.Current.Resources("ColorBrushBg0") = New SolidColorBrush(ColorBg0)
            Application.Current.Resources("ColorBrushBg1") = New SolidColorBrush(ColorBg1)
            Application.Current.Resources("ColorObject1") = CType(Color1, Color)
            Application.Current.Resources("ColorObject2") = CType(Color2, Color)
            Application.Current.Resources("ColorObject3") = CType(Color3, Color)
            Application.Current.Resources("ColorObject4") = CType(Color4, Color)
            Application.Current.Resources("ColorObject5") = CType(Color5, Color)
            Application.Current.Resources("ColorObject6") = CType(Color6, Color)
            Application.Current.Resources("ColorObject7") = CType(Color7, Color)
            Application.Current.Resources("ColorObject8") = CType(Color8, Color)
            Application.Current.Resources("ColorObjectBg0") = CType(ColorBg0, Color)
            Application.Current.Resources("ColorObjectBg1") = CType(ColorBg1, Color)
            ThemeRefreshMain()
        Catch ex As Exception
            Logger.Error(ex, "刷新主题颜色失败", LogBehavior.Toast)
        End Try
    End Sub
    Public Sub ThemeRefreshMain()
        RunInUi(
        Sub()
            If FrmMain Is Nothing OrElse Not FrmMain.IsLoaded Then Return
            '顶部条背景
            Dim Brush = New LinearGradientBrush With {.EndPoint = New Point(1, 0), .StartPoint = New Point(0, 0)}
            If ThemeNow = 5 Then
                Brush.GradientStops.Add(New GradientStop(New MyColor().FromHSL2(ColorHue, ColorSat, 25), 0))
                Brush.GradientStops.Add(New GradientStop(New MyColor().FromHSL2(ColorHue, ColorSat, 15), 0.5))
                Brush.GradientStops.Add(New GradientStop(New MyColor().FromHSL2(ColorHue, ColorSat, 25), 1))
            ElseIf ThemeNow = 12 Then
                For Index As Integer = 0 To 3
                    Brush.GradientStops.Add(New GradientStop(New MyColor().FromHSL2(ColorHue - 21 + Index * 14,
                        ColorSat, If(Index = 0 OrElse Index = 3, 53, 47)), Index / 3.0))
                Next
            Else
                Dim Deltas = ColorHueTopbarDelta.Switch(Function(d) New Integer() {-d, 0, d}, Function(d) d)
                Brush.GradientStops.Add(New GradientStop(New MyColor().FromHSL2(ColorHue + Deltas(0), ColorSat, 48 + ColorLightAdjust), 0))
                Brush.GradientStops.Add(New GradientStop(New MyColor().FromHSL2(ColorHue + Deltas(1), ColorSat, 54 + ColorLightAdjust), 0.5))
                Brush.GradientStops.Add(New GradientStop(New MyColor().FromHSL2(ColorHue + Deltas(2), ColorSat, 48 + ColorLightAdjust), 1))
            End If
            Brush.Freeze()
            FrmMain.PanTitle.Background = Brush
            FrmMain.ImgTitle.Source = If(ThemeNow >= 5 AndAlso ThemeNow <> 14, $"{PathImage}Themes/{ThemeNow}.png", Nothing)
            FrmMain.ImgTitle.Opacity = If(ThemeNow = 13, 0.25, 0.5)
            '主页面背景
            If Settings.Get(Of Boolean)("UiBackgroundColorful") Then
                Brush = New LinearGradientBrush With {.EndPoint = New Point(0.1, 1), .StartPoint = New Point(0.9, 0)}
                Brush.GradientStops.Add(New GradientStop With {.Offset = -0.1, .Color = New MyColor().FromHSL2(ColorHue - 15, ColorSat * 0.8, 91)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0.4, .Color = New MyColor().FromHSL2(ColorHue, ColorSat * 0.8, 91)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 1.1, .Color = New MyColor().FromHSL2(ColorHue + 15, ColorSat * 0.8, 91)})
                FrmMain.PanForm.Background = Brush
            Else
                FrmMain.PanForm.Background = New MyColor(245, 245, 245)
            End If
            FrmMain.PanForm.Background.Freeze()
        End Sub)
    End Sub
    Friend Sub ThemeCheckAll(EffectSetup As Boolean)
        If Not ThemeCheckOne(Settings.Get(Of Integer)("UiLauncherTheme")) Then Settings.Set("UiLauncherTheme", 0)
        If Not EffectSetup OrElse FrmSetupUI Is Nothing Then Return
        For Each Control In FrmSetupUI.PanLauncherTheme.Children.OfType(Of MyRadioBox)()
            If Control.Name = "RadioLauncherTheme5Gray" Then Continue For
            Control.IsEnabled = EnableThemeCustomization OrElse Control.Name = "RadioLauncherTheme0"
            Control.Opacity = If(Control.IsEnabled, 1, 0.4)
            Control.ToolTip = If(EnableThemeCustomization, "本地内置主题", "此构建未启用主题修改")
        Next
        FrmSetupUI.RadioLauncherTheme5Gray.Visibility = Visibility.Collapsed
        FrmSetupUI.LabLauncherTheme5Unlock.Visibility = Visibility.Collapsed
        FrmSetupUI.LabLauncherTheme8Copy.Visibility = Visibility.Collapsed
        FrmSetupUI.LabLauncherTheme9Copy.Visibility = Visibility.Collapsed
        FrmSetupUI.LabLauncherTheme11Click.Visibility = Visibility.Collapsed
        FrmSetupUI.PanLauncherHide.Visibility = Visibility.Collapsed
        FrmSetupUI.PanLauncherTheme.IsEnabled = EnableThemeCustomization
    End Sub
    Friend Function ThemeCheckOne(Id As Integer) As Boolean
        If Not EnableThemeCustomization Then Return Id = 0
        Return (Id >= 0 AndAlso Id <= 14) OrElse Id = 42
    End Function
    Friend Function ThemeUnlock(Id As Integer, Optional ShowDoubleHint As Boolean = True, Optional UnlockHint As String = Nothing) As Boolean
        '主题已经全部可用，不产生“新解锁”事件，避免每次登录或启动都重复弹窗。
        Return False
    End Function

#End Region

#Region "更新"

    Friend Sub UpdateCheckByButton()
        Hint("该版本中不包含更新功能……")
    End Sub

    Friend IsUpdateWaitingRestart As Boolean = False
    Public Sub UpdateRestart(TriggerRestartAndByEnd As Boolean)
    End Sub

    ''' <summary>
    ''' 确保 PathTemp/Latest.exe 是最新正式版的 PCL，它会被用于整合包打包。
    ''' 如果不是，则下载一个。
    ''' </summary>
    Friend Sub DownloadLatestPCL(Optional LoaderToSyncProgress As LoaderBase = Nothing)
        '注意：如果要自行实现这个功能，请换用另一个文件路径，以免与官方版本冲突
    End Sub

#End Region

#Region "联网配置"

    ''' <summary>
    ''' 联网获取的配置信息。
    ''' 若获取失败或仍在获取中，可能为 Nothing。
    ''' </summary>
    Public ServerConfig As JObject

    Public ServerLoader As New LoaderTask(Of Integer, Integer)("PCL 配置更新", Sub() Logger.Info("该版本中不包含更新通知功能……"), Priority:=ThreadPriority.BelowNormal) With
        {.ReloadTimeout = 1000 * 60 * 60} '超时 1 小时

#End Region

#Region "赞助等级"

    Public ReadOnly Property CurrentRank As DonationRank
        Get
            Return DonationRank.None
        End Get
    End Property

    Public Sub InputPotatoCode(IsUpdating As Boolean)
    End Sub
    Friend Sub GeneratePotatoCode()
    End Sub

    ''' <summary>
    ''' 获取设备识别码。
    ''' </summary>
    Friend Function GetIdentify() As String
        Return "0000-0000-0000-0000"
    End Function

#End Region

End Module
