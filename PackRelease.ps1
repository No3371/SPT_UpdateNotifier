$from = $pwd
cd $PSScriptRoot
Get-Content ".\UpdateNotifierPlugin.cs" | Select-String 'public const string ModVersion = "([0-9.]+)"' | ForEach-Object {
    $v = $_.Matches[0].Groups[1].Value
  }
Write-Host($v)
Bandizip.exe c -root:BepInEx\plugins "UpdateNotifier_${v}.zip" ..\..\BepInEx\plugins\UpdateNotifier.dll
cd $from