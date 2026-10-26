Imports Microsoft.Win32

Partial Public Class Settings
    '官方配置始终只读。迁移只处理账户白名单，不导入主题、云配置或启动参数。
    Public Shared Function TryImportOfficialLogin() As Boolean
        Try
            Using Source = Registry.CurrentUser.OpenSubKey("Software\PCL", False)
                If Source Is Nothing Then Return False
                Dim Hardware As String = "Unknown"
                Using Key = Registry.LocalMachine.OpenSubKey("SYSTEM\HardwareConfig", False)
                    If Key IsNot Nothing Then Hardware = CStr(Key.GetValue("LastConfig", "Unknown")).ToUpperInvariant().Trim("{"c, "}"c)
                End Using
                Using Target = Registry.CurrentUser.CreateSubKey("Software\" & RegFolder)
                    Return ImportOfficialLoginFromKeys(Source, Target, Hardware)
                End Using
            End Using
        Catch ex As Exception
            '异常信息可能包含账户内容；这里仅记录失败类型，不记录令牌或明文。
            Logger.Warn("官方登录状态迁移未完成：" & ex.GetType().Name)
            Return False
        End Try
    End Function

    Friend Shared Function ImportOfficialLoginFromKeys(Source As RegistryKey, Target As RegistryKey, Hardware As String) As Boolean
        If CStr(Target.GetValue("OfficialLoginImportedV1", "")) = "True" Then Return False
        Dim LocalJson = CStr(Target.GetValue("LoginMsJson", ""))
        If LocalJson <> "" AndAlso JObject.Parse(DesDecrypt(LocalJson, "PCL" & Identify)).Count > 0 Then Return False
        For Each Name In {"CacheMsV2OAuthRefresh", "CacheMsOAuthRefresh"}
            Dim Existing = CStr(Target.GetValue(Name, ""))
            If Existing <> "" AndAlso DesDecrypt(Existing, "PCL" & Identify) <> "" Then Return False
        Next

        Dim Seed = CStr(Source.GetValue("Identify", ""))
        If Seed.Length < 3 Then Return False '不得生成、猜测或改写官方设备种子。
        Dim Hash = (Hardware & Seed).GetStableHashCode().ToString("X").EnsureLength("7"c, 16)
        Dim OfficialId = $"{Hash.Substring(4, 4)}-{Hash.Substring(12, 4)}-{Hash.Substring(0, 4)}-{Hash.Substring(8, 4)}"
        Dim ReadOfficial As Func(Of String, String) =
            Function(Name)
                Dim Value = CStr(Source.GetValue(Name, ""))
                If Value.Length > 2 * 1024 * 1024 Then Throw New InvalidDataException("账户配置过大")
                Return If(Value = "", "", DesDecrypt(Value, "PCL" & OfficialId))
            End Function

        Dim Accounts As New JObject
        Dim Json = ReadOfficial("LoginMsJson")
        If Json <> "" Then
            Using Reader As New Newtonsoft.Json.JsonTextReader(New StringReader(Json)) With {.MaxDepth = 8}
                Accounts = JObject.Load(Reader)
            End Using
        End If
        If Accounts.Count > 100 Then Throw New InvalidDataException("账户数量过多")
        For Each Account In Accounts.Properties()
            If Account.Value.Type <> JTokenType.String OrElse String.IsNullOrWhiteSpace(CStr(Account.Value)) Then Throw New InvalidDataException("账户记录不完整")
        Next

        '兼容旧版单账户缓存；新版账户列表会在首次使用时自动刷新已有令牌。
        If Accounts.Count = 0 Then
            Dim Name = ReadOfficial("CacheMsV2Name")
            Dim Token = ReadOfficial("CacheMsV2OAuthRefresh")
            If Token = "" Then
                Name = ReadOfficial("CacheMsName")
                Token = ReadOfficial("CacheMsOAuthRefresh")
            End If
            If Name = "" OrElse Token = "" Then Return False
            Accounts(Name) = Token
        End If
        Dim Values As New Dictionary(Of String, Object) From {
            {"LoginMsJson", Accounts.ToString(Newtonsoft.Json.Formatting.None)},
            {"LoginType", McLoginType.Ms},
            {"CacheMsV2Migrated", True}}
        For Each Name In {"CacheMsV2OAuthRefresh", "CacheMsV2Access", "CacheMsV2ProfileJson", "CacheMsV2Uuid", "CacheMsV2Name", "CacheMsV2Expires"}
            Dim Value = ReadOfficial(Name)
            If Value <> "" Then Values(Name) = CTypeDynamic(Value, Entries(Name).Type)
        Next
        If Not Values.ContainsKey("CacheMsV2OAuthRefresh") Then
            Dim First = Accounts.Properties().First()
            Values("CacheMsV2Name") = First.Name
            Values("CacheMsV2OAuthRefresh") = CStr(First.Value)
        End If

        '先验证全部数据并完成重加密；写入失败时恢复本分支原值，绝不回写官方配置。
        Dim Encoded As New Dictionary(Of String, String)
        For Each Pair In Values
            Encoded(Pair.Key) = If(Entries(Pair.Key).Encrypted, DesEncrypt(CStr(Pair.Value), "PCL" & Identify), CStr(Pair.Value))
        Next
        Encoded("OfficialLoginImportedV1") = "True"
        Dim Previous As New Dictionary(Of String, Object)
        Dim Kinds As New Dictionary(Of String, RegistryValueKind)
        For Each Name In Encoded.Keys
            Previous(Name) = Target.GetValue(Name, Nothing)
            If Previous(Name) IsNot Nothing Then Kinds(Name) = Target.GetValueKind(Name)
        Next
        Try
            For Each Pair In Encoded
                Target.SetValue(Pair.Key, Pair.Value, RegistryValueKind.String)
            Next
            Target.Flush()
        Catch
            For Each Pair In Previous
                If Pair.Value Is Nothing Then
                    Target.DeleteValue(Pair.Key, False)
                Else
                    Target.SetValue(Pair.Key, Pair.Value, Kinds(Pair.Key))
                End If
            Next
            Throw
        End Try
        For Each Pair In Values
            Entries(Pair.Key).Set(Pair.Value, Nothing)
        Next
        Return True
    End Function
End Class
