Imports System.Reflection
Imports System.Security.Cryptography

''' <summary>仅使用框架类型，允许在应用的最早期安装依赖解析器。</summary>
Friend Module EmbeddedDependencies
    Private ReadOnly Assemblies As New ConcurrentDictionary(Of String, Lazy(Of Assembly))(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly ManagedNames As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        "Microsoft.Web.WebView2.Core", "Microsoft.Web.WebView2.Wpf", "HtmlAgilityPack", "System.Numerics.Vectors"}

    Public Function Resolve(sender As Object, args As ResolveEventArgs) As Assembly
        Dim name = New AssemblyName(args.Name).Name
        If Not ManagedNames.Contains(name) Then Return Nothing
        Return Assemblies.GetOrAdd(name, Function(key) New Lazy(Of Assembly)(
            Function() Assembly.Load(ReadResource("PCL.Embedded." & key & ".dll")),
            Threading.LazyThreadSafetyMode.ExecutionAndPublication)).Value
    End Function

    Private Function ReadResource(name As String) As Byte()
        Using source = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            If source Is Nothing Then Throw New IO.FileNotFoundException("EXE 中缺少内置组件，请重新获取完整的启动器 EXE。", name)
            Using target As New IO.MemoryStream()
                source.CopyTo(target)
                Return target.ToArray()
            End Using
        End Using
    End Function

    ''' <summary>原生 DLL 必须落盘；按架构和内容哈希隔离，避免与其他构建互相覆盖。</summary>
    Public Function PrepareWebViewLoader(Optional cacheRoot As String = Nothing) As String
        Dim architecture = If(IntPtr.Size = 4, "win-x86",
            If(String.Equals(Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE"), "ARM64", StringComparison.OrdinalIgnoreCase), "win-arm64", "win-x64"))
        Dim bytes = ReadResource("PCL.Embedded." & architecture & ".WebView2Loader.dll")
        Dim digest As String
        Using sha = SHA256.Create()
            digest = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant()
        End Using
        Dim folder = IO.Path.Combine(If(cacheRoot, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
            "PCL-Union", "THUnion", "native", digest, architecture)
        Dim destination = IO.Path.Combine(folder, "WebView2Loader.dll")
        Using guard As New Threading.Mutex(False, "Local\PCL-Union-WebViewLoader-" & digest)
            Dim acquired = False
            Try
                Try
                    acquired = guard.WaitOne(TimeSpan.FromSeconds(30))
                Catch ex As Threading.AbandonedMutexException
                    acquired = True
                End Try
                If Not acquired Then Throw New TimeoutException("等待内置网页组件初始化超时，请重试。")
                IO.Directory.CreateDirectory(folder)
                Dim valid = False
                If IO.File.Exists(destination) Then
                    Using sha = SHA256.Create(), existing = IO.File.OpenRead(destination)
                        valid = BitConverter.ToString(sha.ComputeHash(existing)).Replace("-", "").ToLowerInvariant() = digest
                    End Using
                End If
                If Not valid Then
                    Dim temporary = IO.Path.Combine(folder, Guid.NewGuid().ToString("N") & ".tmp")
                    Try
                        IO.File.WriteAllBytes(temporary, bytes)
                        If IO.File.Exists(destination) Then
                            IO.File.Replace(temporary, destination, Nothing)
                        Else
                            IO.File.Move(temporary, destination)
                        End If
                    Finally
                        If IO.File.Exists(temporary) Then IO.File.Delete(temporary)
                    End Try
                End If
                Return folder
            Finally
                If acquired Then guard.ReleaseMutex()
            End Try
        End Using
    End Function
End Module
