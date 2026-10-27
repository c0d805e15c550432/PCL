Public Class PageTHUnionLeft
    Public PageID As FormMain.PageSubType = FormMain.PageSubType.THUnionOnline
    Private ReadOnly Pages As New Dictionary(Of Integer, MyPageRight)
    Private Selecting As Boolean

    Public Sub New()
        InitializeComponent()
    End Sub

    Public Sub SelectItem(id As FormMain.PageSubType)
        Selecting = True
        Try
            PanItem.Children.OfType(Of MyListItem)().First(Function(item) CInt(item.Tag) = CInt(id)).SetChecked(True, False, False)
        Finally
            Selecting = False
        End Try
    End Sub

    Public Function PageGet(Optional id As FormMain.PageSubType = -1) As MyPageRight
        If Not EnableTHUnion Then Throw New InvalidOperationException("此构建未启用 THUnion。")
        If id = -1 Then id = PageID
        If CInt(id) < 0 OrElse CInt(id) > 7 Then Throw New ArgumentOutOfRangeException(NameOf(id))
        If Not Pages.ContainsKey(CInt(id)) Then
            If id = FormMain.PageSubType.THUnionOnline OrElse id = FormMain.PageSubType.THUnionIndustry OrElse id = FormMain.PageSubType.THUnionServers Then
                Pages.Add(CInt(id), New PageTHUnionData(CInt(id)))
            Else
                Pages.Add(CInt(id), New PageTHUnionWeb(CInt(id)))
            End If
        End If
        PageID = id
        SelectItem(id)
        Return Pages(CInt(id))
    End Function

    Private Sub PageCheck(sender As MyListItem, e As EventArgs) Handles ItemOnline.Check, ItemMap.Check, ItemIndustry.Check, ItemWiki.Check, ItemServers.Check, ItemSecond.Check, ItemFirst.Check, ItemRules.Check
        If Selecting OrElse sender.Tag Is Nothing OrElse FrmMain Is Nothing Then Return
        Dim id = CType(CInt(sender.Tag), FormMain.PageSubType)
        If id = PageID Then Return
        '从主导航进入 THUnion 时仍走完整切换；组内切换只更新右侧。
        FrmMain.PageChange(FormMain.PageType.THUnion, id)
    End Sub

    Public Sub PageChange(id As FormMain.PageSubType)
        If Not EnableTHUnion OrElse id = PageID Then Return
        AniControlEnabled += 1
        Try
            Dim target = PageGet(id)
            AniStop("FrmMain PageChangeRight")
            FrmMain.PageRight = target
            CType(FrmMain.PanMainRight.Child, MyPageRight).PageOnExit()
            AniStart({
                AaCode(
                Sub()
                    CType(FrmMain.PanMainRight.Child, MyPageRight).PageOnForceExit()
                    FrmMain.PanMainRight.Child = FrmMain.PageRight
                    FrmMain.PageRight.Opacity = 0
                End Sub, 130),
                AaCode(
                Sub()
                    FrmMain.PageRight.Opacity = 1
                    FrmMain.PageRight.PageOnEnter()
                End Sub, 30, True)
            }, "PageLeft PageChange")
        Catch ex As Exception
            Logger.Error(ex, "切换 THUnion 子页面失败")
        Finally
            AniControlEnabled -= 1
        End Try
    End Sub
End Class
