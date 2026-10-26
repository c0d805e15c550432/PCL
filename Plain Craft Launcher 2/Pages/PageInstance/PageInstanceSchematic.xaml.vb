Imports PCLCS.Schematics

Public Class PageInstanceSchematic
    Implements IRefreshable

    Private Library As SchematicLibrary
    Private Entries As New List(Of SchematicFile)
    Private ReadOnly Selected As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
    Private ScanCancellation As CancellationTokenSource
    Private DetailCancellation As CancellationTokenSource
    Private Filter As String = ""
    Private Busy As Boolean
    Private Rendering As Boolean

    Private Sub PageLoaded() Handles Me.Loaded
        If FrmInstanceLeft IsNot Nothing Then FrmInstanceLeft.ItemSchematic.Checked = True
        For Each Button As MyRadioButton In PanFilter.Children
            Button.LabText.Margin = New Thickness(-2, 0, 8, 0)
        Next
        Reload()
    End Sub

    Private Sub PageUnloaded() Handles Me.Unloaded
        ScanCancellation?.Cancel()
        DetailCancellation?.Cancel()
    End Sub

    Public Async Sub Reload() Implements IRefreshable.Refresh
        If Busy OrElse PageInstanceLeft.Instance Is Nothing Then Return
        ScanCancellation?.Cancel()
        DetailCancellation?.Cancel()
        Dim Cancellation As New CancellationTokenSource
        ScanCancellation = Cancellation
        Try
            Dim CurrentLibrary As New SchematicLibrary(PageInstanceLeft.Instance.PathIndie)
            If Library Is Nothing OrElse Library.Root <> CurrentLibrary.Root Then Selected.Clear()
            Library = CurrentLibrary
            BtnFolder.ToolTip = Library.Root
            Entries.Clear()
            PanList.Children.Clear()
            RefreshSelection()
            LabStatus.Visibility = Visibility.Visible
            LabStatus.Text = "正在加载投影列表…"
            Dim Warnings As New List(Of String)
            Dim Result = Await Task.Run(Function() CurrentLibrary.Scan(Cancellation.Token, Warnings))
            If Cancellation.IsCancellationRequested Then Return
            Entries = Result
            PanListBack.ToolTip = If(Warnings.Count = 0, Nothing, String.Join(vbCrLf, Warnings))
            ApplyFilter()
        Catch ex As OperationCanceledException
        Catch ex As Exception
            If Not Cancellation.IsCancellationRequested Then LabStatus.Text = "加载失败：" & ex.Message
        Finally
            If ScanCancellation Is Cancellation Then ScanCancellation = Nothing
            Cancellation.Dispose()
        End Try
    End Sub

    Private Sub FilterChanged(sender As Object, ByMouse As Boolean) Handles BtnFilterAll.Check, BtnFilterLitematic.Check, BtnFilterLegacy.Check, BtnFilterSponge.Check, BtnFilterNbt.Check
        Filter = CStr(CType(sender, MyRadioButton).Tag)
        ApplyFilter()
    End Sub

    Private Sub ApplyFilter() Handles SearchBox.TextChanged
        If PanList Is Nothing OrElse PanFilter Is Nothing Then Return
        Dim Query = SearchBox.Text.Trim()
        Dim Matches = Entries.Where(Function(Item) Item.RelativePath.IndexOf(Query, StringComparison.OrdinalIgnoreCase) >= 0).ToList()
        BtnFilterAll.Text = If(Query = "", "全部", "搜索结果") & $" ({Matches.Count})"
        For Each Button As MyRadioButton In PanFilter.Children
            If Button Is BtnFilterAll Then Continue For
            Dim Extension = CStr(Button.Tag)
            Dim Count = Matches.Where(Function(Item) Item.Format = Extension).Count()
            Button.Text = If(Extension = "NBT", "结构 NBT", If(Extension = "SCHEM", "Sponge", If(Extension = "LITEMATIC", "Litematica", "Schematic"))) & $" ({Count})"
            Button.Visibility = If(Count > 0 OrElse Filter = Extension, Visibility.Visible, Visibility.Collapsed)
        Next
        Dim Visible = Matches.Where(Function(Item) Filter = "" OrElse Item.Format = Filter).ToList()
        Selected.IntersectWith(Visible.Select(Function(Item) Item.FullPath))
        Rendering = True
        Try
            PanList.Children.Clear()
            For Each Item In Visible
                Dim Row As New MySchematicItem With {.Entry = Item, .Checked = Selected.Contains(Item.FullPath)}
                Row.Refresh()
                AddHandler Row.Click, Sub() Row.Checked = Not Row.Checked
                AddHandler Row.Changed, Sub()
                                            If Row.Checked Then Selected.Add(Item.FullPath) Else Selected.Remove(Item.FullPath)
                                            If Not Rendering Then RefreshSelection()
                                        End Sub
                AddHandler Row.MouseRightButtonUp, Sub() ShowDetails(Item)
                Dim Info As New MyIconButton With {.Logo = Logo.IconButtonInfo, .ToolTip = "详情"}
                Dim Locate As New MyIconButton With {.Logo = Logo.IconButtonOpen, .ToolTip = "打开文件位置"}
                Dim Delete As New MyIconButton With {.Logo = Logo.IconButtonDelete, .ToolTip = "移到回收站"}
                AddHandler Info.Click, Sub() ShowDetails(Item)
                AddHandler Locate.Click, Sub() LocateFile(Item)
                AddHandler Delete.Click, Sub() DeleteFiles({Item})
                Row.Buttons = {Info, Locate, Delete}
                PanList.Children.Add(Row)
            Next
        Finally
            Rendering = False
        End Try
        LabStatus.Text = If(Entries.Count = 0, "尚未导入投影，可点击从文件导入添加投影。", "没有符合搜索或分类条件的投影。")
        LabStatus.Visibility = If(Visible.Count = 0, Visibility.Visible, Visibility.Collapsed)
        RefreshSelection()
    End Sub

    Private Sub RefreshSelection()
        If CardSelect Is Nothing Then Return
        CardSelect.Visibility = If(Selected.Count > 0, Visibility.Visible, Visibility.Collapsed)
        CardSelect.IsEnabled = Not Busy
        LabSelect.Text = $"已选择 {Selected.Count} 个文件"
        BtnDetails.IsEnabled = Selected.Count = 1
        BtnRename.IsEnabled = Selected.Count = 1
        PanMain.Margin = New Thickness(25, 10, 25, If(Selected.Count > 0, 92, 10))
        BtnSelectAll.Text = If(Selected.Count > 0 AndAlso Selected.Count = PanList.Children.Count, "取消全选", "全选")
    End Sub

    Private Sub SelectAll() Handles BtnSelectAll.Click
        ChangeSelection(Not (Selected.Count > 0 AndAlso Selected.Count = PanList.Children.Count))
    End Sub

    Private Sub CancelSelection() Handles BtnCancel.Click
        ChangeSelection(False)
    End Sub

    Private Sub ChangeSelection(Value As Boolean)
        Rendering = True
        For Each Row As MySchematicItem In PanList.Children
            Row.Checked = Value
        Next
        Rendering = False
        RefreshSelection()
    End Sub

    Private Function Selection() As List(Of SchematicFile)
        Return Entries.Where(Function(Item) Selected.Contains(Item.FullPath)).ToList()
    End Function

    Public Async Function ReadDetailsAsync(Item As SchematicFile) As Task(Of String)
        Dim FilePath = Library.ValidateFile(Item.FullPath)
        DetailCancellation?.Cancel()
        Dim Cancellation As New CancellationTokenSource
        DetailCancellation = Cancellation
        Try
            Return Await Task.Run(Function() SchematicMetadata.Read(FilePath, Cancellation.Token))
        Finally
            If DetailCancellation Is Cancellation Then DetailCancellation = Nothing
            Cancellation.Dispose()
        End Try
    End Function

    Private Async Sub ShowDetails(Item As SchematicFile)
        If Not CanManage() Then Return
        Try
            Dim Text = Await ReadDetailsAsync(Item)
            If Not IsLoaded Then Return
            MyMsgBox(Text & vbCrLf & vbCrLf & Item.RelativePath, "投影详情")
        Catch ex As OperationCanceledException
        Catch ex As Exception
            Hint("读取失败：" & ex.Message, HintType.Red)
        End Try
    End Sub

    Private Sub ShowSelectedDetails() Handles BtnDetails.Click
        If Selected.Count = 1 Then ShowDetails(Selection().Single())
    End Sub

    Private Function CanManage() As Boolean
        If Busy OrElse Library Is Nothing OrElse PageInstanceLeft.Instance Is Nothing Then Return False
        If McInstanceLock.IsLocked(PageInstanceLeft.Instance) Then
            Hint("该版本正在被修改，请稍后管理投影。", HintType.Red)
            Return False
        End If
        If Not String.Equals(Library.Root, New SchematicLibrary(PageInstanceLeft.Instance.PathIndie).Root, StringComparison.OrdinalIgnoreCase) Then
            Reload()
            Return False
        End If
        Return True
    End Function

    Private Sub SetBusy(Value As Boolean)
        Busy = Value
        PanManage.IsEnabled = Not Value
        PanListBack.IsEnabled = Not Value
        SearchBox.IsEnabled = Not Value
        RefreshSelection()
    End Sub

    Private Async Sub ImportFiles() Handles BtnImport.Click
        If Not CanManage() Then Return
        Dim Dialog As New Microsoft.Win32.OpenFileDialog With {.Title = "导入投影", .Filter = SchematicLibrary.FileFilter, .Multiselect = True}
        If Dialog.ShowDialog() <> True Then Return
        Dim CurrentLibrary = Library
        SetBusy(True)
        Try
            Dim Failures As New List(Of String)
            Await Task.Run(Sub()
                               For Each FilePath In Dialog.FileNames
                                   Try
                                       CurrentLibrary.Import(FilePath)
                                   Catch ex As Exception
                                       Failures.Add(Path.GetFileName(FilePath) & "：" & ex.Message)
                                   End Try
                               Next
                           End Sub)
            If Failures.Count > 0 Then MyMsgBox(String.Join(vbCrLf, Failures.Take(12)), "部分投影未导入")
            Hint($"已导入 {Dialog.FileNames.Length - Failures.Count} 个投影", HintType.Green)
        Catch ex As Exception
            Hint(ex.Message, HintType.Red)
        Finally
            SetBusy(False)
            If IsLoaded Then Reload()
        End Try
    End Sub

    Private Sub OpenFolder() Handles BtnFolder.Click
        If Not CanManage() Then Return
        Try
            Library.EnsureDirectory()
            OpenExplorer(Library.Root & Path.DirectorySeparatorChar)
        Catch ex As Exception
            Hint(ex.Message, HintType.Red)
        End Try
    End Sub

    Private Sub RefreshClick() Handles BtnRefresh.Click
        Reload()
        FrmInstanceLeft?.RefreshSchematicAvailability()
    End Sub

    Private Sub LocateFile(Item As SchematicFile)
        If Not CanManage() Then Return
        Try
            OpenExplorer(Library.ValidateFile(Item.FullPath))
        Catch ex As Exception
            Hint(ex.Message, HintType.Red)
        End Try
    End Sub

    Private Async Sub ExportFiles() Handles BtnExport.Click
        If Not CanManage() Then Return
        Dim Items = Selection()
        If Items.Count = 0 Then Return
        Dim Targets As New List(Of String)
        If Items.Count = 1 Then
            Dim Extension = Path.GetExtension(Items(0).Name)
            Dim Dialog As New Microsoft.Win32.SaveFileDialog With {.Title = "导出投影副本", .FileName = Items(0).Name, .DefaultExt = Extension, .Filter = $"投影文件|*{Extension}"}
            If Dialog.ShowDialog() <> True Then Return
            Targets.Add(Dialog.FileName)
        Else
            Dim Folder = Dialogs.SelectFolder("选择导出文件夹（保留子目录，不覆盖同名文件）", False).FirstOrDefault()
            If Folder Is Nothing Then Return
            Targets.AddRange(Items.Select(Function(Item) Path.Combine(Folder, Item.RelativePath)))
        End If
        Dim CurrentLibrary = Library
        SetBusy(True)
        Try
            Dim Failures As New List(Of String)
            Await Task.Run(Sub()
                               For Index = 0 To Items.Count - 1
                                   Try
                                       CurrentLibrary.Export(Items(Index).FullPath, Targets(Index))
                                   Catch ex As Exception
                                       Failures.Add(Items(Index).Name & "：" & ex.Message)
                                   End Try
                               Next
                           End Sub)
            If Failures.Count > 0 Then MyMsgBox(String.Join(vbCrLf, Failures.Take(12)), "部分投影未导出")
            Hint($"已导出 {Items.Count - Failures.Count} 个投影", HintType.Green)
        Catch ex As Exception
            Hint(ex.Message, HintType.Red)
        Finally
            SetBusy(False)
        End Try
    End Sub

    Private Sub RenameFile() Handles BtnRename.Click
        If Not CanManage() OrElse Selected.Count <> 1 Then Return
        Dim Item = Selection().Single()
        Dim NewName = MyMsgBoxInput("重命名投影", "保留扩展名与内部元数据；已有放置可能需要重新关联。", Path.GetFileNameWithoutExtension(Item.Name), New ObjectModel.Collection(Of Validate))
        If NewName Is Nothing Then Return
        Try
            DetailCancellation?.Cancel()
            Library.Rename(Item.FullPath, NewName)
            Reload()
        Catch ex As Exception
            Hint(ex.Message, HintType.Red)
        End Try
    End Sub

    Private Sub DeleteSelected() Handles BtnDelete.Click
        DeleteFiles(Selection())
    End Sub

    Private Sub DeleteFiles(Items As IEnumerable(Of SchematicFile))
        If Not CanManage() Then Return
        Dim Files = Items.ToList()
        If Files.Count = 0 Then Return
        If MyMsgBox($"将以下 {Files.Count} 个投影移到回收站？" & vbCrLf & String.Join(vbCrLf, Files.Take(8).Select(Function(Item) Item.RelativePath)), "删除投影", "移到回收站", "取消", IsWarn:=True) <> 1 Then Return
        DetailCancellation?.Cancel()
        SetBusy(True)
        Try
            For Each Item In Files
                My.Computer.FileSystem.DeleteFile(Library.ValidateFile(Item.FullPath), FileIO.UIOption.AllDialogs, FileIO.RecycleOption.SendToRecycleBin)
            Next
        Catch ex As OperationCanceledException
        Catch ex As Exception
            Hint("删除失败：" & ex.Message, HintType.Red)
        Finally
            SetBusy(False)
            Reload()
        End Try
    End Sub
End Class
