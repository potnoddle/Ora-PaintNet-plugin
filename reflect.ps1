param(
    [string]$ClassName = "PaintDotNet.Data.FileType"
)

$pdnDir = "C:\Program Files\Paint.NET"

if (-not (Test-Path $pdnDir)) {
    Write-Host "Paint.NET installation directory not found at $pdnDir" -ForegroundColor Red
    Exit
}

$xmlFiles = Get-ChildItem -Path $pdnDir -Filter "PaintDotNet.*.xml"

if (-not $xmlFiles) {
    Write-Host "No Paint.NET XML documentation files found at $pdnDir" -ForegroundColor Red
    Exit
}

$foundAny = $false

foreach ($file in $xmlFiles) {
    try {
        [xml]$xml = Get-Content $file.FullName
        $members = $xml.doc.members.member | Where-Object { $_.name -like "*:$ClassName.*" -or $_.name -like "*:$ClassName" }

        if ($members) {
            $foundAny = $true
            Write-Host "==========================================================" -ForegroundColor Cyan
            Write-Host "Members of $ClassName in $($file.Name):" -ForegroundColor Green
            Write-Host "==========================================================" -ForegroundColor Cyan

            foreach ($m in $members) {
                $nameType = $m.name.Substring(0, 2)
                $prefix = $nameType + $ClassName + "."
                $memberName = $m.name.Replace($prefix, "")
                if ($memberName -eq $m.name) {
                    $memberName = $m.name.Substring(2)
                }
                
                $typeStr = switch ($nameType) {
                    "M:" { "Method" }
                    "P:" { "Property" }
                    "F:" { "Field" }
                    "T:" { "Type" }
                    Default { "Member" }
                }

                Write-Host (" - " + $typeStr + ": " + $memberName) -ForegroundColor White
                if ($m.summary) {
                    $summary = ([string]$m.summary.InnerText).Trim() -replace '\s+', ' '
                    Write-Host "   Description: $summary" -ForegroundColor Gray
                }
                Write-Host ""
            }
        }
    } catch {
        Write-Host "Error parsing $($file.Name): $_" -ForegroundColor Red
    }
}

if (-not $foundAny) {
    Write-Host "No documented members found for $ClassName in any Paint.NET XML file." -ForegroundColor Yellow
    Write-Host "Tip: You can pass a different class name, e.g.: .\reflect.ps1 -ClassName `"PaintDotNet.Document`"" -ForegroundColor Gray
}
