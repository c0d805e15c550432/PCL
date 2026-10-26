param([string]$OutputFile = '')
$ErrorActionPreference = 'Stop'
# No token, username, device identity or ciphertext is returned or saved.
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.IO;
using System.Security.Cryptography;
public static class OfficialAccountCipherCheck {
    private static ulong Hash(string text) {
        ulong result = 5381;
        unchecked { foreach (char value in text) result = (result << 5) ^ result ^ value; }
        return result ^ 0xA98F501BC684032FUL;
    }
    public static string Decrypt(string value, string hardware, string seed) {
        string h = Hash(hardware + seed).ToString("X").PadLeft(16, '7');
        string id = h.Substring(4,4) + "-" + h.Substring(12,4) + "-" + h.Substring(0,4) + "-" + h.Substring(8,4);
        string k = Hash("PCL" + id).ToString().PadLeft(8, 'X').Substring(0,8);
        using (var des = DES.Create())
        using (var stream = new MemoryStream())
        using (var crypto = new CryptoStream(stream, des.CreateDecryptor(Encoding.UTF8.GetBytes(k), Encoding.UTF8.GetBytes("95168702")), CryptoStreamMode.Write)) {
            byte[] data = Convert.FromBase64String(value);
            crypto.Write(data,0,data.Length); crypto.FlushFinalBlock();
            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }
}
'@
$source = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\PCL', $false)
if ($null -eq $source) { throw '未找到官方账户配置；未修改任何账户数据。' }
$hardwareKey = $null
try {
    $seed = [string]$source.GetValue('Identify', '')
    if ($seed.Length -lt 3) { throw '官方识别码种子缺失。' }
    $hardwareKey = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey('SYSTEM\HardwareConfig', $false)
    $hardware = if ($null -eq $hardwareKey) { 'Unknown' } else { ([string]$hardwareKey.GetValue('LastConfig', 'Unknown')).ToUpperInvariant().Trim('{', '}') }
    $original = [string]$source.GetValue('LoginMsJson', '')
    $plain = [OfficialAccountCipherCheck]::Decrypt($original, $hardware, $seed)
    $accounts = ConvertFrom-Json -InputObject $plain -AsHashtable
    if ($accounts.Count -lt 1) { throw '官方账户列表为空。' }
    foreach ($value in $accounts.Values) { if ($value -isnot [string] -or [string]::IsNullOrWhiteSpace($value)) { throw '账户记录无效。' } }
    $protected = [Security.Cryptography.ProtectedData]::Protect([Text.Encoding]::UTF8.GetBytes($plain), $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
    $roundTrip = [Text.Encoding]::UTF8.GetString([Security.Cryptography.ProtectedData]::Unprotect($protected, $null, [Security.Cryptography.DataProtectionScope]::CurrentUser))
    $decryptedFields = 0
    foreach ($name in @('CacheMsV2OAuthRefresh', 'CacheMsV2Access', 'CacheMsV2ProfileJson', 'CacheMsV2Uuid', 'CacheMsV2Name', 'CacheMsV2Expires')) {
        $cipher = [string]$source.GetValue($name, '')
        if ($cipher) { $unused = [OfficialAccountCipherCheck]::Decrypt($cipher, $hardware, $seed); $decryptedFields++ }
    }
    $result = [ordered]@{ officialAccountCount = $accounts.Count; decryptedCacheFields = $decryptedFields; dpapiRoundTrip = ($plain -ceq $roundTrip); officialRecordUnchanged = ($original -ceq [string]$source.GetValue('LoginMsJson', '')); sourceOpenedReadOnly = $true; networkLoginPerformed = $false }
    if (-not $result.dpapiRoundTrip -or -not $result.officialRecordUnchanged) { throw '账户兼容验证未通过。' }
    $json = $result | ConvertTo-Json
    if ($OutputFile) { [IO.File]::WriteAllText($OutputFile, $json, [Text.UTF8Encoding]::new($false)) }
    $json
} catch {
    throw ('官方账户只读验证未通过：' + $_.Exception.GetType().Name + '。未输出账户内容。')
} finally {
    $source.Dispose()
    if ($hardwareKey) { $hardwareKey.Dispose() }
    $plain = $roundTrip = $unused = $accounts = $null
}
