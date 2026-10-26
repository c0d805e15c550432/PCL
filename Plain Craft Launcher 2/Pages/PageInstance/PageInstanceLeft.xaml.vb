Public Class PageInstanceLeft
    Implements IRefreshable

    ''' <summary>
    ''' 当前显示设置的 MC 版本。
    ''' </summary>
    Public Shared Instance As McInstance = Nothing

    Private Sub PageInstanceLeft_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        PageSetupUI.HiddenRefresh()
        RefreshSchematicAvailability()
    End Sub

    Private SchematicCancellation As CancellationTokenSource
    Private SchematicInstance As McInstance
    Private SchematicDirectory As String
    Private ModWatcher As FileSystemWatcher
    Private GameDirectoryWatcher As FileSystemWatcher
    Private ReadOnly ModRefreshTimer As New System.Windows.Threading.DispatcherTimer With {.Interval = TimeSpan.FromMilliseconds(300)}
    Public Property SchematicCheckPending As Boolean

    Public ReadOnly Property SchematicAvailable As Boolean
        Get
            Return Instance IsNot Nothing AndAlso Instance Is SchematicInstance AndAlso
                String.Equals(SchematicDirectory, Instance.PathIndie, StringComparison.OrdinalIgnoreCase) AndAlso
                ItemSchematic.Visibility = Visibility.Visible
        End Get
    End Property

    Public Async Sub RefreshSchematicAvailability()
        SchematicCancellation?.Cancel()
        Dim Cancellation As New CancellationTokenSource
        SchematicCancellation = Cancellation
        SchematicCheckPending = True
        Dim Target = Instance
        Try
            If Target Is Nothing Then
                ItemSchematic.Visibility = Visibility.Collapsed
                Return
            End If
            Dim GameDirectory = Target.PathIndie
            If Target IsNot SchematicInstance OrElse GameDirectory <> SchematicDirectory Then ItemSchematic.Visibility = Visibility.Collapsed
            SchematicInstance = Target
            SchematicDirectory = GameDirectory
            WatchMods(GameDirectory)
            Dim Installed = Await Task.Run(Function() PCLCS.Schematics.SchematicModDetector.IsInstalled(GameDirectory, Cancellation.Token))
            If Cancellation.IsCancellationRequested OrElse Instance IsNot Target OrElse Target.PathIndie <> GameDirectory Then Return
            SetSchematicAvailability(Installed)
        Catch ex As OperationCanceledException
        Catch ex As Exception
            If Not Cancellation.IsCancellationRequested Then
                SetSchematicAvailability(False)
                Logger.Warn(ex, "检测投影模组失败")
            End If
        Finally
            If SchematicCancellation Is Cancellation Then
                SchematicCancellation = Nothing
                SchematicCheckPending = False
            End If
            Cancellation.Dispose()
        End Try
    End Sub

    Private Sub SetSchematicAvailability(Installed As Boolean)
        ItemSchematic.Visibility = If(Installed, Visibility.Visible, Visibility.Collapsed)
        If Not Installed AndAlso PageID = FormMain.PageSubType.InstanceSchematic Then
            ItemOverall.Checked = True
            PageChange(FormMain.PageSubType.InstanceOverall)
        End If
    End Sub

    Private Sub WatchMods(GameDirectory As String)
        '同时监听 mods 文件和目录本身，以处理整个目录被删除、重建或改名的情况。
        If GameDirectoryWatcher Is Nothing OrElse GameDirectoryWatcher.Path <> GameDirectory.TrimEnd(Path.DirectorySeparatorChar) Then
            GameDirectoryWatcher?.Dispose()
            GameDirectoryWatcher = Nothing
            If Directory.Exists(GameDirectory) Then
                GameDirectoryWatcher = New FileSystemWatcher(GameDirectory.TrimEnd(Path.DirectorySeparatorChar)) With {.NotifyFilter = NotifyFilters.DirectoryName}
                AddHandler GameDirectoryWatcher.Created, AddressOf ModsChanged
                AddHandler GameDirectoryWatcher.Deleted, AddressOf ModsChanged
                AddHandler GameDirectoryWatcher.Renamed, AddressOf ModsChanged
                GameDirectoryWatcher.EnableRaisingEvents = True
            End If
        End If
        Dim Folder = Path.Combine(GameDirectory, "mods")
        If ModWatcher IsNot Nothing AndAlso ModWatcher.Path = Folder AndAlso Directory.Exists(Folder) Then Return
        ModWatcher?.Dispose()
        ModWatcher = Nothing
        If Directory.Exists(Folder) Then
            ModWatcher = New FileSystemWatcher(Folder) With {.NotifyFilter = NotifyFilters.FileName Or NotifyFilters.LastWrite}
            AddHandler ModWatcher.Created, AddressOf ModsChanged
            AddHandler ModWatcher.Deleted, AddressOf ModsChanged
            AddHandler ModWatcher.Changed, AddressOf ModsChanged
            AddHandler ModWatcher.Renamed, AddressOf ModsChanged
            ModWatcher.EnableRaisingEvents = True
        End If
        RemoveHandler ModRefreshTimer.Tick, AddressOf RefreshAfterModChange
        AddHandler ModRefreshTimer.Tick, AddressOf RefreshAfterModChange
    End Sub

    Private Sub ModsChanged(sender As Object, e As FileSystemEventArgs)
        Dispatcher.BeginInvoke(New Action(Sub()
                                              If Not IsLoaded Then Return
                                              ModRefreshTimer.Stop()
                                              ModRefreshTimer.Start()
                                          End Sub))
    End Sub

    Private Sub RefreshAfterModChange(sender As Object, e As EventArgs)
        ModRefreshTimer.Stop()
        RefreshSchematicAvailability()
    End Sub

    Private Sub StopSchematicDetection() Handles Me.Unloaded
        SchematicCancellation?.Cancel()
        ModRefreshTimer.Stop()
        ModWatcher?.Dispose()
        ModWatcher = Nothing
        GameDirectoryWatcher?.Dispose()
        GameDirectoryWatcher = Nothing
    End Sub

    Public Sub RefreshModDisabled() Handles Me.Loaded
        If Instance IsNot Nothing AndAlso Instance.Modable Then
            ItemMod.Visibility = Visibility.Visible
            ItemModDisabled.Visibility = Visibility.Collapsed
        Else
            ItemMod.Visibility = Visibility.Collapsed
            ItemModDisabled.Visibility = Visibility.Visible
        End If
    End Sub

#Region "页面切换"

    ''' <summary>
    ''' 当前页面的编号。从 0 开始计算。
    ''' </summary>
    Public PageID As FormMain.PageSubType = FormMain.PageSubType.Default

    ''' <summary>
    ''' 勾选事件改变页面。
    ''' </summary>
    Private Sub PageCheck(sender As FrameworkElement, e As RouteEventArgs) Handles ItemOverall.Check, ItemMod.Check, ItemModDisabled.Check, ItemSetup.Check, ItemModify.Check, ItemExport.Check, ItemSchematic.Check
        '尚未初始化控件属性时，sender.Tag 为 Nothing，会导致切换到页面 0
        '若使用 IsLoaded，则会导致模拟点击不被执行（模拟点击切换页面时，控件的 IsLoaded 为 False）
        If sender.Tag IsNot Nothing Then PageChange(Val(sender.Tag))
    End Sub

    Public Function PageGet(Optional ID As FormMain.PageSubType = -1)
        If ID = -1 Then ID = PageID
        If ID = FormMain.PageSubType.InstanceSchematic AndAlso Not SchematicAvailable Then ID = FormMain.PageSubType.InstanceOverall
        Select Case ID
            Case FormMain.PageSubType.InstanceOverall
                If FrmInstanceOverall Is Nothing Then FrmInstanceOverall = New PageInstanceOverall
                Return FrmInstanceOverall
            Case FormMain.PageSubType.InstanceMod
                If FrmInstanceMod Is Nothing Then FrmInstanceMod = New PageInstanceMod
                Return FrmInstanceMod
            Case FormMain.PageSubType.InstanceModDisabled
                If FrmInstanceModDisabled Is Nothing Then FrmInstanceModDisabled = New PageInstanceModDisabled
                Return FrmInstanceModDisabled
            Case FormMain.PageSubType.InstanceSetup
                If FrmInstanceSetup Is Nothing Then FrmInstanceSetup = New PageInstanceSetup
                Return FrmInstanceSetup
            Case FormMain.PageSubType.InstanceSchematic
                If FrmInstanceSchematic Is Nothing Then FrmInstanceSchematic = New PageInstanceSchematic
                Return FrmInstanceSchematic
            Case FormMain.PageSubType.InstanceExport
                If FrmInstanceExport Is Nothing Then FrmInstanceExport = New PageInstanceExport
                Return FrmInstanceExport
            Case FormMain.PageSubType.InstanceModify
                If FrmDownloadModify Is Nothing Then FrmDownloadModify = New PageDownloadModify
                Return FrmDownloadModify
            Case Else
                Throw New Exception("未知的版本设置子页面种类：" & ID)
        End Select
    End Function

    ''' <summary>
    ''' 切换现有页面。
    ''' </summary>
    Public Sub PageChange(ID As FormMain.PageSubType)
        If ID = FormMain.PageSubType.InstanceSchematic AndAlso Not SchematicAvailable Then ID = FormMain.PageSubType.InstanceOverall
        If PageID = ID Then Return
        If Instance IsNot Nothing AndAlso McInstanceLock.IsLocked(Instance) Then
            Hint("该版本正在被修改，请稍后再切换页面！", HintType.Red)
            Return
        End If
        AniControlEnabled += 1
        Try
            PageChangeRun(PageGet(ID))
            PageID = ID
        Catch ex As Exception
            Logger.Error(ex, $"切换分页面失败（ID {ID}）")
        Finally
            AniControlEnabled -= 1
        End Try
    End Sub
    Private Shared Sub PageChangeRun(Target As MyPageRight)
        AniStop("FrmMain PageChangeRight") '停止主页面的右页面切换动画，防止它与本动画一起触发多次 PageOnEnter
        If Target.Parent IsNot Nothing Then Target.SetValue(ContentPresenter.ContentProperty, Nothing)
        FrmMain.PageRight = Target
        CType(FrmMain.PanMainRight.Child, MyPageRight).PageOnExit()
        AniStart({
            AaCode(Sub()
                       CType(FrmMain.PanMainRight.Child, MyPageRight).PageOnForceExit()
                       FrmMain.PanMainRight.Child = FrmMain.PageRight
                       FrmMain.PageRight.Opacity = 0
                   End Sub, 130),
            AaCode(Sub()
                       '延迟触发页面通用动画，以使得在 Loaded 事件中加载的控件得以处理
                       FrmMain.PageRight.Opacity = 1
                       FrmMain.PageRight.PageOnEnter()
                   End Sub, 30, True)
        }, "PageLeft PageChange")
    End Sub

