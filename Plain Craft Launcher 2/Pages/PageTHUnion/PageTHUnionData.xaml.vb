Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Documents
Imports System.Windows.Threading
Imports PCLCS.THUnion

Public Class PageTHUnionData
    Implements IRefreshable

    Private ReadOnly PageId As Integer
    Private ReadOnly PollTimer As New DispatcherTimer With {.Interval = TimeSpan.FromSeconds(1)}
    Private Lifetime As CancellationTokenSource
    Private Busy As Boolean
    Private HasData As Boolean
    Private ReadOnly PlayerRows As New Dictionary(Of String, PlayerRow)(StringComparer.Ordinal)
    Private ReadOnly Avatars As New Dictionary(Of String, ImageSource)(StringComparer.Ordinal)
    Private LastSuccess As DateTime

    Public Sub New(id As Integer)
        InitializeComponent()
        PageId = id
        BtnWiki.Visibility = If(id = 0, Visibility.Collapsed, Visibility.Visible)
        BtnMap.Visibility = If(id = 4, Visibility.Collapsed, Visibility.Visible)
        CardData.Title = If(id = 0, "在线玩家数：加载中", If(id = 2, "三周目工业设置导引", "服务器列表"))
        AddHandler PollTimer.Tick, Sub() Refresh()
    End Sub

    Private Sub BeginLoading() Handles Me.Loaded, Me.PageEnter
        If Not EnableTHUnion Then Return
        '快速切回仍挂载的页面不会再次触发 Loaded，需在进入事件恢复轮询。
        If Lifetime IsNot Nothing Then Return
        Lifetime = New CancellationTokenSource()
        Busy = False
        Refresh()
        If PageId = 0 Then PollTimer.Start()
    End Sub

    Private Sub EndLoading() Handles Me.PageExit, Me.Unloaded
        PollTimer.Stop()
        If Lifetime Is Nothing Then Return
        Lifetime.Cancel()
        Lifetime.Dispose()
        Lifetime = Nothing
        Busy = False
    End Sub

    Private Sub ResizeTable() Handles Me.SizeChanged
        '让宽表的横向滚动条始终处于可见区域，无需滚到几十行表格的底部。
        If TableScroll IsNot Nothing Then TableScroll.MaxHeight = Math.Max(130, ActualHeight - 245)
    End Sub

    Public Async Sub Refresh() Implements IRefreshable.Refresh
        If Not EnableTHUnion OrElse Lifetime Is Nothing OrElse Busy Then Return
        Dim current = Lifetime
        Dim token = current.Token
        Busy = True
        If Not HasData Then
            Loading.Visibility = Visibility.Visible
            Loading.State.LoadingState = MyLoading.MyLoadingState.Run
            LabStatus.Visibility = Visibility.Collapsed
        End If
        Try
            If PageId = 0 Then
                Dim snapshot = Await UnionService.GetOnlineAsync(token)
                If current IsNot Lifetime OrElse token.IsCancellationRequested Then Return
                RenderPlayers(snapshot, token)
            Else
                Dim tables = Await UnionService.GetTablesAsync(PageId, token)
                If current IsNot Lifetime OrElse token.IsCancellationRequested Then Return
                RenderTables(tables)
            End If
            HasData = True
            LastSuccess = DateTime.Now
            Loading.State.LoadingState = MyLoading.MyLoadingState.Stop
            Loading.Visibility = Visibility.Collapsed
            TableScroll.Visibility = Visibility.Visible
            LabStatus.Visibility = Visibility.Visible
            LabStatus.Text = "更新于 " & LastSuccess.ToString("HH:mm:ss") & If(PageId = 0, " · 每秒刷新", " · 按 F5 刷新")
        Catch ex As Exception
            If current IsNot Lifetime OrElse token.IsCancellationRequested Then Return
            LabStatus.Visibility = Visibility.Visible
            LabStatus.Text = If(HasData, "更新失败，当前为 " & LastSuccess.ToString("HH:mm:ss") & " 的数据。", "获取数据失败。") &
                If(PageId = 0, "正在自动重试；也可按 F5。", "点击加载图标或按 F5 重试。") & vbCrLf & ex.Message
            If Not HasData Then Loading.State.LoadingState = MyLoading.MyLoadingState.Error
        Finally
            If current Is Lifetime Then Busy = False
        End Try
    End Sub

    Private Sub Retry(sender As Object, e As MouseButtonEventArgs)
        Refresh()
    End Sub

    Private Sub OpenWiki(sender As Object, e As MouseButtonEventArgs)
        FrmMain.PageChange(FormMain.PageType.THUnion, FormMain.PageSubType.THUnionWiki)
    End Sub

    Private Sub OpenMap(sender As Object, e As MouseButtonEventArgs)
        FrmMain.PageChange(FormMain.PageType.THUnion, FormMain.PageSubType.THUnionMap)
    End Sub

    Private Sub OpenBrowser(sender As Object, e As MouseButtonEventArgs)
        OpenWebsite(UnionService.PageUrl(PageId))
    End Sub

    Private Shared Function NewText(value As String, Optional header As Boolean = False) As TextBlock
        Dim text As New TextBlock With {.Text = value, .TextWrapping = TextWrapping.Wrap,
            .VerticalAlignment = VerticalAlignment.Center, .Margin = New Thickness(10, 9, 10, 9)}
        If header Then
            text.FontWeight = FontWeights.SemiBold
            text.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush3")
        End If
        Return text
    End Function

    Private Shared Function PlayerGrid() As Grid
        Dim row As New Grid With {.MinWidth = 580}
        For Each width As Double In {2.3, 1, 2.7, 0.8, 0.8}
            row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(width, GridUnitType.Star)})
        Next
        Return row
    End Function

    Private Shared Function RowBorder(content As UIElement, header As Boolean) As Border
        Dim result As New Border With {.Child = content, .BorderThickness = New Thickness(0, 0, 0, 1), .CornerRadius = New CornerRadius(3)}
        result.SetResourceReference(Border.BorderBrushProperty, "ColorBrush6")
        If header Then result.SetResourceReference(Border.BackgroundProperty, "ColorBrush8")
        Return result
    End Function

    Private Sub RenderPlayers(snapshot As OnlineSnapshot, token As CancellationToken)
        CardData.Title = "在线玩家数：" & snapshot.CurrentCount
        If Rows.Children.Count = 0 Then
            Dim header = PlayerGrid()
            Dim names = {"玩家", "维度", "坐标 [x, y, z]", "生命值", "护甲值"}
            For i = 0 To names.Length - 1
                Dim text = NewText(names(i), True)
                Grid.SetColumn(text, i)
                header.Children.Add(text)
            Next
            Rows.Children.Add(RowBorder(header, True))
        End If
        '按玩家名复用行和头像；坐标变化不会重建整张列表或重置滚动位置。
        Dim alive As New HashSet(Of String)(StringComparer.Ordinal)
        Dim index = 1
        For Each player In snapshot.Players
            If Not alive.Add(player.Name) Then Continue For
            Dim view As PlayerRow = Nothing
            If Not PlayerRows.TryGetValue(player.Name, view) Then
                view = New PlayerRow()
                Dim row = PlayerGrid()
                Dim identity As New Grid()
                identity.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(38)})
                identity.ColumnDefinitions.Add(New ColumnDefinition())
                view.Avatar = New Image With {.Width = 28, .Height = 28, .Margin = New Thickness(5), .ToolTip = player.Name}
                RenderOptions.SetBitmapScalingMode(view.Avatar, BitmapScalingMode.NearestNeighbor)
                Dim fallback = NewText("◻")
                identity.Children.Add(fallback)
                identity.Children.Add(view.Avatar)
                Dim name = NewText(player.Name)
                name.Margin = New Thickness(2, 9, 6, 9)
                Grid.SetColumn(name, 1)
                identity.Children.Add(name)
                row.Children.Add(identity)
                For i = 1 To 4
                    Dim text = NewText("")
                    view.Values.Add(text)
                    Grid.SetColumn(text, i)
                    row.Children.Add(text)
                Next
                view.Container = RowBorder(row, False)
                PlayerRows.Add(player.Name, view)
            End If
            If view.Avatar.Source Is Nothing AndAlso Not view.AvatarLoading Then LoadAvatar(player.Name, view, token)
            view.Values(0).Text = player.Dimension
            view.Values(1).Text = player.Coordinates
            view.Values(2).Text = UnionService.FormatNumber(player.Health)
            view.Values(3).Text = UnionService.FormatNumber(player.Armor)
            If Rows.Children.IndexOf(view.Container) <> index Then
                Rows.Children.Remove(view.Container)
                Rows.Children.Insert(index, view.Container)
            End If
            index += 1
        Next
        For Each departedName In PlayerRows.Keys.Where(Function(value) Not alive.Contains(value)).ToList()
            Rows.Children.Remove(PlayerRows(departedName).Container)
            PlayerRows.Remove(departedName)
        Next
        '空列表也属于有效响应。
        LabStatus.ToolTip = "接口在线人数与 players 条目数可能不同；这里保留服务器原值。"
    End Sub

    Private Async Sub LoadAvatar(name As String, view As PlayerRow, token As CancellationToken)
        view.AvatarLoading = True
        Try
            Dim bitmap As ImageSource = Nothing
            If Not Avatars.TryGetValue(name, bitmap) Then
                Dim bytes = Await UnionService.GetAvatarAsync(name, token)
                token.ThrowIfCancellationRequested()
                Using stream As New MemoryStream(bytes)
                    Dim image As New BitmapImage()
                    image.BeginInit()
                    image.CacheOption = BitmapCacheOption.OnLoad
                    image.DecodePixelWidth = 32
                    image.StreamSource = stream
                    image.EndInit()
                    image.Freeze()
                    bitmap = image
                End Using
                If Avatars.Count >= 256 Then Avatars.Clear()
                Avatars(name) = bitmap
            End If
            If Not token.IsCancellationRequested Then view.Avatar.Source = bitmap
        Catch ex As Exception
            '头像失败不影响在线数据，使用占位图；本次停留不反复请求缺失头像。
            If Not token.IsCancellationRequested Then view.Avatar.ToolTip = name & "（头像暂不可用）"
        Finally
            '取消的请求允许返回页面后重试；其他失败保留占位图。
            view.AvatarLoading = Not token.IsCancellationRequested AndAlso view.Avatar.Source Is Nothing
        End Try
    End Sub

    Private Sub RenderTables(tables As IReadOnlyList(Of WikiTable))
        Rows.Children.Clear()
        For Each table In tables
            If table.Caption <> "" Then Rows.Children.Add(NewText(table.Caption, True))
            Dim grid As New Grid With {.MinWidth = If(table.ColumnCount > 6, 1180, 680), .Margin = New Thickness(0, 0, 0, 15)}
            For i = 1 To table.ColumnCount
                grid.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
            Next
            For i = 1 To table.RowCount
                grid.RowDefinitions.Add(New RowDefinition With {.Height = GridLength.Auto})
            Next
            For Each cell In table.Cells
                Dim text = NewText("", cell.IsHeader)
                '在原文本中恢复链接，不重复显示链接文字，也不执行远端 HTML。
                Dim offset = 0
                For Each link In cell.Links
                    Dim target = link.Url
                    If String.IsNullOrEmpty(link.Text) Then Continue For
                    Dim position = cell.Text.IndexOf(link.Text, offset, StringComparison.Ordinal)
                    If position < 0 Then Continue For
                    text.Inlines.Add(New Run(cell.Text.Substring(offset, position - offset)))
                    Dim anchor As New Hyperlink(New Run(link.Text)) With {.ToolTip = target}
                    AddHandler anchor.Click, Sub() OpenWebsite(target)
                    text.Inlines.Add(anchor)
                    offset = position + link.Text.Length
                Next
                text.Inlines.Add(New Run(cell.Text.Substring(offset)))
                Dim border = RowBorder(text, cell.IsHeader)
                Grid.SetRow(border, cell.Row)
                Grid.SetColumn(border, cell.Column)
                Grid.SetRowSpan(border, cell.RowSpan)
                Grid.SetColumnSpan(border, cell.ColumnSpan)
                grid.Children.Add(border)
            Next
            Rows.Children.Add(grid)
        Next
    End Sub

    Private Class PlayerRow
        Public Container As Border
        Public Avatar As Image
        Public AvatarLoading As Boolean
        Public ReadOnly Values As New List(Of TextBlock)
    End Class
End Class