#End Region

    Public Sub Refresh_Click(sender As Object, e As EventArgs) '由边栏按钮匿名调用
        Refresh(Val(sender.Tag))
    End Sub
    Public Sub Refresh() Implements IRefreshable.Refresh
        Refresh(FrmMain.PageCurrentSub)
    End Sub
    Public Sub Refresh(SubType As FormMain.PageSubType)
        If Instance IsNot Nothing AndAlso McInstanceLock.IsLocked(Instance) Then
            Hint("该版本正在被修改，请稍后再刷新！", HintType.Red)
            Return
        End If
        Select Case SubType
            Case FormMain.PageSubType.InstanceMod
                PageInstanceMod.Refresh()
                ItemMod.Checked = True
            Case FormMain.PageSubType.InstanceSchematic
                FrmInstanceSchematic?.Reload()
                ItemSchematic.Checked = True
            Case FormMain.PageSubType.InstanceExport
                FrmInstanceExport?.RefreshAll()
                ItemExport.Checked = True
            Case FormMain.PageSubType.InstanceModify
                FrmDownloadModify?.PageLoaderRestart()
                ItemModify.Checked = True
        End Select
    End Sub

    Public Sub Reset(sender As Object, e As EventArgs)
        If MyMsgBox("是否要初始化该版本的版本独立设置？该操作不可撤销。", "初始化确认",, "取消", IsWarn:=True) = 1 Then
            If IsNothing(FrmInstanceSetup) Then FrmInstanceSetup = New PageInstanceSetup
            FrmInstanceSetup.Reset()
            ItemSetup.Checked = True
        End If
    End Sub

    '加载当前版本将要选择的 Java
    Public Shared CurrentJavaWorker As New RedoableWorker(Of Java)(Function(c, p) SelectOrDownloadJava(PageInstanceLeft.Instance, False, c, p))
    Public Shared Sub ReloadCurrentJava() Handles Me.Loaded
        If AniControlEnabled <> 0 Then Return
        If PageInstanceLeft.Instance Is Nothing Then Return
        CurrentJavaWorker.Start()
    End Sub

End Class
